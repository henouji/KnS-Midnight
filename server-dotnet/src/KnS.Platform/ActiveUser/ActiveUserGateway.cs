using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnS.Protocol.Crypto;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Platform.ActiveUser;

/// <summary>An encrypted ActiveUser reply and the headers that go with it.</summary>
/// <param name="Body">base64 ciphertext.</param>
/// <param name="Timestamp">REQ-TIMESTAMP: the request's timestamp, which keys the reply too.</param>
/// <param name="AuthKey">REQ-AUTHKEY: md5 of the reply's plaintext.</param>
public sealed record ActiveUserReply(byte[] Body, string Timestamp, string AuthKey);

/// <summary>
/// The Com2uS ActiveUser module's encrypted gateway.php (DID issue, config, downloads, terms).
/// See <see cref="ActiveUserCipher"/> for the protocol.
/// </summary>
public sealed class ActiveUserGateway(TimeProvider clock, ILogger<ActiveUserGateway> logger)
{
    private const int LoggedReplyLength = 120;

    public ActiveUserReply Handle(ReadOnlySpan<byte> body, string? timestampHeader)
    {
        string timestamp = string.IsNullOrEmpty(timestampHeader)
            ? clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : timestampHeader;

        JsonObject request = TryDecrypt(timestamp, body) ?? [];
        JsonObject reply = BuildReply(request);
        string replyText = reply.ToJsonString(WireJson.Options);
        logger.LogInformation("api={Api} -> {Reply}",
            request["api"]?.ToJsonString() ?? "?", replyText.Length <= LoggedReplyLength ? replyText : replyText[..LoggedReplyLength]);

        byte[] plaintext = Encoding.UTF8.GetBytes(replyText);
        return new ActiveUserReply(ActiveUserCipher.Encrypt(timestamp, plaintext), timestamp, ActiveUserCipher.ComputeAuthKey(plaintext));
    }

    /// <summary>
    /// The reply per api. Shapes are best effort from the response keys the module reads; unknown
    /// apis get the plain success envelope.
    /// </summary>
    private static JsonObject BuildReply(JsonObject request)
    {
        JsonNode? api = request["api"];
        var reply = new JsonObject
        {
            ["errno"] = 0,
            ["error"] = "",
            ["api"] = request.ContainsKey("api") ? api?.DeepClone() : "",
            ["result"] = 1,
        };

        switch (api is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null)
        {
            case "get_did":
                string deviceId = DeviceIdFor(request);
                reply["did"] = deviceId;
                reply["did_data"] = deviceId;
                reply["update_did"] = 0;
                break;
            case "get_config":
                reply["session_max_time"] = 86400;
                reply["session_max_num"] = 100;
                reply["send_referrer"] = 0;
                reply["session_log"] = 0;
                reply["notice"] = "";
                reply["config"] = new JsonObject();
                break;
            case "download":
                // No extra files: up to date.
                reply["download"] = "N";
                reply["url"] = "";
                reply["version"] = "";
                reply["file"] = "";
                reply["filelist"] = new JsonArray();
                break;
            case "agreement" or "get_agreement":
                reply["agreement"] = 1;
                reply["agreement_version"] = 0;
                reply["show"] = 0;
                break;
        }

        return reply;
    }

    /// <summary>A stable 32-hex DID per device.</summary>
    private static string DeviceIdFor(JsonObject request)
    {
        JsonNode? device = LenientJson.IsSet(request["android_id"]) ? request["android_id"]
            : LenientJson.IsSet(request["advertising_id"]) ? request["advertising_id"]
            : null;
        string seed = (device is null ? "guest" : LenientJson.ToText(device)) + "|kns";
#pragma warning disable CA5351 // An id derivation the client already relies on, not a security measure.
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
#pragma warning restore CA5351
    }

    private JsonObject? TryDecrypt(string timestamp, ReadOnlySpan<byte> body)
    {
        try
        {
            return WireJson.ParseObject(ActiveUserCipher.Decrypt(timestamp, body));
        }
        catch (Exception e) when (e is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            // Still answer: the client only needs the success envelope.
            logger.LogWarning("Decrypt failed: {Error}", e.Message);
            return null;
        }
    }
}
