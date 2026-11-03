namespace KnS.Data.Repositories;

/// <summary>A registered player (gameuser row) as the login and lookup code needs it.</summary>
public sealed record PlayerRecord
{
    public long UserNumber { get; init; }

    /// <summary>The Hub uid the player registered with ("-1" for Hive guest logins).</summary>
    public string? HubUid { get; init; }

    public string? Nickname { get; init; }
}

/// <summary>A player's wallet and progress, as stored on their gameuser row.</summary>
public sealed record PlayerAccountRecord
{
    public long Gold { get; init; }
    public long Gems { get; init; }
    public long HeroPcid { get; init; }
    public long Level { get; init; }
    public long Experience { get; init; }

    /// <summary>Item bag slots bought on top of the free ones (user_inventory_size_cash).</summary>
    public long PurchasedItemSlots { get; init; }

    /// <summary>Ally bag slots bought (user_fellow_inventory_size_cash).</summary>
    public long PurchasedAllySlots { get; init; }
}

/// <summary>The two bags a player can buy slots for.</summary>
public enum BagKind
{
    Items,
    Allies,
}

/// <summary>Players (gameuser) and the extra devices linked to them (device_link).</summary>
public sealed class PlayerRepository(DataSession session)
{
    private const string PlayerColumns = "user_no AS UserNumber, hub_uid AS HubUid, nickname AS Nickname";

    public PlayerRecord? FindByUserNumber(long userNumber) =>
        session.QueryFirstOrDefault<PlayerRecord>(
            $"SELECT {PlayerColumns} FROM gameuser WHERE user_no=@userNumber", new { userNumber });

    /// <summary>The player a linked extra device plays as (e.g. the KnS Dev app next to the normal one).</summary>
    public PlayerRecord? FindByLinkedDevice(string deviceId) =>
        session.QueryFirstOrDefault<PlayerRecord>(
            "SELECT g.user_no AS UserNumber, g.hub_uid AS HubUid, g.nickname AS Nickname FROM device_link d"
            + " JOIN gameuser g ON g.user_no=d.user_no WHERE d.hub_did=@deviceId",
            new { deviceId });

    public PlayerRecord? FindByDevice(string deviceId) =>
        session.QueryFirstOrDefault<PlayerRecord>(
            $"SELECT {PlayerColumns} FROM gameuser WHERE hub_did=@deviceId", new { deviceId });

    /// <summary>The device a player registered with, or null for an unknown user number.</summary>
    public string? FindDeviceId(long userNumber) =>
        session.QueryFirstOrDefault<string>("SELECT hub_did FROM gameuser WHERE user_no=@userNumber", new { userNumber });

    public PlayerRecord? FindNewest() =>
        session.QueryFirstOrDefault<PlayerRecord>(
            $"SELECT {PlayerColumns} FROM gameuser WHERE user_no>0 ORDER BY user_no DESC LIMIT 1");

    /// <summary>Registers a device and returns the new player's user number.</summary>
    public long Create(string deviceId, string hubUid, double createdAt)
    {
        long rowId = session.Insert(
            "INSERT INTO gameuser(user_no, hub_did, hub_uid, nickname, created)"
            + " VALUES(0, @deviceId, @hubUid, '', @createdAt)",
            new { deviceId, hubUid, createdAt });
        long userNumber = WireIdentifiers.UserNumberOffset + rowId;
        session.Execute("UPDATE gameuser SET user_no=@userNumber WHERE rowid_alias=@rowId", new { userNumber, rowId });
        return userNumber;
    }

    public PlayerAccountRecord? FindAccount(long userNumber) =>
        session.QueryFirstOrDefault<PlayerAccountRecord>(
            "SELECT gold AS Gold, cash AS Gems, hero_pcid AS HeroPcid, level AS Level, exp AS Experience,"
            + " inventory_cash AS PurchasedItemSlots, fellow_inventory_cash AS PurchasedAllySlots"
            + " FROM gameuser WHERE user_no=@userNumber",
            new { userNumber });

    /// <summary>Adds (or, with negative amounts, takes) gold and gems.</summary>
    public void AddCurrency(long userNumber, long gold = 0, long gems = 0) =>
        session.Execute(
            "UPDATE gameuser SET gold=gold+@gold, cash=cash+@gems WHERE user_no=@userNumber",
            new { gold, gems, userNumber });

    public void SetLevel(long userNumber, long level, long experience) =>
        session.Execute(
            "UPDATE gameuser SET level=@level, exp=@experience WHERE user_no=@userNumber",
            new { level, experience, userNumber });

    public void SetActiveHero(long userNumber, long heroPcid) =>
        session.Execute("UPDATE gameuser SET hero_pcid=@heroPcid WHERE user_no=@userNumber", new { heroPcid, userNumber });

    public void SetPurchasedSlots(long userNumber, BagKind bag, long purchasedSlots)
    {
        string column = bag == BagKind.Items ? "inventory_cash" : "fellow_inventory_cash";
        session.Execute($"UPDATE gameuser SET {column}=@purchasedSlots WHERE user_no=@userNumber", new { purchasedSlots, userNumber });
    }
}
