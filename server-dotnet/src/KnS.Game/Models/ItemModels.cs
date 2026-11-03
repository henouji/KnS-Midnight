using System.Text.Json.Serialization;

namespace KnS.Game.Models;

/// <summary>An item_etc_info row: a currency or counted item balance (ParsingDataToUserGoods@0x14c878).</summary>
public sealed record GoodsEntry(
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("count")] long Count);

/// <summary>A starter item in an item list: just its ids and who wears it.</summary>
public sealed record StarterItemInfo(
    [property: JsonPropertyName("item_no")] long ItemNumber,
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("hero_no")] long HeroNumber);

/// <summary>An owned item (ParsingDataToItem@0x14cca4 / ParsingDataToEquipItem@0x14caa1).</summary>
public sealed record ItemInfo
{
    /// <summary>The unique instance id; equipped slots resolve to inventory nodes by it.</summary>
    [JsonPropertyName("item_no")] public long ItemNumber { get; init; }

    /// <summary>The master catalog id.</summary>
    [JsonPropertyName("item_id")] public long ItemId { get; init; }

    /// <summary>The hero wearing it; 0 = in the bag.</summary>
    [JsonPropertyName("hero_no")] public long HeroNumber { get; init; }

    [JsonPropertyName("item_upgrade")] public long Upgrade { get; init; }
    [JsonPropertyName("item_attack")] public long Attack { get; init; }
    [JsonPropertyName("item_defence")] public long Defence { get; init; }
    [JsonPropertyName("item_hp")] public long Hp { get; init; }
    [JsonPropertyName("item_lock")] public long Locked { get; init; }
    [JsonPropertyName("item_option_count")] public long OptionCount { get; init; }
    [JsonPropertyName("item_options")] public IReadOnlyList<ItemOptionInfo> Options { get; init; } = [];
}

/// <summary>An item option (enchantment): ids at item+0x18, value1 +0x30, value2 +0x48.</summary>
public sealed record ItemOptionInfo(
    [property: JsonPropertyName("item_option_id")] long OptionId,
    [property: JsonPropertyName("item_option_value1")] long Value1,
    [property: JsonPropertyName("item_option_value2")] long Value2);

/// <summary>A picked-up counted etc item on the result screen; a 19xxxxx item_info carries its count in value.</summary>
public sealed record CountedItemInfo(
    [property: JsonPropertyName("item_no")] long ItemNumber,
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("value")] long Count);

/// <summary>A drop a stage start offers (chest or boss drop).</summary>
public sealed record DropOffer(
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("item_no")] long ItemNumber);

/// <summary>A counted etc item a stage start offers: <see cref="Count"/> monsters carry one.</summary>
public sealed record EventDropOffer(
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("count")] long Count,
    [property: JsonPropertyName("item_no")] long ItemNumber);
