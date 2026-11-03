using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Items;

/// <summary>
/// Protocol 106: buys item bag slots (case @0x15d85c; master_inventory_expansion, 60 levels).
/// </summary>
public sealed class ExpandItemBagHandler(BagExpander expander) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request) => expander.Expand(request, BagKind.Items);
}

/// <summary>
/// Protocol 301: buys ally bag slots, the "+" of the Ally screen (case @0x160108;
/// master_fellow_inventory_expansion, 90 levels). Same reply as 106.
/// </summary>
public sealed class ExpandAllyBagHandler(BagExpander expander) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request) => expander.Expand(request, BagKind.Allies);
}

/// <summary>
/// The shared body of the bag expansions. Request {expansion_level}: the client picks the
/// expansion row for its current bought slots and has already checked the price.
/// </summary>
public sealed class BagExpander(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    PlayerRepository players,
    MasterDataRepository masterData,
    ILogger<BagExpander> logger)
{
    public ProtocolResult Expand(GatewayRequest request, BagKind bag)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long expansionLevel = request.Parameters.GetInt64("expansion_level");
        PlayerAccount account = profiles.GetAccount(userNumber);
        long alreadyBought = account.PurchasedSlots(bag);
        BagExpansion? expansion = masterData.GetBagExpansion(bag, expansionLevel);

        if (expansion is null
            || alreadyBought != expansion.TotalSlots - expansion.AddedSlots
            || account.BalanceOf(expansion.PriceItemId) < expansion.Price)
        {
            logger.LogInformation("{Bag} bag expand: level {Level} refused (bought {Bought}, row {Row})",
                bag, expansionLevel, alreadyBought, expansion);
            return ProtocolResult.Refused("invalid expansion");
        }

        profiles.Pay(userNumber, new Price { PriceItemId = expansion.PriceItemId, Amount = expansion.Price });
        players.SetPurchasedSlots(userNumber, bag, expansion.TotalSlots);
        logger.LogInformation("{Bag} bag expand: level {Level} -> {Total} bought slots, paid {Price} of {PriceItem}",
            bag, expansionLevel, expansion.TotalSlots, expansion.Price, expansion.PriceItemId);

        Player player = directory.ResolveCurrent(request);
        return ProtocolResult.Success(new BagExpansionResponse(profiles.BuildUserInfo(player), profiles.GetGoods(userNumber)));
    }
}

/// <param name="UserInfo">Carries the new bought slots (UpdateUserInfo).</param>
/// <param name="Goods">The wallet after paying.</param>
public sealed record BagExpansionResponse(
    [property: JsonPropertyName("user_info")] UserInfo UserInfo,
    [property: JsonPropertyName("update_item_etc_info")] IReadOnlyList<GoodsEntry> Goods)
{
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
}
