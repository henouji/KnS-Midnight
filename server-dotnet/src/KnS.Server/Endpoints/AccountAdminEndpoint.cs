using System.Globalization;
using System.Text.Json.Nodes;
using KnS.Data.Repositories;
using KnS.Server.Admin;
using KnS.Server.Configuration;

namespace KnS.Server.Endpoints;

/// <summary>
/// Support tool, behind the X-Admin-Key header: <c>GET /admin/accounts</c> lists the accounts and
/// their linked devices; <c>POST .../link</c> {device_id, user_no} makes a device play an old
/// account (a new phone, a reinstall that lost the device id); <c>.../unlink</c> {device_id} undoes it;
/// <c>.../wallet</c> {user_no, gold, gems} corrects an account's balances.
/// The login resolves a linked device before the device's own account. Not journaled.
/// </summary>
public sealed class AccountAdminEndpoint(ServerOptions options)
{
    public const string PathPrefix = "/admin/accounts";

    public static bool Matches(string target) =>
        target.Split('?', 2)[0].TrimEnd('/').StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task ServeAsync(HttpContext context, string target)
    {
        if (!await AdminAccess.AuthorizeAsync(context, options))
        {
            return;
        }

        string action = target.Split('?', 2)[0].TrimEnd('/')[PathPrefix.Length..].Trim('/').ToLowerInvariant();
        var service = context.RequestServices.GetRequiredService<AccountAdminService>();

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
        string deviceId = body["device_id"]?.ToString() ?? "";
        AdminResult result;
        switch (action)
        {
            case "wallet":
                result = TryNumber(body["user_no"], out long walletUser) && TryNumber(body["gold"], out long gold) && TryNumber(body["gems"], out long gems)
                    ? service.SetWallet(walletUser, gold, gems)
                    : AdminResult.Invalid("user_no, gold and gems are required numbers");
                break;
            case "link":
                result = long.TryParse(body["user_no"]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long userNumber)
                    ? service.Link(deviceId, userNumber)
                    : AdminResult.Invalid("user_no is required");
                break;
            case "unlink":
                result = service.Unlink(deviceId);
                break;
            default:
                result = AdminResult.NotFound("unknown action");
                break;
        }

        await AdminAccess.WriteResultAsync(context, result, () => Overview(service));
    }

    private static bool TryNumber(JsonNode? node, out long number) =>
        long.TryParse(node?.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);

    private static JsonObject Overview(AccountAdminService service)
    {
        var accounts = new JsonArray();
        foreach (AccountSummaryRecord account in service.ListAccounts())
        {
            accounts.Add(new JsonObject
            {
                ["user_no"] = account.UserNumber,
                ["nickname"] = account.Nickname,
                ["device_id"] = account.DeviceId,
                ["level"] = account.Level,
                ["gold"] = account.Gold,
                ["gems"] = account.Gems,
                ["created"] = AdminAccess.FormatTime(account.Created),
            });
        }

        var links = new JsonArray();
        foreach (DeviceLinkRecord link in service.ListLinks())
        {
            links.Add(new JsonObject { ["device_id"] = link.DeviceId, ["user_no"] = link.UserNumber, ["created"] = AdminAccess.FormatTime(link.Created) });
        }

        return new JsonObject { ["accounts"] = accounts, ["links"] = links };
    }
}
