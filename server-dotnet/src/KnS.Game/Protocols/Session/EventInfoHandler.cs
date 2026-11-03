using System.Text.Json.Serialization;
using KnS.Game.Gateway;

namespace KnS.Game.Protocols.Session;

/// <summary>Protocol 59: running events (case @0x15b036). None; an empty list is safe.</summary>
public sealed class EventInfoHandler : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request) => ProtocolResult.Success(new EventInfoResponse());
}

public sealed record EventInfoResponse
{
    [JsonPropertyName("event_info")] public IReadOnlyList<object> Events { get; init; } = [];
}
