using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnS.Platform.Identifiers;
using KnS.Protocol;
using KnS.Protocol.Json;

namespace KnS.Platform.Services;

/// <summary>The fixed replies of the platform surfaces that keep no state.</summary>
public sealed class PlatformReplies(PlatformIdentifiers identifiers)
{
    /// <summary>
    /// The plain (unencrypted) ActiveUser surface: DID issue, version check, terms. Says the app is
    /// up to date and the terms are agreed.
    /// </summary>
    public ActiveUserStatusReply ActiveUserStatus(RequestParameters parameters) => new()
    {
        DeviceId = parameters.IsSet("did") ? parameters["did"]!.DeepClone() : "MOCK-" + identifiers.NewHubUid(),
        ServerTime = identifiers.UnixSeconds,
    };

    /// <summary>
    /// Com2uS Mercury (notice / messenger CMS). The live Mercury service is what pushes the
    /// "service discontinued" popup; an empty notice set suppresses it.
    /// </summary>
    public MercuryReply Mercury() => new() { ServerTime = identifiers.UnixSeconds };

    /// <summary>The Com2uS offerwall (ads): empty.</summary>
    public static OfferwallReply Offerwall() => new();

    /// <summary>
    /// InApp billing target selection (check2.c2s): the client reads <c>type</c> (1 = Google
    /// Play) in SelectTarget$1.
    /// </summary>
    public static BillingTargetReply BillingTarget() => new();

    /// <summary>Any other path: a benign envelope covering both protocol families.</summary>
    public static FallbackReply Fallback() => new();
}

public sealed record ActiveUserStatusReply
{
    [JsonPropertyName("errno")] public int ErrorNumber { get; init; }
    [JsonPropertyName("error")] public string Error { get; init; } = "";
    [JsonPropertyName("did")] public JsonNode? DeviceId { get; init; }
    [JsonPropertyName("result")] public int Result { get; init; } = 1;

    /// <summary>App-version check: up to date.</summary>
    [JsonPropertyName("is_latest")] public int IsLatest { get; init; } = 1;

    /// <summary>0 = no forced update.</summary>
    [JsonPropertyName("update_type")] public int UpdateType { get; init; }

    /// <summary>Terms already agreed.</summary>
    [JsonPropertyName("useragree")] public int TermsAgreed { get; init; } = 1;

    [JsonPropertyName("server_status")] public string ServerStatus { get; init; } = "normal";
    [JsonPropertyName("server_time")] public long ServerTime { get; init; }
}

public sealed record MercuryReply
{
    [JsonPropertyName("errno")] public int ErrorNumber { get; init; }
    [JsonPropertyName("error")] public string Error { get; init; } = "";
    [JsonPropertyName("result")] public int Result { get; init; }
    [JsonPropertyName("resultCode")] public int ResultCode { get; init; }
    [JsonPropertyName("notices")] public IReadOnlyList<object> Notices { get; init; } = [];
    [JsonPropertyName("messages")] public IReadOnlyList<object> Messages { get; init; } = [];
    [JsonPropertyName("events")] public IReadOnlyList<object> Events { get; init; } = [];
    [JsonPropertyName("banners")] public IReadOnlyList<object> Banners { get; init; } = [];
    [JsonPropertyName("list")] public IReadOnlyList<object> List { get; init; } = [];
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("server_time")] public long ServerTime { get; init; }
}

public sealed record OfferwallReply
{
    [JsonPropertyName("errno")] public int ErrorNumber { get; init; }
    [JsonPropertyName("error")] public string Error { get; init; } = "";
    [JsonPropertyName("result")] public int Result { get; init; }
    [JsonPropertyName("list")] public IReadOnlyList<object> List { get; init; } = [];
    [JsonPropertyName("reward")] public int Reward { get; init; }
}

public sealed record BillingTargetReply
{
    [JsonPropertyName("type")] public int Type { get; init; } = 1;
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("google_service")] public int GoogleService { get; init; } = 1;
    [JsonPropertyName("last_iap")] public int LastIap { get; init; } = 1;
    [JsonPropertyName("state")] public int State { get; init; }
}

public sealed record FallbackReply
{
    [JsonPropertyName("errno")] public int ErrorNumber { get; init; }
    [JsonPropertyName("error")] public string Error { get; init; } = "";
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("error_detail")] public string ErrorDetail { get; init; } = "";
    [JsonPropertyName("result")] public JsonObject Result { get; init; } = [];
    [JsonPropertyName("data")] public JsonObject Data { get; init; } = [];
    [JsonPropertyName("Result")] public JsonObject GatewayResult { get; init; } = [];
}
