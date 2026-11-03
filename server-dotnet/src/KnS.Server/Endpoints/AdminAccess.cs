using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KnS.Protocol.Json;
using KnS.Server.Admin;
using KnS.Server.Configuration;

namespace KnS.Server.Endpoints;

/// <summary>The X-Admin-Key check and JSON replies shared by the /admin endpoints.</summary>
internal static class AdminAccess
{
    /// <summary>
    /// False (with the reply already written) unless the request carries the admin key. No key
    /// configured = no endpoint at all, so a public server does not advertise one.
    /// </summary>
    public static async Task<bool> AuthorizeAsync(HttpContext context, ServerOptions options)
    {
        if (string.IsNullOrEmpty(options.AdminKey))
        {
            await WriteAsync(context, StatusCodes.Status404NotFound, new JsonObject { ["error"] = "not found" });
            return false;
        }

        string? offered = context.Request.Headers["X-Admin-Key"].FirstOrDefault();
        // Compares hashes, so the check takes the same time whatever prefix matches.
        if (offered is null || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(offered)), SHA256.HashData(Encoding.UTF8.GetBytes(options.AdminKey))))
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, new JsonObject { ["error"] = "bad admin key" });
            return false;
        }

        return true;
    }

    public static async Task<JsonObject> ReadBodyAsync(HttpContext context)
    {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        try
        {
            return WireJson.ParseObject(buffer.ToArray()) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>Replies with <paramref name="onSuccess"/> when the action worked, else with its failure as {error}.</summary>
    public static Task WriteResultAsync(HttpContext context, AdminResult result, Func<JsonObject> onSuccess) =>
        result.Outcome == AdminOutcome.Ok
            ? WriteAsync(context, StatusCodes.Status200OK, onSuccess())
            : WriteAsync(context, result.Outcome switch
            {
                AdminOutcome.NotFound => StatusCodes.Status404NotFound,
                AdminOutcome.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            }, new JsonObject { ["error"] = result.Message });

    /// <summary>Unix seconds as "yyyy-MM-dd HH:mm:ssZ".</summary>
    public static string FormatTime(double unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds).ToString("u", System.Globalization.CultureInfo.InvariantCulture);

    public static async Task WriteAsync(HttpContext context, int status, JsonObject reply)
    {
        byte[] body = Encoding.UTF8.GetBytes(reply.ToJsonString());
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }
}
