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
/// Protocol 82: item Enhance, evolving it into master_item.next_item_id (parser case @0x15e108).
/// </summary>
/// <remarks>
/// Request {item_no, used_item_no_list (up to 4)}: a fully powered-up item (upgrade == c4) and the
/// material items its row lists (material1..6). The item keeps its item number and becomes the
/// next item at +0 with that item's base stats. Price = upgrade_cost (GetPriceUpgradeAndEvolve
/// mode 1).
/// </remarks>
public sealed class EnhanceItemHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    InventoryService inventory,
    ItemRepository items,
    HeroRepository heroes,
    MasterDataRepository masterData,
    ILogger<EnhanceItemHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long itemNumber = request.Parameters.GetInt64("item_no");
        List<long> materialNumbers = request.Parameters.GetList("used_item_no_list")
            .Select(LenientJson.TryToInt64)
            .OfType<long>()
            .ToList();
        Dictionary<long, long> wornBy = inventory.GetWornItems(userNumber);
        long gold = profiles.GetAccount(userNumber).Gold;

        ItemRecord? item = items.FindOwned(userNumber, itemNumber);
        ItemEnhanceRule? rule = item is null ? null : masterData.GetEnhanceRule(item.ItemId);
        // A material that is the item itself, worn, or not owned counts as no material (0).
        List<long> givenMaterialIds = materialNumbers
            .Select(number => number != itemNumber && !wornBy.ContainsKey(number)
                ? items.FindOwned(userNumber, number)?.ItemId ?? 0
                : 0)
            .ToList();

        string? refusal = null;
        if (rule is null || rule.NextItemId <= 0)
        {
            refusal = "invalid enhance";
        }
        else if (!rule.Materials.Order().SequenceEqual(givenMaterialIds.Order()))
        {
            refusal = "wrong materials";
        }
        else if (item!.Upgrade < rule.MaxUpgrade || gold < rule.Price)
        {
            refusal = "cannot enhance";
        }

        if (refusal is not null)
        {
            logger.LogInformation("Enhance refused: {Reason} (item_no {ItemNumber}, used {Materials})",
                refusal, itemNumber, string.Join(",", materialNumbers));
            return ProtocolResult.Refused(refusal);
        }

        long nextItemId = rule!.NextItemId;
        profiles.AddCurrency(userNumber, gold: -rule.Price);
        items.Evolve(itemNumber, nextItemId, inventory.GetNewItemStats(nextItemId));
        foreach (long materialNumber in materialNumbers)
        {
            items.Delete(materialNumber);
        }

        if (wornBy.ContainsKey(itemNumber))
        {
            heroes.SetCategoryOfItem(userNumber, itemNumber, EquipmentRanges.CategoryOf(nextItemId));
        }

        ItemRecord enhanced = items.FindOwned(userNumber, itemNumber)!;
        logger.LogInformation("Enhance: item_no {ItemNumber} {From} -> {To} ({Price} G), used {Materials}",
            itemNumber, item!.ItemId, nextItemId, rule.Price, string.Join(",", materialNumbers));
        return ProtocolResult.Success(new EnhanceItemResponse
        {
            Goods = profiles.GetGoods(userNumber),
            Item = inventory.ToItemInfo(enhanced, wornBy.GetValueOrDefault(itemNumber)),
            MaterialItemNumbers = materialNumbers,
        });
    }
}

public sealed record EnhanceItemResponse
{
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];
    [JsonPropertyName("item_info")] public required ItemInfo Item { get; init; }

    /// <summary>Each one is removed from the client's inventory (DeleteItemNode).</summary>
    [JsonPropertyName("used_item_no_list")] public IReadOnlyList<long> MaterialItemNumbers { get; init; } = [];

    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
}
