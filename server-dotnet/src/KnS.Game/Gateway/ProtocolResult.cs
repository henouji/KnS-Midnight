namespace KnS.Game.Gateway;

/// <summary>
/// What a protocol handler answers: the reply's fields, or a refusal. A refusal is sent as
/// errno=1, which routes the client into its error popup.
/// </summary>
public sealed class ProtocolResult
{
    private ProtocolResult(object? payload, string? refusal)
    {
        Payload = payload;
        Refusal = refusal;
    }

    /// <summary>The reply's fields, serialized flat onto the envelope's root.</summary>
    public object? Payload { get; }

    public string? Refusal { get; }

    public bool IsRefused => Refusal is not null;

    public static ProtocolResult Success(object payload) => new(payload, null);

    public static ProtocolResult Refused(string reason) => new(null, reason);
}
