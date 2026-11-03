using KnS.Data.Repositories;
using KnS.Game.Rules;

namespace KnS.Game.Inventory;

/// <summary>
/// Which items a hero can equip, and the hero's own version of an item.
/// </summary>
/// <remarks>
/// A hero's "series" are the item sub_types it can equip (master_hero.series200..900: 200/300/400
/// for knights, 201/301/401 for demon hunters, 700/800/900 shared). The client keeps the active
/// hero's six at gameInfo+0x1510 and CheckEquipItemSubType@0xb96e0 tests items against them.
/// </remarks>
public sealed class HeroGearRules(MasterDataRepository masterData)
{
    /// <summary>The position in the series list of each equipment range that has hero versions.</summary>
    private static readonly Dictionary<long, int> SeriesIndexOfCategory = new()
    {
        [EquipmentRanges.Weapon] = 0,
        [EquipmentRanges.Shield] = 1,
        [EquipmentRanges.Armor] = 2,
    };

    public IReadOnlyList<long> GetSeries(long heroPcid) => masterData.GetHeroSeries(heroPcid);

    public bool CanEquip(long itemId, IReadOnlyList<long> series) =>
        masterData.GetSubType(itemId) is { } subType && series.Contains(subType);

    /// <summary>
    /// The item a stage reward becomes for a hero. Reward lists name the knights' line (200xxx,
    /// 300xxx, 400xxx); the client shows each in the current hero's line
    /// (ChangeEquipItemByCharacterID@0xf4a48): new id = series * 1000 + id % 1000, so the Long
    /// Sword 200001 is the Whip 201001 for a demon hunter. An id with no such twin stays as it is.
    /// </summary>
    public long ToHeroVersion(long itemId, IReadOnlyList<long> series)
    {
        long category = EquipmentRanges.CategoryOf(itemId);
        if (series.Count == 0 || !SeriesIndexOfCategory.TryGetValue(category, out int index))
        {
            return itemId;
        }

        long twin = series[index] * 1000 + itemId % 1000;
        return masterData.ItemExists(twin) ? twin : itemId;
    }
}
