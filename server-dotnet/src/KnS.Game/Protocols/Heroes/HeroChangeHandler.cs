using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Players;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Heroes;

/// <summary>
/// Protocol 202: plays an owned hero (case @0x15e65e). The choice is stored and sent back as
/// user_info.user_hero_pcid at the next login.
/// </summary>
public sealed class HeroChangeHandler(
    PlayerDirectory directory,
    InventoryService inventory,
    PlayerRepository players,
    ILogger<HeroChangeHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long heroNumber = request.Parameters.GetInt64("hero_no");
        // GetHeroes also creates the default hero's row, which may be missing right after a
        // restart (the client can switch back to it before reloading protocol 60).
        OwnedHeroRecord? hero = inventory.GetHeroes(userNumber).FirstOrDefault(owned => owned.HeroNumber == heroNumber);
        if (hero is null)
        {
            logger.LogInformation("Hero change: user {User} has no hero_no {Hero}", userNumber, heroNumber);
            return ProtocolResult.Refused("unknown hero");
        }

        players.SetActiveHero(userNumber, hero.HeroPcid);
        logger.LogInformation("Hero change: hero_no {Hero} pcid {Pcid}", heroNumber, hero.HeroPcid);
        return ProtocolResult.Success(new HeroChangeResponse(heroNumber, hero.HeroPcid));
    }
}

/// <param name="HeroPcid">Goes to Net_HeroChange; closing the Characters popup switches to it.</param>
public sealed record HeroChangeResponse(
    [property: JsonPropertyName("hero_no")] long HeroNumber,
    [property: JsonPropertyName("hero_pcid")] long HeroPcid);
