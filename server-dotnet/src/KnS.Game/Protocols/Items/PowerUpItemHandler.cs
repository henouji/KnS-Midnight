using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Items;

/// <summary>
/// Protocol 81: item Power up, +1 upgrade level (parser case @0x15e072).
/// </summary>
/// <remarks>
/// Request {item_no, used_item_no}: the item and a second copy of it (the material picker lists
/// only the same item), which is used up. Rules from the item's master row: price = c5 +
/// upgrade * c6 (GetPriceUpgradeAndEvolve@0xb9f90 mode 0), each level adds attack_up /
/// defence_up / hp_up, and c4 is the level cap. Starter gear has no item row, so it can only be
/// the material.
/// </remarks>
public sealed class PowerUpItemHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    InventoryService inventory,
    ItemRepository items,
    MasterDataRepository masterData,
    ILogger<PowerUpItemHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long itemNumber = request.Parameters.GetInt64("item_no");
        long materialNumber = request.Parameters.GetInt64("used_item_no");
        Dictionary<long, long> wornBy = inventory.GetWornItems(userNumber);
        long gold = profiles.GetAccount(userNumber).Gold;

        ItemRecord? item = items.FindOwned(userNumber, itemNumber);
        ItemPowerUpRule? rule = item is null ? null : masterData.GetPowerUpRule(item.ItemId);
        long? materialItemId = StarterGear.FindByItemNumber(materialNumber) is { } starter
            ? (inventory.GetGivenUpStarterItems(userNumber).Contains(materialNumber) ? null : starter.ItemId)
            : items.FindOwned(userNumber, materialNumber)?.ItemId;

        string? refusal = null;
        if (item is null || rule is null || materialNumber == itemNumber || wornBy.ContainsKey(materialNumber)
            || materialItemId != item.ItemId)
        {
            refusal = "invalid power up";
        }
        else if (item.Upgrade >= rule.MaxUpgrade || gold < rule.BasePrice + item.Upgrade * rule.PricePerUpgrade)
        {
            refusal = "cannot power up";
        }

        if (refusal is not null)
        {
            logger.LogInformation("Power up refused: {Reason} (item_no {ItemNumber}, used {Material})", refusal, itemNumber, materialNumber);
            return ProtocolResult.Refused(refusal);
        }

        long price = rule!.BasePrice + item!.Upgrade * rule.PricePerUpgrade;
        profiles.AddCurrency(userNumber, gold: -price);
        items.PowerUp(itemNumber, new ItemStats(Math.Max(0, rule.AttackGain), Math.Max(0, rule.DefenceGain), Math.Max(0, rule.HpGain)));
        if (StarterGear.FindByItemNumber(materialNumber) is not null)
        {
            items.MarkStarterItemGivenUp(userNumber, materialNumber);
        }
        else
        {
            items.Delete(materialNumber);
        }

        ItemRecord upgraded = items.FindOwned(userNumber, itemNumber)!;
        logger.LogInformation("Power up: item_no {ItemNumber} -> +{Upgrade} ({Price} G), used {Material}",
            itemNumber, upgraded.Upgrade, price, materialNumber);
        return ProtocolResult.Success(new PowerUpItemResponse
        {
            Goods = profiles.GetGoods(userNumber),
            Item = inventory.ToItemInfo(upgraded, wornBy.GetValueOrDefault(itemNumber)),
            MaterialItemNumber = materialNumber,
        });
    }
}

public sealed record PowerUpItemResponse
{
    /// <summary>The wallet after paying.</summary>
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];

    /// <summary>The item after the upgrade.</summary>
    [JsonPropertyName("item_info")] public required ItemInfo Item { get; init; }

    [JsonPropertyName("used_item_no")] public long MaterialItemNumber { get; init; }
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
}
