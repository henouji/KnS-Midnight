using System.Text.Json.Nodes;
using KnS.Data.Repositories;
using KnS.Server.Admin;
using KnS.Server.Configuration;

namespace KnS.Server.Endpoints;

/// <summary>
/// The owner's view of the tester allowlist, behind the X-Admin-Key header (KNS_ADMIN_KEY):
/// <c>GET /admin/testers</c> lists testers and the refused devices; <c>POST /admin/testers</c>
/// adds {email, note}; <c>.../attach</c> {email, device_id}, <c>.../revoke</c> and
/// <c>.../restore</c> {email} change one; <c>.../approve</c> {email, device_id?} turns a public
/// sign-up into a tester and <c>.../dismiss</c> {email} drops it. Not journaled: nothing here belongs in a log.
/// </summary>
public sealed class TesterAdminEndpoint(ServerOptions options)
{
    public const string PathPrefix = "/admin/testers";

    public static bool Matches(string target) =>
        target.Split('?', 2)[0].TrimEnd('/').StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task ServeAsync(HttpContext context, string target)
    {
        if (!await AdminAccess.AuthorizeAsync(context, options))
        {
            return;
        }

        string action = target.Split('?', 2)[0].TrimEnd('/')[PathPrefix.Length..].Trim('/').ToLowerInvariant();
        var service = context.RequestServices.GetRequiredService<TesterAdminService>();

        if (HttpMethods.IsGet(context.Request.Method) && action.Length == 0)
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status200OK, Overview(service));
            return;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status405MethodNotAllowed, new JsonObject { ["error"] = "use GET or POST" });
            return;
        }

        JsonObject body = await AdminAccess.ReadBodyAsync(context);
        string email = TesterAdminService.NormalizeEmail(body["email"]?.ToString()) ?? "";
        string deviceId = body["device_id"]?.ToString() ?? "";
        AdminResult result = action switch
        {
            "" => service.Add(email, body["note"]?.ToString() ?? ""),
            "attach" => service.AttachDevice(email, deviceId),
            "approve" => service.Approve(email, deviceId),
            "dismiss" => service.Dismiss(email),
            "revoke" => service.SetRevoked(email, true),
            "restore" => service.SetRevoked(email, false),
            _ => AdminResult.NotFound("unknown action"),
        };

        await AdminAccess.WriteResultAsync(context, result, () => Overview(service));
    }

    private static JsonObject Overview(TesterAdminService service)
    {
        var testers = new JsonArray();
        foreach (TesterRecord tester in service.ListTesters())
        {
            testers.Add(new JsonObject
            {
                ["email"] = tester.Email,
                ["device_id"] = tester.DeviceId,
                ["note"] = tester.Note,
                ["revoked"] = tester.Revoked,
            });
        }

        var refused = new JsonArray();
        foreach (RefusedDeviceRecord device in service.ListRefused())
        {
            refused.Add(new JsonObject
            {
                ["device_id"] = device.DeviceId,
                ["user_no"] = device.UserNumber,
                ["attempts"] = device.Attempts,
                ["last_seen"] = AdminAccess.FormatTime(device.LastSeen),
            });
        }

        var requests = new JsonArray();
        foreach (SignupRecord signup in service.ListSignups())
        {
            requests.Add(new JsonObject
            {
                ["email"] = signup.Email,
                ["device_id"] = signup.DeviceId,
                ["note"] = signup.Note,
                ["created"] = AdminAccess.FormatTime(signup.Created),
            });
        }

        return new JsonObject { ["testers"] = testers, ["refused"] = refused, ["requests"] = requests };
    }
}
