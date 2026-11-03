namespace KnS.Data.Repositories;

/// <summary>Power-up rule of an item (master_item; dt_item_pkg_140).</summary>
public sealed record ItemPowerUpRule
{
    /// <summary>Highest upgrade level (c4); Enhance is allowed from this level on.</summary>
    public long MaxUpgrade { get; init; }

    /// <summary>Price at upgrade 0 (c5).</summary>
    public long BasePrice { get; init; }

    /// <summary>Price added per upgrade level already reached (c6).</summary>
    public long PricePerUpgrade { get; init; }

    public long AttackGain { get; init; }
    public long DefenceGain { get; init; }
    public long HpGain { get; init; }
}

/// <summary>Enhance (evolve) rule of an item (master_item).</summary>
public sealed record ItemEnhanceRule
{
    /// <summary>The upgrade level the item must reach first (c4).</summary>
    public long MaxUpgrade { get; init; }

    public long NextItemId { get; init; }
    public long Price { get; init; }
    public long Material1 { get; init; }
    public long Material2 { get; init; }
    public long Material3 { get; init; }
    public long Material4 { get; init; }
    public long Material5 { get; init; }
    public long Material6 { get; init; }

    /// <summary>The material item ids the enhance consumes (unused material slots left out).</summary>
    public IEnumerable<long> Materials =>
        new[] { Material1, Material2, Material3, Material4, Material5, Material6 }.Where(id => id > 0);
}

/// <summary>What decides the options a new item gets (master_item).</summary>
public sealed record ItemOptionRule
{
    /// <summary>The equipment range (200000 weapon, ...): which allow_* column of master_item_option applies.</summary>
    public long Category { get; init; }

    public long Grade { get; init; }

    /// <summary>1 when the item has fixed options (c31) instead of rolled ones.</summary>
    public long HasFixedOptions { get; init; }

    public long FixedOption1 { get; init; }
    public long FixedOption2 { get; init; }
    public long FixedOption3 { get; init; }
    public long FixedOption4 { get; init; }
    public long FixedOption5 { get; init; }
    public long FixedOption6 { get; init; }

    public IEnumerable<long> FixedOptions =>
        new[] { FixedOption1, FixedOption2, FixedOption3, FixedOption4, FixedOption5, FixedOption6 }.Where(id => id > 0);
}

public sealed record OptionCountWeight
{
    public long OptionCount { get; init; }
    public long Percent { get; init; }
}

public sealed record OptionValueRange
{
    public long Value1Min { get; init; }
    public long Value1Max { get; init; }
    public long Value2Min { get; init; }
    public long Value2Max { get; init; }
}

/// <summary>One row of the power-up table by grade gap (target grade - material grade).</summary>
public sealed record FellowPowerUpRate
{
    public long GradeGap { get; init; }
    public long SuccessPercent { get; init; }

    /// <summary>The Power-up Points a failure adds.</summary>
    public long FailPoints { get; init; }
}

/// <summary>The gold a power-up (c2) and an enhance (c3) of a fellow row cost.</summary>
public sealed record FellowUpgradeCosts
{
    public long PowerUpGold { get; init; }
    public long EnhanceGold { get; init; }
}

public sealed record DrawableFellow
{
    public long FellowId { get; init; }
    public long Grade { get; init; }
}

public sealed record StageMonsterSetup
{
    public long MonsterLevel { get; init; }
    public string NormalMonsterFile { get; init; } = "";
    public string HardMonsterFile { get; init; } = "";
}

public sealed record StageRewardSlot
{
    public long Slot { get; init; }
    public long ItemId { get; init; }

    /// <summary>Percent chance, cumulative within the slot's group.</summary>
    public long Chance { get; init; }

    public long MinCount { get; init; }
    public long MaxCount { get; init; }
}

/// <summary>An area boss's "?" reward (master_stage_random_reward).</summary>
public sealed record StageRandomReward
{
    public long Chance { get; init; }
    public long ItemType { get; init; }
    public long GradeMin { get; init; }
    public long GradeMax { get; init; }
    public long MinCount { get; init; }
    public long MaxCount { get; init; }
}

public sealed record AreaRewardRule
{
    /// <summary>Clears of the area needed before the reward can be claimed.</summary>
    public long ClearsNeeded { get; init; }

    public long RewardItemId { get; init; }
    public long RewardCount { get; init; }
}

/// <summary>A price in some currency item (gold 1900008 or gems 1900009).</summary>
public sealed record Price
{
    public long PriceItemId { get; init; }
    public long Amount { get; init; }
}

public sealed record ShopPackage
{
    public long PriceItemId { get; init; }
    public long Price { get; init; }
    public long DrawCount { get; init; }
}

public sealed record BagExpansion
{
    public long PriceItemId { get; init; }
    public long Price { get; init; }
    public long AddedSlots { get; init; }

    /// <summary>The bought slots after this expansion.</summary>
    public long TotalSlots { get; init; }
}

/// <summary>A hero offered on the Characters screen and the gear it comes with.</summary>
public sealed record HeroCatalogEntry
{
    public long PriceGems { get; init; }
    public long WeaponId { get; init; }
    public long ShieldId { get; init; }
    public long ArmorId { get; init; }

    public IEnumerable<long> DefaultGear => [WeaponId, ShieldId, ArmorId];
}
