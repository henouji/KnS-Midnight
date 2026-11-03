using KnS.Data.Dialects;
using Microsoft.Extensions.Logging;

namespace KnS.Data.Schema;

/// <summary>
/// Creates the tables (when missing) and adds columns newer server versions introduced. The
/// layout is exactly the Python server's, so both servers run on the same saved data.
/// </summary>
internal sealed class GameSchema(DataSession session, string masterDataDirectory, ILogger logger)
{
    private SqlDialect Sql => session.Dialect;

    public void Create()
    {
        CreatePlatformTables();
        CreatePlayerTables();

        var seeder = new MasterDataSeeder(session, masterDataDirectory, logger);
        foreach (MasterTable master in MasterTables.All)
        {
            seeder.Seed(master);
        }

        CreateInventoryTables();
        CreateFellowTables();
        CreateStageTables();
    }

    /// <summary>Hive / Hub guest accounts of the Com2uS platform endpoints.</summary>
    private void CreatePlatformTables()
    {
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS account(
                uid {Sql.KeyTextColumn} PRIMARY KEY, guest_uid {Sql.TextColumn}, login_key {Sql.TextColumn}, session_key {Sql.TextColumn},
                did {Sql.TextColumn}, created {Sql.RealColumn})
            """);

        // The tester allowlist (KNS_TESTERS_ONLY): an email, the device attached to it, a revoke flag.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS tester(
                tester_id {Sql.AutoIncrementPrimaryKey},
                email {Sql.KeyTextColumn} UNIQUE, device_id {Sql.KeyTextColumn}, note {Sql.TextColumn},
                revoked {Sql.IntegerColumn}, created {Sql.RealColumn})
            """);

        // Server-side feature switches, changed without a restart (a missing row falls back to FeatureFlags.Defaults).
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS feature_flag(
                feature_key {Sql.KeyTextColumn} PRIMARY KEY, enabled {Sql.IntegerColumn},
                created_date {Sql.RealColumn}, update_date {Sql.RealColumn})
            """);
        double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        session.Execute(
            $"{Sql.InsertOrIgnore} INTO feature_flag(feature_key, enabled, created_date, update_date) VALUES('fourth_fellow', 1, @now, @now)",
            new { now });
        // Sign-up requests from the public page: pending until the owner approves (never grants access by itself).
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS signup_request(
                email {Sql.KeyTextColumn} PRIMARY KEY, device_id {Sql.KeyTextColumn}, note {Sql.TextColumn}, created {Sql.RealColumn})
            """);
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS access_attempt(
                device_id {Sql.KeyTextColumn} PRIMARY KEY, user_no {Sql.IntegerColumn}, attempts {Sql.IntegerColumn}, last_seen {Sql.RealColumn})
            """);
    }

    private void CreatePlayerTables()
    {
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS gameuser(
                rowid_alias {Sql.AutoIncrementPrimaryKey},
                user_no {Sql.IntegerColumn}, hub_did {Sql.KeyTextColumn} UNIQUE, hub_uid {Sql.TextColumn},
                nickname {Sql.TextColumn}, created {Sql.RealColumn})
            """);

        // Extra devices that play an existing account: a device's Hive guest id -> its user_no.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS device_link(
                hub_did {Sql.KeyTextColumn} PRIMARY KEY, user_no {Sql.IntegerColumn}, created {Sql.RealColumn})
            """);

        // Wallet, active hero, level, and the bag slots bought on top of the free ones.
        AddMissingIntegerColumns("gameuser", [
            ("gold", StoredPlayerDefaults.Gold),
            ("cash", StoredPlayerDefaults.Gems),
            ("hero_pcid", StoredPlayerDefaults.HeroPcid),
            ("level", StoredPlayerDefaults.Level),
            ("exp", 0),
            ("inventory_cash", 0),
            ("fellow_inventory_cash", 0),
        ]);

        // Heroes a user owns; hero_no is the per-user id the client echoes back (the hero_pcid).
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS user_hero(
                user_no {Sql.IntegerColumn}, hero_no {Sql.IntegerColumn}, hero_pcid {Sql.IntegerColumn},
                PRIMARY KEY(user_no, hero_no))
            """);

        // Stages a user has cleared, per stage mode (1 normal, 2 hard).
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS stage_clear(
                user_no {Sql.IntegerColumn}, stage_id {Sql.IntegerColumn}, stage_mode {Sql.IntegerColumn}, clear_count {Sql.IntegerColumn},
                PRIMARY KEY(user_no, stage_id, stage_mode))
            """);
    }

    private void CreateInventoryTables()
    {
        // Starter items (fixed numbers, never stored in `item`) the user sold or used up.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS starter_sold(
                user_no {Sql.IntegerColumn}, item_no {Sql.IntegerColumn}, PRIMARY KEY(user_no, item_no))
            """);

        // Items a user owns (claimed=1) or was offered by the current stage start (claimed=0).
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS item(
                rowid_alias {Sql.AutoIncrementPrimaryKey},
                user_no {Sql.IntegerColumn}, item_id {Sql.IntegerColumn}, claimed {Sql.IntegerColumn} DEFAULT 0,
                upgrade {Sql.IntegerColumn} DEFAULT 0, attack {Sql.IntegerColumn} DEFAULT 0,
                defence {Sql.IntegerColumn} DEFAULT 0, hp {Sql.IntegerColumn} DEFAULT 0, locked {Sql.IntegerColumn} DEFAULT 0)
            """);

        // The item each hero wears per equipment range (200000 = weapon, ...). No row = starter item.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS hero_equip(
                user_no {Sql.IntegerColumn}, hero_no {Sql.IntegerColumn}, category {Sql.IntegerColumn}, item_no {Sql.IntegerColumn},
                PRIMARY KEY(user_no, hero_no, category))
            """);

        // Counted etc items (19xxxxx: tickets, collectibles, ...). Gold and gems stay on gameuser.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS user_goods(
                user_no {Sql.IntegerColumn}, item_id {Sql.IntegerColumn}, count {Sql.IntegerColumn} DEFAULT 0,
                PRIMARY KEY(user_no, item_id))
            """);

        // Options (enchantments) of an item, up to 6 slots.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS item_option(
                item_no {Sql.IntegerColumn}, slot {Sql.IntegerColumn}, user_no {Sql.IntegerColumn}, option_id {Sql.IntegerColumn},
                value1 {Sql.IntegerColumn}, value2 {Sql.IntegerColumn}, PRIMARY KEY(item_no, slot))
            """);

        AddMissingIntegerColumns("item", [("upgrade", 0), ("attack", 0), ("defence", 0), ("hp", 0), ("locked", 0)]);
    }

    private void CreateFellowTables()
    {
        // Fellows ("allies") a user owns.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS fellow(
                rowid_alias {Sql.AutoIncrementPrimaryKey},
                user_no {Sql.IntegerColumn}, fellow_id {Sql.IntegerColumn}, level {Sql.IntegerColumn} DEFAULT 1, exp {Sql.IntegerColumn} DEFAULT 0,
                upgrade {Sql.IntegerColumn} DEFAULT 0, locked {Sql.IntegerColumn} DEFAULT 0)
            """);

        // The per-user collection record: one row per fellow_id ever owned.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS fellow_book(
                user_no {Sql.IntegerColumn}, fellow_id {Sql.IntegerColumn}, recv_reward {Sql.IntegerColumn} DEFAULT 0,
                exp {Sql.IntegerColumn} DEFAULT 0, level {Sql.IntegerColumn} DEFAULT 1, PRIMARY KEY(user_no, fellow_id))
            """);

        // Active fellow slots: one row per (setup type 1-3, position 0-2); fellow_no 0 = empty.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS fellow_active(
                user_no {Sql.IntegerColumn}, type {Sql.IntegerColumn}, position {Sql.IntegerColumn}, fellow_no {Sql.IntegerColumn},
                PRIMARY KEY(user_no, type, position))
            """);

        // fail_count = the Power-up Points a failed power-up adds to the next try; attack_bonus = the "+N" the power-ups and enhances carry over.
        AddMissingIntegerColumns("fellow", [("fail_count", 0), ("attack_bonus", 0)]);
    }

    private void CreateStageTables()
    {
        // The counted etc items the current stage start offered. Replaced on every stage start.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS stage_event_offer(
                rowid_alias {Sql.AutoIncrementPrimaryKey},
                user_no {Sql.IntegerColumn}, item_id {Sql.IntegerColumn}, count {Sql.IntegerColumn})
            """);

        // Area-clear rewards a user has claimed, by area and stage mode.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS stage_area_reward(
                user_no {Sql.IntegerColumn}, area_id {Sql.IntegerColumn}, stage_mode {Sql.IntegerColumn},
                PRIMARY KEY(user_no, area_id, stage_mode))
            """);

        // The reward slots the user's last stage start rolled.
        session.Execute($"""
            CREATE TABLE IF NOT EXISTS stage_roll(
                user_no {Sql.IntegerColumn}, slot {Sql.IntegerColumn}, item_id {Sql.IntegerColumn}, count {Sql.IntegerColumn},
                PRIMARY KEY(user_no, slot))
            """);
    }

    private void AddMissingIntegerColumns(string table, IEnumerable<(string Column, long Default)> columns)
    {
        var existing = session.GetColumnNames(table).ToHashSet(StringComparer.Ordinal);
        foreach ((string column, long defaultValue) in columns)
        {
            if (!existing.Contains(column))
            {
                session.Execute($"ALTER TABLE {table} ADD COLUMN {column} {Sql.IntegerColumn} DEFAULT {defaultValue}");
            }
        }
    }
}
