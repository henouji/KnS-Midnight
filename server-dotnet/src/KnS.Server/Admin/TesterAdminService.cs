using KnS.Data.Repositories;

namespace KnS.Server.Admin;

/// <summary>The owner's actions on the tester allowlist and the public sign-up requests.</summary>
public sealed class TesterAdminService(TesterRepository testers, AdminWork work, TimeProvider clock)
{
    private const int MaxEmailLength = 200;

    public IReadOnlyList<TesterRecord> ListTesters() => testers.List();

    public IReadOnlyList<RefusedDeviceRecord> ListRefused() => testers.ListRefused();

    public IReadOnlyList<SignupRecord> ListSignups() => testers.ListSignups();

    public AdminResult Add(string email, string note) => Run(email, () =>
    {
        if (testers.Find(email) is not null)
        {
            return AdminResult.Conflict("already a tester");
        }

        testers.Add(email, note, Now());
        return AdminResult.Ok;
    });

    public AdminResult AttachDevice(string email, string deviceId) => Run(email, () =>
    {
        deviceId = deviceId.Trim();
        if (testers.Find(email) is null || deviceId.Length == 0)
        {
            return AdminResult.Invalid("needs a known email and a device_id");
        }

        if (DeviceHolder(deviceId, email) is { } holder)
        {
            return AdminResult.Conflict("that device belongs to " + holder.Email);
        }

        testers.SetDevice(email, deviceId);
        return AdminResult.Ok;
    });

    /// <summary>Adds the tester if new and attaches the device (the given one, else the sign-up's).</summary>
    public AdminResult Approve(string email, string deviceId) => Run(email, () =>
    {
        SignupRecord? signup = testers.FindSignup(email);
        deviceId = deviceId.Trim().Length > 0 ? deviceId.Trim() : signup?.DeviceId.Trim() ?? "";
        if (deviceId.Length == 0)
        {
            return AdminResult.Invalid("no device id to attach");
        }

        if (DeviceHolder(deviceId, email) is { } holder)
        {
            return AdminResult.Conflict("that device belongs to " + holder.Email);
        }

        if (testers.Find(email) is null)
        {
            testers.Add(email, signup?.Note ?? "", Now());
        }

        testers.SetDevice(email, deviceId);
        testers.SetRevoked(email, false);
        testers.DeleteSignup(email);
        return AdminResult.Ok;
    });

    public AdminResult Dismiss(string email) => Run(email, () =>
    {
        testers.DeleteSignup(email);
        return AdminResult.Ok;
    });

    public AdminResult SetRevoked(string email, bool revoked) => Run(email, () =>
    {
        if (testers.Find(email) is null)
        {
            return AdminResult.NotFound("no such tester");
        }

        testers.SetRevoked(email, revoked);
        return AdminResult.Ok;
    });

    /// <summary>
    /// Stores a public sign-up for the owner to approve. Already approved (this email or this
    /// device) stores nothing; the caller replies the same either way.
    /// </summary>
    public void RecordSignup(string email, string deviceId, string note) => work.Run(() =>
    {
        if (testers.Find(email) is null && (deviceId.Length == 0 || !testers.IsActiveDevice(deviceId)))
        {
            testers.AddSignup(email, deviceId, note, Now());
        }

        return AdminResult.Ok;
    });

    /// <summary>The email as stored (trimmed, lower case), or null when it is not usable.</summary>
    public static string? NormalizeEmail(string? email)
    {
        string normalized = (email ?? "").Trim().ToLowerInvariant();
        return normalized.Contains('@', StringComparison.Ordinal) && normalized.Length <= MaxEmailLength ? normalized : null;
    }

    private AdminResult Run(string email, Func<AdminResult> action) =>
        NormalizeEmail(email) is null ? AdminResult.Invalid("email is required") : work.Run(action);

    private TesterRecord? DeviceHolder(string deviceId, string email) =>
        testers.FindByDevice(deviceId) is { } holder && holder.Email != email ? holder : null;

    private double Now() => clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0;
}
