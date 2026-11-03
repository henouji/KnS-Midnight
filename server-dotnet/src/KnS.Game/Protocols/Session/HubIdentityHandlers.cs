using System.Text.Json.Serialization;
using KnS.Game.Gateway;
using KnS.Game.Players;

namespace KnS.Game.Protocols.Session;

/// <summary>Protocol 52: echoes the player's Hub uid (case @0x15f986).</summary>
public sealed class HubUidHandler(PlayerDirectory directory) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request) =>
        ProtocolResult.Success(new HubUidResponse(directory.Resolve(request.Parameters).HubUid));
}

/// <summary>Protocol 53: echoes the player's Hub device and uid (case @0x15f942).</summary>
public sealed class HubDeviceHandler(PlayerDirectory directory) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        Player player = directory.Resolve(request.Parameters);
        return ProtocolResult.Success(new HubDeviceResponse(
            request.Parameters.GetText("hub_did", "guest"), player.HubUid, player.HubUid));
    }
}

public sealed record HubUidResponse([property: JsonPropertyName("hub_uid")] string? HubUid);

public sealed record HubDeviceResponse(
    [property: JsonPropertyName("hub_did")] string DeviceId,
    [property: JsonPropertyName("strHubGuestUid")] string? GuestUid,
    [property: JsonPropertyName("hub_uid")] string? HubUid);
