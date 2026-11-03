using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnS.Data;
using KnS.Game.Access;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Protocol;
using KnS.Protocol.Crypto;
using KnS.Protocol.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Gateway;

/// <summary>
/// The native game gateway (/1mhero/gateway.php): decrypts a request, runs its protocol's
/// handler in one transaction and encrypts the reply.
/// </summary>
/// <remarks>
/// Request JSON: <c>{"protocol": N, "params": {...}}</c>. The reply is FLAT:
/// <c>{"protocol": N, "errno": 0, "error": "", &lt;fields&gt;}</c>. JanssonParser reads protocol,
/// errno and every payload field straight off the root, so nesting the payload makes every field
/// read as zero. errno 0 is the success path; anything else opens the error popup. Every field
/// is optional to the client: a missing one reads as 0 / "" / empty.
/// </remarks>
public sealed class GameGateway(
    IServiceProvider services,
    DataSession session,
    PlayerDirectory directory,
    PlayerAllowlist allowlist,
    LastLoadedPlayer lastLoaded,
    GatewayTrace trace,
    ILogger<GameGateway> logger)
{
    private const int LoggedParameterLength = 2000;
    private const int LoggedReplyLength = 240;
    private const int MaxTransactionAttempts = 3;

    /// <param name="body">The raw request body (base64 ciphertext).</param>
    /// <param name="userNumberHeader">The USERNO header, if any.</param>
    /// <param name="host">The Host header, if any.</param>
    /// <returns>The encrypted reply body.</returns>
    public byte[] Handle(ReadOnlySpan<byte> body, string? userNumberHeader, string? host)
    {
        JsonObject? request = TryDecrypt(body);
        if (request is null)
        {
            trace.Reply = new JsonObject { ["protocol"] = 0, ["errno"] = -1, ["error"] = "decrypt failed" };
            return EncryptReply(trace.Reply);
        }

        JsonNode? protocolNode = request["protocol"];
        trace.Protocol = protocolNode?.DeepClone();
        trace.Request = (JsonObject)request.DeepClone();
        var parameters = new RequestParameters(request["params"] as JsonObject);
        var gatewayRequest = new GatewayRequest(parameters, ParseUserNumber(userNumberHeader), host);

        string protocolText = protocolNode?.ToJsonString() ?? "null";
        logger.LogInformation("protocol={Protocol} params={Params}", protocolText, Truncate(parameters.ToJsonString(), LoggedParameterLength));

        IProtocolHandler? handler = LenientJson.IsInteger(protocolNode, out long protocol) && protocol is >= int.MinValue and <= int.MaxValue
            ? services.GetKeyedService<IProtocolHandler>((int)protocol)
            : null;
        if (handler is null)
        {
            logger.LogInformation("No handler for protocol {Protocol}: replying bare errno=0", protocolText);
        }

        JsonObject reply = RunInTransaction(() =>
        {
            // A retried transaction runs the handler again: keep only the last run's findings.
            trace.Notes.Clear();
            trace.Warnings.Clear();
            if (allowlist.IsEnabled && !allowlist.Permits(LenientJson.IsInteger(protocolNode, out long listed) ? (int)listed : 0, gatewayRequest))
            {
                logger.LogWarning("Allowlist refused protocol {Protocol}: {Identity}. Attach the device to a tester to let them in.", protocolText, PlayerAllowlist.Describe(gatewayRequest));
                return BuildReply(protocolNode, ProtocolResult.Refused("not on the tester list"));
            }

            if (parameters.IsSet("user_no"))
            {
                lastLoaded.Set(directory.Resolve(parameters));
            }

            ProtocolResult result = handler?.Handle(gatewayRequest) ?? ProtocolResult.Success(EmptyObject.Instance);
            return BuildReply(protocolNode, result);
        });

        trace.Reply = reply;
        trace.UnreadParameters = parameters.UnreadNames;
        string replyText = reply.ToJsonString(WireJson.Options);
        logger.LogInformation("-> {Reply}", Truncate(replyText, LoggedReplyLength));
        return GameGatewayCipher.Encrypt(Encoding.UTF8.GetBytes(replyText));
    }

    /// <summary>
    /// Runs the request as one transaction. One that collides with another player's (deadlock,
    /// lock timeout) is rolled back and run again; the client's post-login burst sends several
    /// requests at once.
    /// </summary>
    private T RunInTransaction<T>(Func<T> work)
    {
        for (int attempt = 1; ; attempt++)
        {
            session.BeginTransaction();
            try
            {
                T result = work();
                session.Commit();
                return result;
            }
            catch (Exception e) when (attempt < MaxTransactionAttempts && session.Dialect.IsTransientConflict(e))
            {
                session.Rollback();
                logger.LogWarning("Transaction conflict ({Error}); retrying, attempt {Attempt}", e.Message, attempt + 1);
            }
        }
    }

    /// <summary>The flat envelope with the payload's fields (or the refusal) on the root.</summary>
    private static JsonObject BuildReply(JsonNode? protocolNode, ProtocolResult result)
    {
        var reply = new JsonObject
        {
            ["protocol"] = protocolNode?.DeepClone(),
            ["errno"] = result.IsRefused ? 1 : 0,
            ["error"] = result.Refusal ?? "",
        };
        if (result.Payload is { } payload)
        {
            JsonObject fields = WireJson.ToJsonObject(payload);
            foreach (string name in fields.Select(field => field.Key).ToList())
            {
                JsonNode? value = fields[name];
                fields.Remove(name);
                reply[name] = value;
            }
        }

        return reply;
    }

    private JsonObject? TryDecrypt(ReadOnlySpan<byte> body)
    {
        try
        {
            return WireJson.ParseObject(GameGatewayCipher.Decrypt(body))
                ?? throw new JsonException("The request is not a JSON object.");
        }
        catch (Exception e) when (e is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            logger.LogWarning("Decrypt failed: {Error}", e.Message);
            return null;
        }
    }

    private static byte[] EncryptReply(JsonObject reply) =>
        GameGatewayCipher.Encrypt(Encoding.UTF8.GetBytes(reply.ToJsonString(WireJson.Options)));

    private static long ParseUserNumber(string? header) =>
        long.TryParse(header?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) ? number : 0;

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
