using KnS.Data.Repositories;
using KnS.Game.Randomness;
using KnS.Game.Rules;

namespace KnS.Game.Inventory;

/// <summary>Gives a new equipment item its options (enchantments).</summary>
public sealed class ItemOptionRoller(
    MasterDataRepository masterData,
    ItemRepository items,
    IRandomSource random,
    GameOptions options)
{
    /// <summary>
    /// Items with fixed options (master_item.c31 = 1) get those. Others roll a count from the
    /// stage mode's master_item_option_count table, then that many options of distinct families
    /// the item's range and grade allow (allow_&lt;range&gt; set, tier 1..grade), uniformly: the
    /// original weights are unknown. Values are uniform within the option's ranges.
    /// </summary>
    /// <returns>The option ids given.</returns>
    public IReadOnlyList<long> Roll(long userNumber, long itemNumber, long itemId, long stageMode)
    {
        ItemOptionRule? rule = masterData.GetOptionRule(itemId);
        if (rule is null || !ItemOptionRules.OptionBearingCategories.Contains(rule.Category))
        {
            return [];
        }

        List<long> picked = rule.HasFixedOptions == 1
            ? rule.FixedOptions.ToList()
            : RollOptionIds(rule, stageMode);
        picked = picked.Take(ItemOptionRules.MaxOptions).ToList();

        for (int slot = 0; slot < picked.Count; slot++)
        {
            OptionValueRange range = masterData.GetOptionValueRange(picked[slot]) ?? new OptionValueRange();
            long value1 = random.NextInclusive(Math.Min(range.Value1Min, range.Value1Max), Math.Max(range.Value1Min, range.Value1Max));
            long value2 = range.Value2Min > 0
                ? random.NextInclusive(Math.Min(range.Value2Min, range.Value2Max), Math.Max(range.Value2Min, range.Value2Max))
                : 0;
            items.SetOption(userNumber, itemNumber, slot, picked[slot], value1, value2);
        }

        return picked;
    }

    private List<long> RollOptionIds(ItemOptionRule rule, long stageMode)
    {
        IReadOnlyList<OptionCountWeight> weights = masterData.GetOptionCountWeights(ItemOptionRules.OptionCountSource(stageMode));
        long count;
        if (options.DropEverything)
        {
            count = weights.Where(w => w.Percent > 0).Select(w => w.OptionCount).DefaultIfEmpty(0).Max();
        }
        else
        {
            count = weights.Count > 0 && weights.Sum(w => w.Percent) > 0
                ? random.PickWeighted(weights.Select(w => w.OptionCount).ToList(), weights.Select(w => w.Percent).ToList())
                : 0;
        }

        List<long> candidates = [.. masterData.GetRollableOptions(rule.Category, rule.Grade)];
        random.Shuffle(candidates);

        var picked = new List<long>();
        var families = new HashSet<long>();
        foreach (long optionId in candidates)
        {
            if (picked.Count >= count)
            {
                break;
            }

            if (families.Add(ItemOptionRules.FamilyOf(optionId)))
            {
                picked.Add(optionId);
            }
        }

        return picked;
    }
}
