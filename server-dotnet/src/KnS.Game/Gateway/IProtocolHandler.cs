namespace KnS.Game.Gateway;

/// <summary>Answers one <see cref="GameProtocol"/>. Registered as a keyed service under its protocol number.</summary>
public interface IProtocolHandler
{
    /// <summary>
    /// Runs inside the request's transaction (committed after the reply is built, rolled back on
    /// an exception).
    /// </summary>
    ProtocolResult Handle(GatewayRequest request);
}
