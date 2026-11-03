using System.Text.Json.Nodes;
using KnS.Data.Repositories;
using KnS.Server.Admin;
using KnS.Server.Configuration;

namespace KnS.Server.Endpoints;

/// <summary>
/// The feature switches behind the X-Admin-Key header: <c>GET /admin/flags</c> lists them,
/// <c>POST /admin/flags</c> with {feature_key, enabled} sets one (creating its row if missing).
/// </summary>
public sealed class FeatureFlagAdminEndpoint(ServerOptions options)
{
    public const string PathPrefix = "/admin/flags";

    public static bool Matches(string target) =>
        target.Split('?', 2)[0].TrimEnd('/').Equals(PathPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task ServeAsync(HttpContext context)
    {
        if (!await AdminAccess.AuthorizeAsync(context, options))
        {
            return;
        }

        var service = context.RequestServices.GetRequiredService<FeatureFlagAdminService>();
        if (HttpMethods.IsPost(context.Request.Method))
        {
            JsonObject body = await AdminAccess.ReadBodyAsync(context);
            AdminResult result = body["enabled"] is JsonValue enabled && enabled.TryGetValue(out bool on)
                ? service.Set(body["feature_key"]?.ToString() ?? "", on)
                : AdminResult.Invalid("needs feature_key (a-z, 0-9, _) and enabled (true/false)");
            await AdminAccess.WriteResultAsync(context, result, () => Overview(service));
        }
        else if (HttpMethods.IsGet(context.Request.Method))
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status200OK, Overview(service));
        }
        else
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status405MethodNotAllowed, new JsonObject { ["error"] = "use GET or POST" });
        }
    }

    private static JsonObject Overview(FeatureFlagAdminService service)
    {
        var list = new JsonArray();
        foreach (FeatureFlagRecord flag in service.List())
        {
            list.Add(new JsonObject
            {
                ["feature_key"] = flag.FeatureKey,
                ["enabled"] = flag.Enabled,
                ["created_date"] = AdminAccess.FormatTime(flag.CreatedDate),
                ["update_date"] = AdminAccess.FormatTime(flag.UpdateDate),
            });
        }

        return new JsonObject { ["flags"] = list };
    }
}
