using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Protocol;

namespace KnS.Game.Players;

/// <summary>Finds (or registers) the player a request belongs to.</summary>
public sealed class PlayerDirectory(PlayerRepository players, LastLoadedPlayer lastLoaded, TimeProvider clock)
{
    /// <summary>
    /// The player the request's own fields name: its user_no when that is a registered player,
    /// else the device (hub_did / did), registering a new player for an unknown device.
    /// </summary>
    /// <remarks>
    /// Post-login requests carry only user_no, so it is checked first. Without that the device
    /// fallback ("guest") picks a different row and protocol 60 reports a user_no that disagrees
    /// with the one login just issued: the client then loads a stranger.
    /// </remarks>
    public Player Resolve(RequestParameters parameters)
    {
        long wantedUserNumber = parameters.GetInt64OrFallback("user_no");
        if (wantedUserNumber != 0 && players.FindByUserNumber(wantedUserNumber) is { } known)
        {
            return Player.From(known);
        }

        string deviceId = FirstSetText(parameters, "guest", "hub_did", "did");
        string hubUid = FirstSetText(parameters, deviceId, "hub_uid", "hub_sessionkey");

        PlayerRecord? record = players.FindByLinkedDevice(deviceId) ?? players.FindByDevice(deviceId);
        if (record is not null)
        {
            return Player.From(record);
        }

        long createdAt = clock.GetUtcNow().ToUnixTimeMilliseconds();
        long userNumber = players.Create(deviceId, hubUid, createdAt / 1000.0);
        return Player.From(new PlayerRecord { UserNumber = userNumber, HubUid = hubUid, Nickname = "" });
    }

    /// <summary>
    /// The player a request belongs to when its body may not say: the USERNO header, else the
    /// player loaded last, else (after a restart, before anyone logs in again) the newest
    /// registered player, else <see cref="Resolve"/>.
    /// </summary>
    public Player ResolveCurrent(GatewayRequest request)
    {
        if (request.HeaderUserNumber != 0 && players.FindByUserNumber(request.HeaderUserNumber) is { } byHeader)
        {
            return Player.From(byHeader);
        }

        if (lastLoaded.Current is { } last)
        {
            return last;
        }

        if (!request.Parameters.IsSet("user_no") && players.FindNewest() is { } newest)
        {
            return Player.From(newest);
        }

        return Resolve(request.Parameters);
    }

    private static string FirstSetText(RequestParameters parameters, string fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (parameters.IsSet(name))
            {
                return parameters.GetText(name, fallback);
            }
        }

        return fallback;
    }
}
