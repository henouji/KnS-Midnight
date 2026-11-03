using System.Text.Json;
using System.Text.Json.Nodes;
using KnS.Game.Gateway;
using KnS.Platform.Accounts;
using KnS.Platform.ActiveUser;
using KnS.Platform.Services;
using KnS.Protocol;
using KnS.Protocol.Json;
using KnS.Server.Configuration;
using KnS.Server.Diagnostics;
using KnS.Server.Endpoints;
using Microsoft.AspNetCore.Http.Features;

namespace KnS.Server.Routing;

/// <summary>
/// Sends each request to its surface by substrings of the raw path, in the Python server's order.
/// The APK's every Com2uS host points here, so the path is all there is to tell the game gateway,
/// ActiveUser, Hive, Hub, Mercury and billing apart.
/// </summary>
public sealed class RequestRouter(
    ServerOptions options,
    RequestJournal journal,
    TrafficLog traffic,
    HealthCheck health,
    AdditionFileEndpoint additionFiles,
    DownloadPageEndpoint downloads,
    SignupEndpoint signups,
    TesterAdminEndpoint testerAdmin,
    AccountAdminEndpoint accountAdmin,
    FeatureFlagAdminEndpoint flagAdmin,
    ActiveUserGateway activeUser,
    PlatformReplies platformReplies)
{
    private const string JsonContentType = "application/json";

    /// <summary>The game gateway's reply type (base64 text), as the original server sent it.</summary>
    private const string GatewayContentType = "text/plain; charset=ISO-8859-1";

    private static readonly string[] HiveMarkers = ["guest", "/auth", "device", "registered", "acquire_uid"];
    private static readonly string[] HubMarkers = ["/hub", "/user", "member", "policy", ".c2s", "/api"];

    public async Task RouteAsync(HttpContext context)
    {
        // The request target as sent (path and query, not decoded), like Python's self.path.
        // Kestrel always sets it; in-memory test hosts may not.
        string? rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        string target = string.IsNullOrEmpty(rawTarget) ? context.Request.Path + context.Request.QueryString : rawTarget;

        if (target.Split('?', 2)[0] == "/health")
        {
            // Not logged: App Service probes it every minute.
            HealthReport report = health.Run(context.RequestServices);
            await WriteJsonAsync(context, report, report.IsHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
            return;
        }

        if (DownloadPageEndpoint.Matches(context.Request.Method, target))
        {
            // Not journaled: browsers, not the game.
            await downloads.ServeAsync(context, target);
            return;
        }

        if (SignupEndpoint.Matches(context.Request.Method, target))
        {
            await signups.ServeAsync(context);
            return;
        }

        if (FeatureFlagAdminEndpoint.Matches(target))
        {
            await flagAdmin.ServeAsync(context);
            return;
        }

        if (AccountAdminEndpoint.Matches(target))
        {
            await accountAdmin.ServeAsync(context, target);
            return;
        }

        if (TesterAdminEndpoint.Matches(target))
        {
            await testerAdmin.ServeAsync(context, target);
            return;
        }

        if (target.StartsWith(AdditionFileEndpoint.PathPrefix, StringComparison.Ordinal))
        {
            journal.LogRequest(context.Request.Method, target, "-> addition " + target);
            await additionFiles.ServeAsync(context, target);
            return;
        }

        byte[] body = await ReadBodyAsync(context.Request);
        JsonObject fields = ParseFields(body);
        var parameters = new RequestParameters(fields);
        string path = target.ToLowerInvariant();
        journal.LogRequest(context.Request.Method, target, $"({body.Length}b)");
        journal.Capture(context.Request, target, body, fields);

        var exchange = new TrafficExchange(context.Request.Method, target, fields);
        try
        {
            await DispatchAsync(context, target, path, body, parameters, exchange);
        }
        catch (Exception e) when (traffic.Enabled)
        {
            exchange.Failure = $"{e.GetType().Name}: {e.Message}";
            throw;
        }
        finally
        {
            traffic.Write(exchange, context.Response.StatusCode);
        }
    }

    private async Task DispatchAsync(
        HttpContext context, string target, string path, byte[] body, RequestParameters parameters, TrafficExchange exchange)
    {
        if (path.Contains("gateway.php", StringComparison.Ordinal) && !path.Contains("1mhero", StringComparison.Ordinal) && !options.BreakActiveUser)
        {
            // Encrypted both ways; the traffic log keeps only the raw fields.
            exchange.Surface = "activeuser-gateway";
            ActiveUserReply reply = activeUser.Handle(body, context.Request.Headers["REQ-TIMESTAMP"].FirstOrDefault());
            context.Response.Headers["REQ-TIMESTAMP"] = reply.Timestamp;
            context.Response.Headers["REQ-AUTHKEY"] = reply.AuthKey;
            await WriteBodyAsync(context, reply.Body, JsonContentType);
            return;
        }

        if (path.Contains("activeuser", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, exchange, "activeuser", platformReplies.ActiveUserStatus(parameters));
            return;
        }

        if (path.Contains("mercury", StringComparison.Ordinal) || path.Contains("/init", StringComparison.Ordinal)
            || path.Contains("messenger", StringComparison.Ordinal))
        {
            // Suppresses the "service discontinued" notice.
            await WriteJsonAsync(context, exchange, "mercury", platformReplies.Mercury());
            return;
        }

        if (path.Contains("wall", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, exchange, "offerwall", PlatformReplies.Offerwall());
            return;
        }

        if (path.Contains("/1mhero", StringComparison.Ordinal))
        {
            await HandleGameGatewayAsync(context, body, exchange);
            return;
        }

        if (HiveMarkers.Any(marker => path.Contains(marker, StringComparison.Ordinal)))
        {
            await WriteJsonAsync(context, exchange, "hive", context.RequestServices.GetRequiredService<HiveGuestService>().Handle(target, parameters));
            return;
        }

        if (path.Contains("billing", StringComparison.Ordinal) || path.Contains("check2", StringComparison.Ordinal))
        {
            if (options.BillingDelay > TimeSpan.Zero)
            {
                journal.LogRequest(context.Request.Method, target, $"stalling the check2 reply {options.BillingDelay.TotalSeconds:0.0}s");
                await Task.Delay(options.BillingDelay, context.RequestAborted);
            }

            await WriteJsonAsync(context, exchange, "billing", PlatformReplies.BillingTarget());
            return;
        }

        if (HubMarkers.Any(marker => path.Contains(marker, StringComparison.Ordinal)))
        {
            await WriteJsonAsync(context, exchange, "hub", context.RequestServices.GetRequiredService<HubAccountService>().Handle(parameters));
            return;
        }

        await WriteJsonAsync(context, exchange, "fallback", PlatformReplies.Fallback());
    }

    private async Task HandleGameGatewayAsync(HttpContext context, byte[] body, TrafficExchange exchange)
    {
        exchange.Surface = "gateway";
        exchange.Gateway = context.RequestServices.GetRequiredService<GatewayTrace>();
        exchange.UserNumberHeader = context.Request.Headers["USERNO"].FirstOrDefault();
        if (options.GatewayDelay > TimeSpan.Zero)
        {
            await Task.Delay(options.GatewayDelay, context.RequestAborted);
        }

        GameGateway gateway = context.RequestServices.GetRequiredService<GameGateway>();
        byte[] reply = gateway.Handle(body, exchange.UserNumberHeader, context.Request.Headers.Host.FirstOrDefault());
        await WriteBodyAsync(context, reply, GatewayContentType);
    }

    /// <summary>A platform reply: recorded for the traffic log, then sent as JSON.</summary>
    private Task WriteJsonAsync(HttpContext context, TrafficExchange exchange, string surface, object reply)
    {
        exchange.Surface = surface;
        if (traffic.Enabled)
        {
            exchange.Reply = JsonSerializer.SerializeToNode(reply, reply.GetType(), WireJson.Options);
        }

        return WriteJsonAsync(context, reply);
    }

    /// <summary>The request's fields: a JSON object body, else a form body.</summary>
    private static JsonObject ParseFields(byte[] body)
    {
        if (body.Length == 0)
        {
            return [];
        }

        try
        {
            return WireJson.ParseObject(body) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return FormBody.Parse(body);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        return buffer.ToArray();
    }

    private static Task WriteJsonAsync(HttpContext context, object reply, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers.AccessControlAllowOrigin = "*";
        return WriteBodyAsync(context, WireJson.Serialize(reply), JsonContentType);
    }

    private static async Task WriteBodyAsync(HttpContext context, byte[] body, string contentType)
    {
        context.Response.ContentType = contentType;
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }
}
