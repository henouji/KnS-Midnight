using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Platform.Identifiers;
using KnS.Protocol;
using KnS.Protocol.Json;

namespace KnS.Platform.Accounts;

/// <summary>
/// Com2uS Hive / Peppermint guest auth and device registration. The reply formats were captured
/// verbatim from the still-live real Hive server: error_code is a number and <c>type</c> echoes
/// the endpoint.
/// </summary>
public sealed class HiveGuestService(PlatformAccountRepository accounts, PlatformIdentifiers identifiers)
{
    /// <param name="path">The request path and query, as sent.</param>
    public object Handle(string path, RequestParameters parameters)
    {
        object guest = parameters.IsSet("guest_uid") ? LenientJson.ToDatabaseValue(parameters["guest_uid"])!
            : parameters.IsSet("did") ? LenientJson.ToDatabaseValue(parameters["did"])!
            : "dev";

        // Known quirk kept from the Python server: a new account is stored under a NEW guest uid,
        // so the lookup by the caller's own id only matches when the client sends that issued uid.
        string deviceId, guestUid;
        if (accounts.FindHiveAccount(guest) is { } existing)
        {
            (deviceId, guestUid) = (existing.Uid, existing.GuestUid ?? "");
        }
        else
        {
            guestUid = identifiers.NewHiveGuestUid();
            deviceId = identifiers.NewHiveDeviceId();
            accounts.Add(deviceId, guestUid, "", "", deviceId, identifiers.UnixTime);
        }

        string endpoint = path.ToLowerInvariant();
        if (endpoint.Contains("device/registered", StringComparison.Ordinal))
        {
            return new HiveDeviceRegisteredReply();
        }

        if (endpoint.Contains("guest/create", StringComparison.Ordinal))
        {
            return new HiveGuestReply { Type = "guest/create", DeviceId = deviceId, GuestUid = guestUid };
        }

        if (endpoint.Contains("guest/auth", StringComparison.Ordinal) || endpoint.EndsWith("/auth", StringComparison.Ordinal))
        {
            return new HiveAuthReply { DeviceId = deviceId };
        }

        if (endpoint.Contains("acquire_uid", StringComparison.Ordinal))
        {
            return new HiveAcquireUidReply { Uid = guestUid, DeviceId = deviceId };
        }

        if (endpoint.Contains("guest/bind", StringComparison.Ordinal))
        {
            return new HiveBindReply { DeviceId = deviceId };
        }

        return new HiveGuestReply { Type = endpoint.Trim('/'), DeviceId = deviceId, GuestUid = guestUid };
    }
}

public sealed record HiveDeviceRegisteredReply
{
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "device/registered";
    [JsonPropertyName("is_registered")] public bool IsRegistered { get; init; }
}

public sealed record HiveGuestReply
{
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("did")] public required string DeviceId { get; init; }
    [JsonPropertyName("guest_uid")] public required string GuestUid { get; init; }
}

public sealed record HiveAuthReply
{
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "guest/auth";
    [JsonPropertyName("did")] public required string DeviceId { get; init; }
    [JsonPropertyName("is_auth")] public bool IsAuthenticated { get; init; } = true;
}

public sealed record HiveAcquireUidReply
{
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "guest/acquire_uid";
    [JsonPropertyName("uid")] public required string Uid { get; init; }
    [JsonPropertyName("did")] public required string DeviceId { get; init; }
}

public sealed record HiveBindReply
{
    [JsonPropertyName("error_code")] public int ErrorCode { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "guest/bind";
    [JsonPropertyName("did")] public required string DeviceId { get; init; }
}
