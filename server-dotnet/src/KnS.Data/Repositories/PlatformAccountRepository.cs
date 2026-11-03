namespace KnS.Data.Repositories;

public sealed record HubAccountRecord
{
    public string Uid { get; init; } = "";
    public string? LoginKey { get; init; }
    public string? SessionKey { get; init; }
}

public sealed record HiveAccountRecord
{
    /// <summary>The device id (did) issued to the guest.</summary>
    public string Uid { get; init; } = "";

    public string? GuestUid { get; init; }
}

/// <summary>Guest accounts of the Com2uS platform endpoints (account).</summary>
public sealed class PlatformAccountRepository(DataSession session)
{
    /// <param name="guestUid">The raw client value; compared the way SQLite compares it with text.</param>
    public HubAccountRecord? FindHubAccount(object? guestUid) =>
        session.QueryFirstOrDefault<HubAccountRecord>(
            "SELECT uid AS Uid, login_key AS LoginKey, session_key AS SessionKey FROM account WHERE guest_uid=@guestUid",
            new { guestUid });

    public HiveAccountRecord? FindHiveAccount(object? guestUid) =>
        session.QueryFirstOrDefault<HiveAccountRecord>(
            "SELECT uid AS Uid, guest_uid AS GuestUid FROM account WHERE guest_uid=@guestUid",
            new { guestUid });

    public void Add(string uid, object? guestUid, string loginKey, string sessionKey, object? deviceId, double createdAt) =>
        session.Execute(
            "INSERT INTO account(uid, guest_uid, login_key, session_key, did, created)"
            + " VALUES(@uid, @guestUid, @loginKey, @sessionKey, @deviceId, @createdAt)",
            new { uid, guestUid, loginKey, sessionKey, deviceId, createdAt });
}
