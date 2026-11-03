using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnS.Server.Configuration;

namespace KnS.Server.Diagnostics;

/// <summary>
/// Records requests for protocol reverse engineering: a line per request in work/server.log and
/// the full request (headers, body as hex and text) in work/requests.jsonl. Off when quiet.
/// </summary>
public sealed class RequestJournal(ServerOptions options, TimeProvider clock, ILogger<RequestJournal> logger)
{
    private static readonly JsonSerializerOptions CaptureJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly object _fileLock = new();

    public void LogRequest(string method, string target, string detail)
    {
        logger.LogInformation("{Method} {Target} {Detail}", method, target, detail);
        if (!options.Quiet)
        {
            Append("server.log", $"{TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone):HH:mm:ss} {method} {target} {detail}");
        }
    }

    public void Capture(HttpRequest request, string target, byte[] body, JsonObject parameters)
    {
        if (options.Quiet)
        {
            return;
        }

        var headers = new JsonObject();
        foreach ((string name, Microsoft.Extensions.Primitives.StringValues values) in request.Headers)
        {
            headers[name] = values.ToString();
        }

        var record = new JsonObject
        {
            ["ts"] = clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0,
            ["method"] = request.Method,
            ["path"] = target,
            ["headers"] = headers,
            ["len"] = body.Length,
            ["body_hex"] = Convert.ToHexString(body).ToLowerInvariant(),
        };
        if (TryDecodeText(body) is { } text)
        {
            record["body_text"] = text;
        }

        if (parameters.Count > 0)
        {
            record["params"] = parameters.DeepClone();
        }

        Append("requests.jsonl", record.ToJsonString(CaptureJson));
    }

    /// <summary>The body as text when it is UTF-8 and looks like text (printable, or form / JSON punctuation).</summary>
    private static string? TryDecodeText(byte[] body)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return text.All(IsPrintable) || text.IndexOfAny(['\n', '{', '[', '&', '=']) >= 0 ? text : null;
    }

    /// <summary>Python's str.isprintable: no control, format, surrogate, private-use or separator characters except the space.</summary>
    private static bool IsPrintable(char c) => c == ' ' || CharUnicodeInfo.GetUnicodeCategory(c) switch
    {
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator => false,
        _ => true,
    };

    private void Append(string fileName, string line)
    {
        lock (_fileLock)
        {
            Directory.CreateDirectory(options.LogDirectory);
            File.AppendAllText(Path.Combine(options.LogDirectory, fileName), line + "\n", Encoding.UTF8);
        }
    }
}
