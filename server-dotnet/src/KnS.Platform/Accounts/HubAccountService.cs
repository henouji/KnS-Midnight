using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Platform.Identifiers;
using KnS.Protocol;
using KnS.Protocol.Json;

namespace KnS.Platform.Accounts;

/// <summary>
/// Com2uS Hub 2.0 (guest create / auth / bind, acquire uid, login): issues a stable uid with a
/// login and session key per guest and remembers the guest account.
/// </summary>
public sealed class HubAccountService(PlatformAccountRepository accounts, PlatformIdentifiers identifiers)
{
    public HubReply Handle(RequestParameters parameters)
    {
        JsonNode? guest = parameters.IsSet("guest_uid") ? parameters["guest_uid"] : parameters["guestuid"];
        bool hasGuest = LenientJson.IsSet(guest);
        HubAccountRecord? existing = hasGuest ? accounts.FindHubAccount(LenientJson.ToDatabaseValue(guest)) : null;

        string uid, loginKey, sessionKey;
        if (existing is not null)
        {
            (uid, loginKey, sessionKey) = (existing.Uid, existing.LoginKey ?? "", existing.SessionKey ?? "");
        }
        else
        {
            uid = identifiers.NewHubUid();
            loginKey = "lk_" + uid;
            sessionKey = "sk_" + uid;
            accounts.Add(uid, hasGuest ? LenientJson.ToDatabaseValue(guest) : uid, loginKey, sessionKey,
                parameters.Contains("did") ? LenientJson.ToDatabaseValue(parameters["did"]) : "", identifiers.UnixTime);
        }

        JsonNode? guestUid = hasGuest ? guest!.DeepClone() : uid;
        return new HubReply
        {
            Result = new HubAccount
            {
                Uid = uid,
                GuestUid = guestUid,
                DeviceId = parameters.CopyOrDefault("did", uid),
                LoginKey = loginKey,
                SessionKey = sessionKey,
                IsNew = existing is null ? 1 : 0,
            },
            Uid = uid,
            LoginKey = loginKey,
            SessionKey = sessionKey,
            ServerTime = identifiers.UnixSeconds,
        };
    }
}

/// <summary>The Hub envelope: Peppermint uses error_code (0 = success); errno is there for the other readers.</summary>
public sealed record HubReply
{
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("error_detail")] public string ErrorDetail { get; init; } = "";
    [JsonPropertyName("errno")] public int ErrorNumber { get; init; }
    [JsonPropertyName("error")] public string Error { get; init; } = "";
    [JsonPropertyName("result")] public required HubAccount Result { get; init; }
    [JsonPropertyName("data")] public JsonObject Data { get; init; } = [];
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";
    [JsonPropertyName("user_id")] public string UserId => Uid;
    [JsonPropertyName("login_key")] public string LoginKey { get; init; } = "";
    [JsonPropertyName("session_key")] public string SessionKey { get; init; } = "";
    [JsonPropertyName("server_time")] public long ServerTime { get; init; }
}

public sealed record HubAccount
{
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";
    [JsonPropertyName("hub_uid")] public string HubUid => Uid;
    [JsonPropertyName("user_id")] public string UserId => Uid;
    [JsonPropertyName("guest_uid")] public JsonNode? GuestUid { get; init; }
    [JsonPropertyName("did")] public JsonNode? DeviceId { get; init; }
    [JsonPropertyName("login_key")] public string LoginKey { get; init; } = "";
    [JsonPropertyName("session_key")] public string SessionKey { get; init; } = "";
    [JsonPropertyName("secure_key")] public string SecureKey => SessionKey;
    [JsonPropertyName("register_type")] public string RegisterType { get; init; } = "guest";
    [JsonPropertyName("is_new")] public int IsNew { get; init; }
}
