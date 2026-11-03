namespace KnS.Data.Repositories;

public sealed record FellowRecord
{
    public long FellowNumber { get; init; }
    public long FellowId { get; init; }
    public long Level { get; init; }
    public long Experience { get; init; }
    public long Upgrade { get; init; }
    public long Locked { get; init; }

    /// <summary>The level cap, from master_fellow.</summary>
    public long MaxLevel { get; init; }

    public long FailCount { get; init; }
    public long AttackBonus { get; init; }
}

/// <summary>A fellow with what power-up and enhance (303 / 307) read: its grade and its upgrade state.</summary>
public sealed record FellowUpgradeRecord
{
    public long FellowNumber { get; init; }
    public long FellowId { get; init; }
    public long Grade { get; init; }
    public long Level { get; init; }
    public long MaxLevel { get; init; }
    public long Upgrade { get; init; }
    public long Locked { get; init; }
    public long FailCount { get; init; }
    public long AttackBonus { get; init; }
}

public sealed record FellowProgressRecord
{
    public long FellowNumber { get; init; }
    public long Level { get; init; }
    public long Experience { get; init; }
    public long Grade { get; init; }
    public long MaxLevel { get; init; }
}

public sealed record FellowBookRecord
{
    public long FellowId { get; init; }
    public long RewardReceived { get; init; }
    public long Experience { get; init; }
    public long Level { get; init; }
}

public sealed record FellowSlotRecord
{
    /// <summary>The setup (1-3) the slot belongs to; dungeons use setup 3.</summary>
    public long SetupType { get; init; }

    public long Position { get; init; }
    public long FellowNumber { get; init; }
}

public sealed record FellowSaleRecord
{
    public long SellPrice { get; init; }
    public long Locked { get; init; }
}

/// <summary>
/// Fellows ("allies") a player owns (fellow), their collection book (fellow_book) and the active
/// slots (fellow_active). Fellow numbers are the client's ids (<see cref="WireIdentifiers.FellowNumberOffset"/>).
/// </summary>
public sealed class FellowRepository(DataSession session)
{
    private const long Offset = WireIdentifiers.FellowNumberOffset;

    /// <summary>Every owned fellow, oldest first.</summary>
    public IReadOnlyList<FellowRecord> GetOwned(long userNumber) =>
        session.Query<FellowRecord>(
            "SELECT f.rowid_alias + @offset AS FellowNumber, f.fellow_id AS FellowId, f.level AS Level, f.exp AS Experience,"
            + " f.upgrade AS Upgrade, f.locked AS Locked, m.max_level AS MaxLevel, f.fail_count AS FailCount, f.attack_bonus AS AttackBonus"
            + " FROM fellow f JOIN master_fellow m ON m.fellow_id=f.fellow_id WHERE f.user_no=@userNumber"
            + " ORDER BY f.rowid_alias",
            new { offset = Offset, userNumber });

    public HashSet<long> GetOwnedFellowNumbers(long userNumber) =>
        [.. session.Query<long>("SELECT rowid_alias + @offset FROM fellow WHERE user_no=@userNumber", new { offset = Offset, userNumber })];

    /// <summary>Adds a fellow and returns its fellow number.</summary>
    public long Add(long userNumber, long fellowId, long level) =>
        session.Insert(
            "INSERT INTO fellow(user_no, fellow_id, level) VALUES(@userNumber, @fellowId, @level)",
            new { userNumber, fellowId, level }) + Offset;

    public void Delete(long fellowNumber) =>
        session.Execute("DELETE FROM fellow WHERE rowid_alias=@rowId", new { rowId = fellowNumber - Offset });

    public FellowProgressRecord? FindProgress(long userNumber, long fellowNumber) =>
        session.QueryFirstOrDefault<FellowProgressRecord>(
            "SELECT f.rowid_alias + @offset AS FellowNumber, f.level AS Level, f.exp AS Experience, m.grade AS Grade, m.max_level AS MaxLevel"
            + " FROM fellow f JOIN master_fellow m ON m.fellow_id=f.fellow_id"
            + " WHERE f.rowid_alias=@rowId AND f.user_no=@userNumber",
            new { offset = Offset, rowId = fellowNumber - Offset, userNumber });

    public void SetProgress(long fellowNumber, long level, long experience) =>
        session.Execute(
            "UPDATE fellow SET level=@level, exp=@experience WHERE rowid_alias=@rowId",
            new { level, experience, rowId = fellowNumber - Offset });

    /// <summary>The sale price (master_fellow_upgrade.sell_price for the fellow's grade and upgrade) and lock flag.</summary>
    public FellowSaleRecord? FindSaleInfo(long userNumber, long fellowNumber) =>
        session.QueryFirstOrDefault<FellowSaleRecord>(
            "SELECT price.sell_price AS SellPrice, f.locked AS Locked FROM fellow f"
            + " JOIN master_fellow m ON m.fellow_id=f.fellow_id"
            + " JOIN master_fellow_upgrade price ON price.grade=m.grade AND price.upgrade=f.upgrade"
            + " WHERE f.rowid_alias=@rowId AND f.user_no=@userNumber",
            new { rowId = fellowNumber - Offset, userNumber });

    public FellowUpgradeRecord? FindForUpgrade(long userNumber, long fellowNumber) =>
        session.QueryFirstOrDefault<FellowUpgradeRecord>(
            "SELECT f.rowid_alias + @offset AS FellowNumber, f.fellow_id AS FellowId, m.grade AS Grade, f.level AS Level,"
            + " m.max_level AS MaxLevel, f.upgrade AS Upgrade, f.locked AS Locked, f.fail_count AS FailCount, f.attack_bonus AS AttackBonus"
            + " FROM fellow f JOIN master_fellow m ON m.fellow_id=f.fellow_id"
            + " WHERE f.rowid_alias=@rowId AND f.user_no=@userNumber",
            new { offset = Offset, rowId = fellowNumber - Offset, userNumber });

    public void SetUpgradeState(long fellowNumber, long upgrade, long failCount, long attackBonus) =>
        session.Execute(
            "UPDATE fellow SET upgrade=@upgrade, fail_count=@failCount, attack_bonus=@attackBonus WHERE rowid_alias=@rowId",
            new { upgrade, failCount, attackBonus, rowId = fellowNumber - Offset });

    public void RecordInBook(long userNumber, long fellowId) =>
        session.Execute(
            $"{session.Dialect.InsertOrIgnore} INTO fellow_book(user_no, fellow_id) VALUES(@userNumber, @fellowId)",
            new { userNumber, fellowId });

    public IReadOnlyList<FellowBookRecord> GetBook(long userNumber) =>
        session.Query<FellowBookRecord>(
            "SELECT fellow_id AS FellowId, recv_reward AS RewardReceived, exp AS Experience, level AS Level"
            + " FROM fellow_book WHERE user_no=@userNumber ORDER BY fellow_id",
            new { userNumber });

    /// <summary>Every filled active slot, by setup and position.</summary>
    public IReadOnlyList<FellowSlotRecord> GetFilledSlots(long userNumber) =>
        session.Query<FellowSlotRecord>(
            "SELECT type AS SetupType, position AS Position, fellow_no AS FellowNumber FROM fellow_active"
            + " WHERE user_no=@userNumber AND fellow_no<>0 ORDER BY type, position",
            new { userNumber });

    public void SetSlot(long userNumber, long setupType, long position, long fellowNumber) =>
        session.Execute(
            $"{session.Dialect.InsertOrReplace} INTO fellow_active(user_no, type, position, fellow_no)"
            + " VALUES(@userNumber, @setupType, @position, @fellowNumber)",
            new { userNumber, setupType, position, fellowNumber });
}
