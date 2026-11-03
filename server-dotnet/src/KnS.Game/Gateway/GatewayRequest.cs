using KnS.Protocol;

namespace KnS.Game.Gateway;

/// <summary>A decrypted game gateway request.</summary>
/// <param name="Parameters">The request's "params" object.</param>
/// <param name="HeaderUserNumber">
/// The USERNO header: the client sends its user number with every request after login (login
/// itself sends none), which is what lets one server carry several players. 0 when absent.
/// </param>
/// <param name="Host">The Host header, for absolute download URLs.</param>
public sealed record GatewayRequest(RequestParameters Parameters, long HeaderUserNumber, string? Host);
