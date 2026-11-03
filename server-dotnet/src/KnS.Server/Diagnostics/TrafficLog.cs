using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnS.Game.Gateway;
using KnS.Server.Configuration;

namespace KnS.Server.Diagnostics;

/// <summary>One request and its reply as the traffic log records it; filled in while the request is routed.</summary>
public sealed class TrafficExchange(string method, string target, JsonObject fields)
{
    public long Started { get; } = Stopwatch.GetTimestamp();
    public string Method { get; } = method;
    public string Target { get; } = target;

    /// <summary>Which surface answered: gateway, activeuser, hive, hub, billing, ...</summary>
    public string Surface { get; set; } = "fallback";

    /// <summary>The request's fields (form or JSON body); the gateway's are replaced by the decrypted request.</summary>
    public JsonObject Fields { get; } = fields;

    public JsonNode? Reply { get; set; }

    /// <summary>The USERNO header: the player of every post-login gateway request (71 / 72 carry no user_no).</summary>
    public string? UserNumberHeader { get; set; }

    /// <summary>The game gateway's plain-text view, for gateway requests.</summary>
    public GatewayTrace? Gateway { get; set; }

    /// <summary>The exception that ended the request, if one did.</summary>
    public string? Failure { get; set; }
}

/// <summary>
/// Every request with its reply in plain text, one JSON line each, in
/// &lt;log dir&gt;/traffic/&lt;yyyy-MM-dd&gt;.jsonl. Game gateway lines carry the decrypted request
/// and reply, the protocol's name, the parameters the server ignored and the handler's notes
/// (the stage drop audit); findings that look like lost drops also go to the console as warnings.
/// Read the files with tools/traffic.py. Files older than the retention are deleted.
/// </summary>
public sealed class TrafficLog(ServerOptions options, TimeProvider clock, ILogger<TrafficLog> logger)
{
    private static readonly JsonSerializerOptions LineJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _fileLock = new();
    private long _sequence;
    private string? _currentDay;

    public bool Enabled => options.TrafficLog;

    public void Write(TrafficExchange exchange, int statusCode)
    {
        if (!Enabled)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref _sequence);
        DateTimeOffset now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone);
        GatewayTrace? gateway = exchange.Gateway;

        var line = new JsonObject
        {
            ["seq"] = sequence,
            ["time"] = now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            ["ms"] = Math.Round(Stopwatch.GetElapsedTime(exchange.Started).TotalMilliseconds, 1),
            ["surface"] = exchange.Surface,
            ["method"] = exchange.Method,
            ["path"] = exchange.Target,
            ["status"] = exchange.Failure is null ? statusCode : StatusCodes.Status500InternalServerError,
        };
        if (exchange.Failure is { } failure)
        {
            line["failure"] = failure;
        }

        JsonNode? request = exchange.Fields;
        if (gateway is not null)
        {
            request = gateway.Request;
            line["user_no"] = FindUserNumber(exchange.UserNumberHeader, gateway);
            line["protocol"] = gateway.Protocol?.DeepClone();
            line["name"] = gateway.ProtocolName;
            line["errno"] = gateway.Reply?["errno"]?.DeepClone();
            if (gateway.Reply?["error"] is JsonValue error && error.ToString() is { Length: > 0 } text)
            {
                line["error"] = text;
            }

            if (gateway.Warnings.Count > 0)
            {
                line["warnings"] = new JsonArray([.. gateway.Warnings.Select(warning => (JsonNode)warning)]);
            }

            if (gateway.UnreadParameters.Count > 0)
            {
                line["unread"] = new JsonArray([.. gateway.UnreadParameters.Select(name => (JsonNode)name)]);
            }

            if (gateway.Notes.Count > 0)
            {
                line["notes"] = gateway.Notes.DeepClone();
            }
        }

        line["request"] = request?.DeepClone();
        line["reply"] = (gateway?.Reply ?? exchange.Reply)?.DeepClone();

        foreach (string warning in gateway?.Warnings ?? [])
        {
            logger.LogWarning("#{Sequence} {Protocol} user {User}: {Warning}", sequence, gateway!.ProtocolName, line["user_no"], warning);
        }

        Append(now, line.ToJsonString(LineJson));
    }

    /// <summary>The USERNO header, else params.user_no, else the reply's user_no (login sends none).</summary>
    private static JsonNode? FindUserNumber(string? header, GatewayTrace gateway) =>
        long.TryParse(header?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) && number != 0
            ? (JsonNode)number
            : (gateway.Request?["params"] as JsonObject)?["user_no"]?.DeepClone() ?? gateway.Reply?["user_no"]?.DeepClone();

    private void Append(DateTimeOffset now, string line)
    {
        string day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string folder = Path.Combine(options.LogDirectory, "traffic");
        lock (_fileLock)
        {
            if (day != _currentDay)
            {
                Directory.CreateDirectory(folder);
                DeleteExpired(folder, now);
                _currentDay = day;
            }

            File.AppendAllText(Path.Combine(folder, day + ".jsonl"), line + "\n", Utf8NoBom);
        }
    }

    private void DeleteExpired(string folder, DateTimeOffset now)
    {
        string oldestKept = now.AddDays(1 - options.TrafficRetentionDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (string file in Directory.EnumerateFiles(folder, "????-??-??.jsonl"))
        {
            if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(file), oldestKept) < 0)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException e)
                {
                    logger.LogWarning("Could not delete the old traffic log {File}: {Error}", file, e.Message);
                }
            }
        }
    }
}
