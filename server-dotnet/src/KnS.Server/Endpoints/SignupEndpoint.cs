using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KnS.Server.Admin;

namespace KnS.Server.Endpoints;

/// <summary>
/// The public sign-up page: <c>GET /signup</c> is the form, <c>POST /signup</c> {email, device_id}
/// stores a pending request the owner approves in /admin (the device id is optional: the original game never shows one,
/// so the owner matches the note's time against the refused devices). It never grants access by itself.
/// </summary>
/// <remarks>
/// Open to anyone, so it is bounded: a short fixed-window cap on requests, field length limits, and the
/// table keeps only the newest few hundred (TesterRepository). Replies are the same whether or not the
/// email was already known, so the form cannot be used to probe the allowlist.
/// </remarks>
public sealed partial class SignupEndpoint(TimeProvider clock)
{
    private const string RoutePath = "/signup";
    private const int MaxPerHour = 60;

    private readonly object _gate = new();
    private DateTimeOffset _windowStart;
    private int _inWindow;

    private static readonly string PagePath = System.IO.Path.Combine(AppContext.BaseDirectory, "web", "signup.html");

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.:\-]{4,64}$")]
    private static partial Regex DevicePattern();

    public static bool Matches(string method, string target) =>
        target.Split('?', 2)[0].TrimEnd('/') == RoutePath && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsPost(method));

    public async Task ServeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            if (!File.Exists(PagePath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "public, max-age=300";
            await context.Response.SendFileAsync(PagePath, context.RequestAborted);
            return;
        }

        if (context.Request.ContentLength is > 4096)
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, new JsonObject { ["error"] = "too large" });
            return;
        }

        if (!TakeSlot())
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status429TooManyRequests, new JsonObject { ["error"] = "too many requests, try again later" });
            return;
        }

        JsonObject body = await AdminAccess.ReadBodyAsync(context);
        string email = (body["email"]?.ToString() ?? "").Trim().ToLowerInvariant();
        string deviceId = (body["device_id"]?.ToString() ?? "").Trim();
        if (email.Length > 200 || !EmailPattern().IsMatch(email))
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "enter a valid email" });
            return;
        }

        if (deviceId.Length > 0 && !DevicePattern().IsMatch(deviceId))
        {
            await AdminAccess.WriteAsync(context, StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "enter your Device ID exactly as the game shows it" });
            return;
        }

        string note = (body["note"]?.ToString() ?? "").Trim();
        note = note.Length > 200 ? note[..200] : note;

        context.RequestServices.GetRequiredService<TesterAdminService>().RecordSignup(email, deviceId, note);
        await AdminAccess.WriteAsync(context, StatusCodes.Status200OK, new JsonObject { ["ok"] = true });
    }

    private bool TakeSlot()
    {
        lock (_gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (now - _windowStart > TimeSpan.FromHours(1))
            {
                _windowStart = now;
                _inWindow = 0;
            }

            return ++_inWindow <= MaxPerHour;
        }
    }
}
