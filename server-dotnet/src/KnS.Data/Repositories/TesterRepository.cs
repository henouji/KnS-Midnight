namespace KnS.Data.Repositories;

/// <summary>Someone allowed to play, known by email; the game never sends it, so a device id is attached to it.</summary>
public sealed record TesterRecord
{
    public string Email { get; init; } = "";

    /// <summary>The attached device (hub_did); empty until the tester has tried to log in and the owner attaches it.</summary>
    public string DeviceId { get; init; } = "";

    public string Note { get; init; } = "";

    public bool Revoked { get; init; }
}

/// <summary>A sign-up from the public page, waiting for the owner.</summary>
public sealed record SignupRecord
{
    public string Email { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string Note { get; init; } = "";
    public double Created { get; init; }
}

/// <summary>A device the allowlist turned away, newest first, for the owner to attach to an email.</summary>
public sealed record RefusedDeviceRecord
{
    public string DeviceId { get; init; } = "";
    public long UserNumber { get; init; }
    public long Attempts { get; init; }
    public double LastSeen { get; init; }
}

/// <summary>The tester allowlist (tester) and the devices it refused (access_attempt).</summary>
public sealed class TesterRepository(DataSession session)
{
    /// <summary>Refused devices kept; older ones go, so strangers cannot grow the table.</summary>
    private const int RefusedDevicesKept = 200;

    private const string TesterColumns = "email AS Email, device_id AS DeviceId, note AS Note, revoked AS Revoked";

    /// <summary>Whether the device is attached to a tester whose access is not revoked.</summary>
    public bool IsActiveDevice(string deviceId) =>
        deviceId.Length > 0
        && session.Exists("SELECT 1 FROM tester WHERE device_id=@deviceId AND revoked=0 LIMIT 1", new { deviceId });

    /// <summary>Whether the device is attached to a tester whose access was revoked.</summary>
    public bool IsRevokedDevice(string deviceId) =>
        deviceId.Length > 0
        && session.Exists("SELECT 1 FROM tester WHERE device_id=@deviceId AND revoked=1 LIMIT 1", new { deviceId });

    /// <summary>
    /// Whether the device is linked to an account (device_link) the owner has not locked out. A link is
    /// the owner's decision to let that phone play, so it passes without being a tester itself; revoking
    /// the account's own tester (its registered device) still closes it.
    /// </summary>
    public bool IsLinkedDeviceAllowed(string deviceId) =>
        deviceId.Length > 0
        && session.Exists(LinkedAndNotRevoked + " AND l.hub_did=@deviceId LIMIT 1", new { deviceId });

    /// <summary>Whether some device is linked to the account and the account is not locked out (see <see cref="IsLinkedDeviceAllowed"/>).</summary>
    public bool HasAllowedLink(long userNumber) =>
        session.Exists(LinkedAndNotRevoked + " AND l.user_no=@userNumber LIMIT 1", new { userNumber });

    private const string LinkedAndNotRevoked =
        "SELECT 1 FROM device_link l WHERE NOT EXISTS ("
        + "SELECT 1 FROM tester t JOIN gameuser g ON g.hub_did=t.device_id WHERE g.user_no=l.user_no AND t.revoked=1)";

    public IReadOnlyList<TesterRecord> List() =>
        session.Query<TesterRecord>($"SELECT {TesterColumns} FROM tester ORDER BY email");

    public TesterRecord? Find(string email) =>
        session.QueryFirstOrDefault<TesterRecord>($"SELECT {TesterColumns} FROM tester WHERE email=@email", new { email });

    public TesterRecord? FindByDevice(string deviceId) =>
        deviceId.Length == 0
            ? null
            : session.QueryFirstOrDefault<TesterRecord>($"SELECT {TesterColumns} FROM tester WHERE device_id=@deviceId", new { deviceId });

    public void Add(string email, string note, double createdAt) =>
        session.Execute(
            "INSERT INTO tester(email, device_id, note, revoked, created) VALUES(@email, '', @note, 0, @createdAt)",
            new { email, note, createdAt });

    public void SetDevice(string email, string deviceId) =>
        session.Execute("UPDATE tester SET device_id=@deviceId WHERE email=@email", new { email, deviceId });

    public void SetNote(string email, string note) =>
        session.Execute("UPDATE tester SET note=@note WHERE email=@email", new { email, note });

    public void SetRevoked(string email, bool revoked) =>
        session.Execute("UPDATE tester SET revoked=@revoked WHERE email=@email", new { email, revoked = revoked ? 1 : 0 });

    /// <summary>Pending sign-ups kept; the oldest go, so strangers cannot grow the table.</summary>
    private const int SignupsKept = 200;

    public IReadOnlyList<SignupRecord> ListSignups() =>
        session.Query<SignupRecord>("SELECT email AS Email, device_id AS DeviceId, note AS Note, created AS Created FROM signup_request ORDER BY created DESC");

    public SignupRecord? FindSignup(string email) =>
        session.QueryFirstOrDefault<SignupRecord>(
            "SELECT email AS Email, device_id AS DeviceId, note AS Note, created AS Created FROM signup_request WHERE email=@email", new { email });

    /// <summary>Records (or replaces, per email) a sign-up, then trims the table.</summary>
    public void AddSignup(string email, string deviceId, string note, double now)
    {
        session.Execute(
            "INSERT INTO signup_request(email, device_id, note, created) VALUES(@email, @deviceId, @note, @now)"
            + $" {session.Dialect.OnConflictUpdate("email")} device_id=@deviceId, note=@note, created=@now",
            new { email, deviceId, note, now });
        session.Execute(
            "DELETE FROM signup_request WHERE email NOT IN"
            + $" (SELECT email FROM (SELECT email FROM signup_request ORDER BY created DESC LIMIT {SignupsKept}) newest)");
    }

    public void DeleteSignup(string email) => session.Execute("DELETE FROM signup_request WHERE email=@email", new { email });

    /// <summary>Notes a refusal (once per device, counting repeats), then trims the table.</summary>
    public void RecordRefusal(string deviceId, long userNumber, double now)
    {
        session.Execute(
            "INSERT INTO access_attempt(device_id, user_no, attempts, last_seen) VALUES(@deviceId, @userNumber, 1, @now)"
            + $" {session.Dialect.OnConflictUpdate("device_id")} attempts=attempts+1, last_seen=@now, user_no=@userNumber",
            new { deviceId, userNumber, now });
        session.Execute(
            "DELETE FROM access_attempt WHERE device_id NOT IN"
            + $" (SELECT device_id FROM (SELECT device_id FROM access_attempt ORDER BY last_seen DESC LIMIT {RefusedDevicesKept}) newest)");
    }

    /// <summary>Refused devices not yet attached to a tester, newest first.</summary>
    public IReadOnlyList<RefusedDeviceRecord> ListRefused() =>
        session.Query<RefusedDeviceRecord>(
            "SELECT device_id AS DeviceId, user_no AS UserNumber, attempts AS Attempts, last_seen AS LastSeen FROM access_attempt"
            + " WHERE device_id NOT IN (SELECT device_id FROM tester WHERE device_id<>'') ORDER BY last_seen DESC");
}
