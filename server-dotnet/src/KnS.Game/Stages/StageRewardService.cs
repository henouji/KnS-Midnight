using System.Text.Json.Nodes;
using KnS.Data.Repositories;
using KnS.Game.Fellows;
using KnS.Game.Inventory;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Randomness;
using KnS.Game.Rules;
using KnS.Protocol.Json;

namespace KnS.Game.Stages;

/// <summary>The drops of a stage start (71) and claiming them in the stage result (72).</summary>
/// <remarks>
/// A stage start rolls the stage's reward slots once and keeps the hits (stage_roll). Every kind
/// of drop comes from that one roll: equipment and materials become offered items, Gold Boxes
/// and counted etc items become event offers, and fellows are granted when the stage is cleared.
/// </remarks>
public sealed class StageRewardService(
    StageRepository stages,
    ItemRepository items,
    PlayerRepository players,
    GoodsRepository goods,
    MasterDataRepository masterData,
    PlayerProfileService profiles,
    InventoryService inventory,
    HeroGearRules heroGear,
    ItemOptionRoller optionRoller,
    FellowService fellows,
    IRandomSource random,
    GameOptions options)
{
    /// <summary>
    /// Rolls the stage and offers its equipment and material drops (fixed slots, then the area
    /// boss's random item) plus Gold Boxes: drop_item_list. Replaces the previous start's offers.
    /// </summary>
    public DropItemList OfferDrops(long userNumber, long stageId, long stageMode)
    {
        items.DeleteUnclaimed(userNumber);
        stages.ClearEventOffers(userNumber);
        HashSet<long> equipment = masterData.GetEquipmentItemIds();
        HashSet<long> materials = masterData.GetMaterialItemIds();
        IReadOnlyList<long> series = heroGear.GetSeries(profiles.GetAccount(userNumber).HeroPcid);

        RollStage(userNumber, stageId);
        List<long> rolled = GetRolledItems(userNumber, id => equipment.Contains(id) || materials.Contains(id));
        rolled.AddRange(RollRandomRewards(stageId, series));

        // Never offer equipment the active hero cannot equip (CheckEquipItemSubType).
        IEnumerable<long> offerable = rolled
            .Select(id => heroGear.ToHeroVersion(id, series))
            .Where(id => materials.Contains(id) || series.Count == 0 || heroGear.CanEquip(id, series))
            .Take(DropRules.MaxDropsPerList);

        var drops = new List<DropOffer>();
        foreach (long itemId in offerable.ToList())
        {
            long itemNumber = items.Add(userNumber, itemId, claimed: false, inventory.GetNewItemStats(itemId));
            if (equipment.Contains(itemId))
            {
                optionRoller.Roll(userNumber, itemNumber, itemId, stageMode);
            }

            drops.Add(new DropOffer(itemId, itemNumber));
        }

        StageMonsterSetup? setup = masterData.GetStageMonsterSetup(stageId);
        bool bossStage = setup is not null && setup.NormalMonsterFile.StartsWith(DropRules.BossMonsterFilePrefix, StringComparison.Ordinal);
        long goldBoxAmount = DropRules.GoldBoxBase + DropRules.GoldBoxPerMonsterLevel * (setup?.MonsterLevel ?? 1);
        int goldBoxes = Math.Min(
            GetRolledItems(userNumber, id => id == CurrencyItems.GoldBox).Count,
            DropRules.MaxDropsPerList - drops.Count);
        for (int i = 0; i < goldBoxes; i++)
        {
            long offerNumber = stages.AddEventOffer(userNumber, CurrencyItems.GoldBox, goldBoxAmount);
            // The boss list sends the box as gold, which CreateDropItem draws as the Gold Box.
            drops.Add(new DropOffer(bossStage ? CurrencyItems.Gold : CurrencyItems.GoldBox, offerNumber));
        }

        return bossStage ? new DropItemList([], drops) : new DropItemList(drops, []);
    }

    /// <summary>
    /// Offers the rolled counted etc items (19xxxxx except the Gold Box, e.g. the World Tree
    /// Branch): drop_event_item_list, at most two kinds; count = how many monsters carry one.
    /// Runs after <see cref="OfferDrops"/>, which cleared the previous offers.
    /// </summary>
    public IReadOnlyList<EventDropOffer> OfferEventItems(long userNumber)
    {
        HashSet<long> etcItems = masterData.GetEtcItemIds();
        var kinds = new List<long>();
        var counts = new Dictionary<long, long>();
        foreach (long itemId in GetRolledItems(userNumber, id => etcItems.Contains(id) && id != CurrencyItems.GoldBox))
        {
            if (!counts.TryAdd(itemId, 1))
            {
                counts[itemId]++;
            }
            else
            {
                kinds.Add(itemId);
            }
        }

        return kinds.Take(DropRules.MaxEventItemKinds)
            .Select(itemId => new EventDropOffer(itemId, counts[itemId], stages.AddEventOffer(userNumber, itemId, counts[itemId])))
            .ToList();
    }

    /// <summary>Turns the offered items the client reports as picked up into owned ones.</summary>
    public List<ItemInfo> ClaimItems(long userNumber, IReadOnlyList<JsonNode?> rewardItems)
    {
        var wanted = new SortedSet<long>();
        foreach (JsonNode? reported in rewardItems)
        {
            JsonNode? itemNumber = reported is JsonObject entry ? entry["item_no"] : reported;
            if (LenientJson.TryToInt64(itemNumber) is { } number)
            {
                wanted.Add(number);
            }
        }

        var claimed = new List<ItemInfo>();
        foreach (long itemNumber in wanted)
        {
            if (items.Claim(userNumber, itemNumber) is { } item)
            {
                claimed.Add(inventory.ToItemInfo(item));
            }
        }

        return claimed;
    }

    /// <summary>
    /// Credits the event offers the client reports: event items come back in event_items, Gold
    /// Boxes in reward_items. Each picked-up kind is reported once, by the offer's item number, so
    /// a reported offer credits its whole count: Gold Boxes to the wallet, the rest to the goods.
    /// </summary>
    /// <returns>What was credited, in report order.</returns>
    public IReadOnlyList<EventOfferRecord> ClaimEventOffers(long userNumber, IEnumerable<long> reportedItemNumbers)
    {
        Dictionary<long, EventOfferRecord> offers = stages.GetEventOffers(userNumber).ToDictionary(offer => offer.ItemNumber);
        var credited = new List<EventOfferRecord>();
        var totals = new List<(long ItemId, long Count)>();
        foreach (long itemNumber in reportedItemNumbers)
        {
            if (!offers.Remove(itemNumber, out EventOfferRecord? offer))
            {
                continue;
            }

            credited.Add(offer);
            int index = totals.FindIndex(total => total.ItemId == offer.ItemId);
            if (index >= 0)
            {
                totals[index] = (offer.ItemId, totals[index].Count + offer.Count);
            }
            else
            {
                totals.Add((offer.ItemId, offer.Count));
            }
        }

        foreach ((long itemId, long count) in totals)
        {
            if (itemId == CurrencyItems.GoldBox)
            {
                players.AddCurrency(userNumber, gold: count);
            }
            else
            {
                goods.Add(userNumber, itemId, count);
            }
        }

        stages.ClearEventOffers(userNumber);
        return credited;
    }

    /// <summary>
    /// The stage result's item_infos: the claimed items plus one entry per picked-up counted etc
    /// item, in the order the client reported its pickups. The client reports a 0 in
    /// reward_items for each such etc item, and opens one chest per entry. Gold Boxes are not chests.
    /// </summary>
    public static IReadOnlyList<object> OrderChests(
        IReadOnlyList<JsonNode?> rewardItems, IReadOnlyList<ItemInfo> claimedItems, IReadOnlyList<EventOfferRecord> creditedOffers)
    {
        var events = new Queue<CountedItemInfo>(creditedOffers
            .Where(offer => offer.ItemId != CurrencyItems.GoldBox)
            .Select(offer => new CountedItemInfo(offer.ItemNumber, offer.ItemId, offer.Count)));
        var remaining = claimedItems.ToList();
        var ordered = new List<object>();

        foreach (JsonNode? reported in rewardItems)
        {
            if (!TryGetWholeNumber(reported, out long itemNumber))
            {
                continue;
            }

            if (itemNumber == 0 && events.Count > 0)
            {
                ordered.Add(events.Dequeue());
                continue;
            }

            int index = remaining.FindIndex(item => item.ItemNumber == itemNumber);
            if (index >= 0)
            {
                ordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
        }

        ordered.AddRange(events);
        ordered.AddRange(remaining);
        return ordered;
    }

    /// <summary>
    /// What the last stage start left to pick up: the offered items, the counted offers (Gold
    /// Boxes, event items) and every rolled slot, fellows included. For the drop audit.
    /// </summary>
    public PendingDrops GetPendingDrops(long userNumber) => new(
        items.GetUnclaimed(userNumber),
        stages.GetEventOffers(userNumber),
        stages.GetRolledRewards(userNumber));

    /// <summary>Grants a cleared stage's rolled fellows. Returns their fellow numbers.</summary>
    public IReadOnlyList<long> GrantDroppedFellows(long userNumber) =>
        GetRolledItems(userNumber, id => id <= DropRules.MaxFellowItemId)
            .Take(DropRules.MaxDroppedFellows)
            .Select(fellowId => fellows.Grant(userNumber, fellowId))
            .ToList();

    /// <summary>
    /// Rolls each slot group once (cumulative chances against one 0-99 roll; at most one slot per
    /// group hits) and keeps the hits with their rolled counts.
    /// </summary>
    private void RollStage(long userNumber, long stageId)
    {
        stages.ClearRolledRewards(userNumber);
        Dictionary<long, StageRewardSlot> slots = masterData.GetRewardSlots(stageId).ToDictionary(slot => slot.Slot);
        foreach ((long first, long last) in DropRules.RewardSlotGroups)
        {
            long roll = random.NextInclusive(0, 99);
            long cumulative = 0;
            bool hit = false;
            for (long slotNumber = first; slotNumber <= last; slotNumber++)
            {
                if (!slots.TryGetValue(slotNumber, out StageRewardSlot? slot))
                {
                    continue;
                }

                cumulative += slot.Chance;
                if (options.DropEverything || (!hit && roll < cumulative))
                {
                    hit = true;
                    long count = random.NextInclusive(Math.Max(1, slot.MinCount), Math.Max(Math.Max(1, slot.MinCount), slot.MaxCount));
                    stages.AddRolledReward(userNumber, slot.Slot, slot.ItemId, count);
                }

                if (hit && !options.DropEverything)
                {
                    break;
                }
            }
        }
    }

    /// <summary>The item ids the last roll hit, each repeated by its rolled count, for the ids <paramref name="wanted"/> accepts.</summary>
    private List<long> GetRolledItems(long userNumber, Func<long, bool> wanted) =>
        stages.GetRolledRewards(userNumber)
            .Where(reward => wanted(reward.ItemId))
            .SelectMany(reward => Enumerable.Repeat(reward.ItemId, (int)reward.Count))
            .ToList();

    /// <summary>
    /// Rolls an area boss's "?" reward: each hit is a uniform pick within the grade range among
    /// the hero's own upgrade line for that item type (type 2 / 3 / 4 = series200 / 300 / 400;
    /// the knights' type * 100 when the hero is unknown). That skips shop and event specials.
    /// </summary>
    private List<long> RollRandomRewards(long stageId, IReadOnlyList<long> series)
    {
        var picks = new List<long>();
        foreach (StageRandomReward reward in masterData.GetRandomRewards(stageId))
        {
            long subType = series.Count > 0 && reward.ItemType is >= 2 and <= 4
                ? series[(int)reward.ItemType - 2]
                : reward.ItemType * 100;
            IReadOnlyList<long> candidates = masterData.GetUpgradeLineItems(reward.ItemType, subType, reward.GradeMin, reward.GradeMax);
            if (candidates.Count > 0 && random.NextInclusive(1, 100) <= reward.Chance)
            {
                long minCount = Math.Max(1, reward.MinCount);
                long count = random.NextInclusive(minCount, Math.Max(minCount, reward.MaxCount));
                picks.AddRange(random.PickMany(candidates, count));
            }
        }

        return picks;
    }

    private static bool TryGetWholeNumber(JsonNode? node, out long number)
    {
        number = 0;
        if (LenientJson.IsInteger(node, out number))
        {
            return true;
        }

        // A float such as 2001.0 matched an item number on the Python server too.
        if (node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number)
        {
            double asDouble = double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
            if (asDouble == Math.Floor(asDouble))
            {
                number = (long)asDouble;
                return true;
            }
        }

        return false;
    }
}

/// <summary>The drops a stage start left open (see <see cref="StageRewardService.GetPendingDrops"/>).</summary>
public sealed record PendingDrops(
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<EventOfferRecord> CountedItems,
    IReadOnlyList<RolledRewardRecord> Rolled);

/// <summary>drop_item_list: chests for random monsters ("1"), and the boss's drop ("4").</summary>
public sealed record DropItemList(
    [property: System.Text.Json.Serialization.JsonPropertyName(DropRules.ChestListKey)] IReadOnlyList<DropOffer> Chests,
    [property: System.Text.Json.Serialization.JsonPropertyName(DropRules.BossListKey)] IReadOnlyList<DropOffer> BossDrops);
