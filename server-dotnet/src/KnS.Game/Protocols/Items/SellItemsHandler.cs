using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Items;

/// <summary>
/// Protocol 83: sells items (case @0x15ad7c, post-handler @0x1573a6) for master_item.sell_price,
/// the price the client shows. Worn and locked items are refused.
/// </summary>
public sealed class SellItemsHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    InventoryService inventory,
    ItemRepository items,
    MasterDataRepository masterData,
    ILogger<SellItemsHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        Dictionary<long, long> wornBy = inventory.GetWornItems(userNumber);
        HashSet<long> givenUpStarters = items.GetGivenUpStarterItems(userNumber);
        var sold = new List<long>();
        var failed = new List<long>();
        long earned = 0;

        foreach (long itemNumber in request.Parameters.GetList("sell_items").Select(LenientJson.TryToInt64).OfType<long>())
        {
            long? itemId = null;
            if (wornBy.ContainsKey(itemNumber))
            {
                // Worn items stay.
            }
            else if (StarterGear.FindByItemNumber(itemNumber) is { } starter)
            {
                if (givenUpStarters.Add(itemNumber))
                {
                    items.MarkStarterItemGivenUp(userNumber, itemNumber);
                    itemId = starter.ItemId;
                }
            }
            else if (items.FindOwned(userNumber, itemNumber) is { Locked: false } owned)
            {
                items.Delete(itemNumber);
                itemId = owned.ItemId;
            }

            if (itemId is not { } soldItemId)
            {
                failed.Add(itemNumber);
                continue;
            }

            earned += masterData.GetSellPrice(soldItemId) ?? 0;
            sold.Add(itemNumber);
        }

        profiles.AddCurrency(userNumber, gold: earned);
        logger.LogInformation("Sell: sold [{Sold}] for {Gold} G, failed [{Failed}], gold now {Balance}",
            string.Join(",", sold), earned, string.Join(",", failed), profiles.GetAccount(userNumber).Gold);
        return ProtocolResult.Success(new SellItemsResponse(profiles.GetGoods(userNumber), sold, failed));
    }
}

/// <param name="Sold">Each one is removed from the client's inventory; an unknown one raises an exception there.</param>
/// <param name="Failed">Parsed and otherwise ignored: the item stays.</param>
public sealed record SellItemsResponse(
    [property: JsonPropertyName("update_item_etc_info")] IReadOnlyList<GoodsEntry> Goods,
    [property: JsonPropertyName("success_list")] IReadOnlyList<long> Sold,
    [property: JsonPropertyName("fail_list")] IReadOnlyList<long> Failed);
