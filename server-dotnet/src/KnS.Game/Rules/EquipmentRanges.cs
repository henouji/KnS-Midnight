namespace KnS.Game.Rules;

/// <summary>
/// Equipment ranges: an item's category is its id rounded down to 100000 (200000 weapons,
/// 300000 shields, 400000 armor, 700000-900000 accessories).
/// </summary>
public static class EquipmentRanges
{
    public const long Weapon = 200000;
    public const long Shield = 300000;
    public const long Armor = 400000;

    public static long CategoryOf(long itemId) => itemId / 100000 * 100000;
}

/// <summary>A starter item every player begins with (fixed item number, never stored in the item table).</summary>
public sealed record StarterItem(long Category, long ItemNumber, long ItemId);

/// <summary>
/// The starter gear of the default hero. Every equipped range must resolve to an inventory item
/// or the dungeon result screen crashes (DrawResult looks the slots up without a null check).
/// </summary>
public static class StarterGear
{
    public static readonly IReadOnlyList<StarterItem> Items =
    [
        new(EquipmentRanges.Weapon, ItemNumber: 1001, ItemId: 200001),
        new(EquipmentRanges.Shield, ItemNumber: 1003, ItemId: 300001),
        new(EquipmentRanges.Armor, ItemNumber: 1002, ItemId: 400001),
    ];

    public static StarterItem? FindByItemNumber(long itemNumber) =>
        Items.FirstOrDefault(item => item.ItemNumber == itemNumber);
}
