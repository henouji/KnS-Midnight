using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Heroes;

/// <summary>
/// Protocol 201: unlocks a hero on the Characters screen (case @0x15a8c0).
/// </summary>
/// <remarks>
/// The client has checked the gem price (master_hero.price_gems) already. Its post-handler adds
/// the reply's hero_info (inserting hero_item_infos into the inventory), and closing the popup
/// switches to that hero: a reply without hero_info switches to pcid 0, which has no master row,
/// and crashes. The new hero gets its master default gear; every equipped range must resolve to
/// an inventory item or the result screen crashes.
/// </remarks>
public sealed class HeroBuyHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    InventoryService inventory,
    HeroRepository heroes,
    ItemRepository items,
    PlayerRepository players,
    MasterDataRepository masterData,
    ILogger<HeroBuyHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        Player player = directory.ResolveCurrent(request);
        long userNumber = player.UserNumber;
        long heroPcid = request.Parameters.GetInt64("hero_pcid");
        HeroCatalogEntry? hero = masterData.GetHero(heroPcid);
        long? ownedHeroNumber = heroes.FindHeroNumber(userNumber, heroPcid);
        if (hero is null)
        {
            logger.LogInformation("Hero buy: unknown hero_pcid {Pcid}", heroPcid);
            return ProtocolResult.Refused("unknown hero");
        }

        if (ownedHeroNumber is null)
        {
            if (profiles.GetAccount(userNumber).Gems < hero.PriceGems)
            {
                logger.LogInformation("Hero buy: pcid {Pcid} costs {Price} gems, not enough", heroPcid, hero.PriceGems);
                return ProtocolResult.Refused("not enough gems");
            }

            // The new hero's number is its pcid.
            heroes.Add(userNumber, heroPcid, heroPcid);
            foreach (long itemId in hero.DefaultGear)
            {
                long itemNumber = items.Add(userNumber, itemId, claimed: true, inventory.GetNewItemStats(itemId));
                heroes.Equip(userNumber, heroPcid, EquipmentRanges.CategoryOf(itemId), itemNumber);
            }

            profiles.AddCurrency(userNumber, gems: -hero.PriceGems);
            players.SetActiveHero(userNumber, heroPcid);
            logger.LogInformation("Hero buy: pcid {Pcid} for {Price} gems, gear [{Gear}]",
                heroPcid, hero.PriceGems, string.Join(",", hero.DefaultGear));
        }

        long heroNumber = ownedHeroNumber ?? heroPcid;
        return ProtocolResult.Success(new HeroBuyResponse
        {
            UserInfo = profiles.BuildUserInfo(player),
            Goods = profiles.GetGoods(userNumber),
            Hero = inventory.BuildHeroInfo(userNumber, heroNumber, heroPcid),
        });
    }
}

public sealed record HeroBuyResponse
{
    [JsonPropertyName("user_info")] public required UserInfo UserInfo { get; init; }
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];
    [JsonPropertyName("hero_info")] public required HeroInfo Hero { get; init; }
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
}
