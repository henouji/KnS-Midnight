using System.Text.Json.Nodes;

namespace KnS.Game.Gateway;

/// <summary>
/// What one game gateway request looked like in plain text, for the traffic log: the decrypted
/// request and reply, the parameters no handler read, and the notes and warnings a handler adds
/// (the stage result's drop audit). One per request (scoped); empty until the gateway runs.
/// </summary>
public sealed class GatewayTrace
{
    /// <summary>The request's protocol field as sent.</summary>
    public JsonNode? Protocol { get; set; }

    /// <summary>The decrypted request, null when it did not decrypt.</summary>
    public JsonObject? Request { get; set; }

    /// <summary>The reply before encryption.</summary>
    public JsonObject? Reply { get; set; }

    /// <summary>Parameters the client sent that the server never looked at.</summary>
    public IReadOnlyList<string> UnreadParameters { get; set; } = [];

    /// <summary>Handler findings by name, e.g. "drops".</summary>
    public JsonObject Notes { get; } = [];

    /// <summary>Findings worth a line in the console, e.g. a drop the client reported but the server never offered.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>The protocol's name when it is a known protocol number.</summary>
    public string? ProtocolName =>
        Protocol is JsonValue value && value.TryGetValue(out int number) && Enum.IsDefined(typeof(GameProtocol), number)
            ? ((GameProtocol)number).ToString()
            : null;
}
