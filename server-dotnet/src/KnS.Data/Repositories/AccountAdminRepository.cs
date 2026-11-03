namespace KnS.Data.Repositories;

/// <summary>An account as the admin page lists it.</summary>
public sealed record AccountSummaryRecord
{
    public long UserNumber { get; init; }
    public string? Nickname { get; init; }

    /// <summary>The device the account registered with.</summary>
    public string? DeviceId { get; init; }

    public long Level { get; init; }
    public long Gold { get; init; }
    public long Gems { get; init; }

    /// <summary>Unix seconds.</summary>
    public double Created { get; init; }
}

/// <summary>An extra device that plays an existing account (device_link).</summary>
public sealed record DeviceLinkRecord
{
    public string DeviceId { get; init; } = "";
    public long UserNumber { get; init; }

    /// <summary>Unix seconds.</summary>
    public double Created { get; init; }
}

/// <summary>The support view of accounts and the device links that let another phone play one.</summary>
public sealed class AccountAdminRepository(DataSession session)
{
    /// <summary>Every real account (user_no &gt; 0), oldest first.</summary>
    public IReadOnlyList<AccountSummaryRecord> ListAccounts() =>
        session.Query<AccountSummaryRecord>(
            "SELECT user_no AS UserNumber, nickname AS Nickname, hub_did AS DeviceId, level AS Level, gold AS Gold, cash AS Gems, created AS Created"
            + " FROM gameuser WHERE user_no>0 ORDER BY user_no");

    public IReadOnlyList<DeviceLinkRecord> ListLinks() =>
        session.Query<DeviceLinkRecord>("SELECT hub_did AS DeviceId, user_no AS UserNumber, created AS Created FROM device_link ORDER BY created DESC");

    public bool AccountExists(long userNumber) =>
        session.Exists("SELECT 1 FROM gameuser WHERE user_no=@userNumber AND user_no>0", new { userNumber });

    /// <summary>Makes the device play the account, replacing any earlier link of that device.</summary>
    public void LinkDevice(string deviceId, long userNumber, double now) =>
        session.Execute(
            "INSERT INTO device_link(hub_did, user_no, created) VALUES(@deviceId, @userNumber, @now)"
            + $" {session.Dialect.OnConflictUpdate("hub_did")} user_no=@userNumber, created=@now",
            new { deviceId, userNumber, now });

    /// <summary>Sets the wallet to exact amounts (support corrections).</summary>
    public void SetWallet(long userNumber, long gold, long gems) =>
        session.Execute("UPDATE gameuser SET gold=@gold, cash=@gems WHERE user_no=@userNumber", new { userNumber, gold, gems });

    public void UnlinkDevice(string deviceId) =>
        session.Execute("DELETE FROM device_link WHERE hub_did=@deviceId", new { deviceId });
}
