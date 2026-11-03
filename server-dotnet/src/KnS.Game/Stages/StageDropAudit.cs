using System.Text.Json.Nodes;
using KnS.Data.Repositories;
using KnS.Game.Models;
using KnS.Game.Rules;
using KnS.Protocol.Json;

namespace KnS.Game.Stages;

/// <summary>
/// The traffic log's view of one stage run: what the stage start (71) left to pick up, what the
/// stage result (72) reports and what the server paid out. A drop the client shows but the server
/// does not keep shows up as a reported number nothing was offered under, an etc-item pickup with
/// no offer behind it, a rolled fellow that was not granted, or a stage result field nothing reads.
/// </summary>
public static class StageDropAudit
{
    /// <summary>The rolled reward slots, labelled fellow / gold box / item: the 71 reply does not show the fellows.</summary>
    public static JsonArray DescribeRolled(IEnumerable<RolledRewardRecord> rolled) =>
        [.. rolled.Select(reward => new JsonObject
        {
            ["item_id"] = reward.ItemId,
            ["count"] = reward.Count,
            ["kind"] = KindOf(reward.ItemId),
        })];

    /// <param name="offered">The pending drops before the result claimed any.</param>
    /// <param name="rewardItems">reward_items as sent: item numbers (or {item_no}), 0 for each etc-item pickup.</param>
    /// <param name="eventItems">event_items as sent: the offer numbers of the picked-up event items.</param>
    /// <param name="claimedItems">The offered items the result turned into owned ones.</param>
    /// <param name="creditedOffers">The counted offers the result paid out.</param>
    /// <param name="cleared">Whether the result counted as a clear (fellows are granted only then).</param>
    /// <param name="grantedFellowIds">The fellow ids granted.</param>
    /// <param name="unreadParameters">The stage result fields no code read.</param>
    /// <param name="warnings">Receives a line per finding that looks like a lost drop.</param>
    public static JsonObject Build(
        PendingDrops offered,
        IReadOnlyList<JsonNode?> rewardItems,
        IReadOnlyList<JsonNode?> eventItems,
        IReadOnlyList<ItemInfo> claimedItems,
        IReadOnlyList<EventOfferRecord> creditedOffers,
        bool cleared,
        IReadOnlyList<long> grantedFellowIds,
        IReadOnlyList<string> unreadParameters,
        List<string> warnings)
    {
        var offeredNumbers = offered.Items.Select(item => item.ItemNumber)
            .Concat(offered.CountedItems.Select(offer => offer.ItemNumber))
            .ToHashSet();

        var notOffered = new List<long>();
        var unreadable = new JsonArray();
        int etcPickups = 0;
        foreach (JsonNode? reported in rewardItems.Concat(eventItems))
        {
            JsonNode? number = reported is JsonObject entry ? entry["item_no"] : reported;
            if (LenientJson.TryToInt64(number) is not { } itemNumber)
            {
                unreadable.Add(reported?.DeepClone());
            }
            else if (itemNumber == 0)
            {
                etcPickups++;
            }
            else if (!offeredNumbers.Contains(itemNumber))
            {
                notOffered.Add(itemNumber);
            }
        }

        var claimedNumbers = claimedItems.Select(item => item.ItemNumber).ToHashSet();
        var creditedNumbers = creditedOffers.Select(offer => offer.ItemNumber).ToHashSet();
        int creditedEtcItems = creditedOffers.Count(offer => offer.ItemId != CurrencyItems.GoldBox);

        List<long> rolledFellows = offered.Rolled
            .Where(reward => reward.ItemId <= DropRules.MaxFellowItemId)
            .SelectMany(reward => Enumerable.Repeat(reward.ItemId, (int)reward.Count))
            .ToList();
        var fellowsNotGranted = rolledFellows.ToList();
        foreach (long fellowId in grantedFellowIds)
        {
            fellowsNotGranted.Remove(fellowId);
        }

        if (notOffered.Count > 0)
        {
            warnings.Add($"client reported {notOffered.Count} drop(s) the server never offered: [{string.Join(",", notOffered)}]");
        }

        if (unreadable.Count > 0)
        {
            warnings.Add($"client reported drops the server cannot read: {unreadable.ToJsonString()}");
        }

        if (etcPickups > creditedEtcItems)
        {
            warnings.Add($"client reported {etcPickups} etc-item pickup(s) (0 in reward_items) but {creditedEtcItems} event offer(s) were credited");
        }

        if (cleared && fellowsNotGranted.Count > 0)
        {
            warnings.Add($"rolled fellow(s) not granted on a clear: [{string.Join(",", fellowsNotGranted)}]");
        }

        if (unreadParameters.Count > 0)
        {
            warnings.Add($"stage result fields the server ignores: [{string.Join(",", unreadParameters)}]");
        }

        return new JsonObject
        {
            ["offered"] = new JsonObject
            {
                ["items"] = DescribeItems(offered.Items),
                ["counted"] = DescribeOffers(offered.CountedItems),
                ["rolled"] = DescribeRolled(offered.Rolled),
            },
            ["reported"] = new JsonObject
            {
                ["reward_items"] = ToArray(rewardItems),
                ["event_items"] = ToArray(eventItems),
                ["etc_pickups"] = etcPickups,
            },
            ["granted"] = new JsonObject
            {
                ["items"] = new JsonArray([.. claimedItems.Select(item => (JsonNode)new JsonObject { ["item_no"] = item.ItemNumber, ["item_id"] = item.ItemId })]),
                ["counted"] = DescribeOffers(creditedOffers),
                ["fellows"] = new JsonArray([.. grantedFellowIds.Select(id => (JsonNode)id)]),
                ["cleared"] = cleared,
            },
            ["not_offered"] = new JsonArray([.. notOffered.Select(number => (JsonNode)number)]),
            ["unreadable"] = unreadable,
            ["not_picked_up"] = new JsonObject
            {
                ["items"] = DescribeItems(offered.Items.Where(item => !claimedNumbers.Contains(item.ItemNumber))),
                ["counted"] = DescribeOffers(offered.CountedItems.Where(offer => !creditedNumbers.Contains(offer.ItemNumber))),
            },
            ["fellows_not_granted"] = new JsonArray([.. fellowsNotGranted.Select(id => (JsonNode)id)]),
        };
    }

    private static string KindOf(long itemId) => itemId switch
    {
        <= DropRules.MaxFellowItemId => "fellow",
        CurrencyItems.GoldBox => "gold_box",
        _ => "item",
    };

    private static JsonArray DescribeItems(IEnumerable<ItemRecord> items) =>
        [.. items.Select(item => new JsonObject { ["item_no"] = item.ItemNumber, ["item_id"] = item.ItemId })];

    private static JsonArray DescribeOffers(IEnumerable<EventOfferRecord> offers) =>
        [.. offers.Select(offer => new JsonObject { ["item_no"] = offer.ItemNumber, ["item_id"] = offer.ItemId, ["count"] = offer.Count })];

    private static JsonArray ToArray(IEnumerable<JsonNode?> nodes) => [.. nodes.Select(node => node?.DeepClone())];
}
