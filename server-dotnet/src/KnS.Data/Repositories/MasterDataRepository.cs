namespace KnS.Data.Repositories;

/// <summary>
/// Read-only lookups in the master tables (seeded from the game's own data). The tables keep the
/// exporter's column names, including the unnamed <c>cNN</c> ones; this class gives them names.
/// </summary>
public sealed class MasterDataRepository(DataSession session)
{
    /// <summary>
    /// master_stage column taken as the exp a cleared stage awards. Unconfirmed: it is the only
    /// per-stage field that grows like a reward (20/24/28 for stages 3000001-3).
    /// </summary>
    private const string StageExperienceColumn = "c87";

    /// <summary>The equipment ranges that have an allow_* column in master_item_option.</summary>
    private static readonly HashSet<long> OptionBands = [200000, 300000, 400000, 700000, 800000, 900000];

    // ------------------------------------------------------------------ levels

    /// <summary>Exp needed to leave a player level (master_level.exp_to_next), or null for an unknown level.</summary>
    public long? GetExperienceToNextLevel(long level) =>
        session.QueryFirstOrDefault<long?>("SELECT exp_to_next FROM master_level WHERE level=@level", new { level });

    /// <summary>Player level → exp needed to leave it.</summary>
    public Dictionary<long, long> GetExperienceTable() =>
        session.Query<(long Level, long ExperienceToNext)>("SELECT level, exp_to_next FROM master_level")
            .ToDictionary(row => row.Level, row => row.ExperienceToNext);

    /// <summary>Fellow level → exp needed to leave it, for one fellow grade.</summary>
    public Dictionary<long, long> GetFellowExperienceTable(long grade) =>
        session.Query<(long Level, long ExperienceToNext)>(
                "SELECT level, exp_to_next FROM master_fellow_level WHERE grade=@grade", new { grade })
            .ToDictionary(row => row.Level, row => row.ExperienceToNext);

    // ------------------------------------------------------------------ items

    public HashSet<long> GetEquipmentItemIds() => [.. session.Query<long>("SELECT item_id FROM master_item")];

    public HashSet<long> GetMaterialItemIds() => [.. session.Query<long>("SELECT item_id FROM master_material")];

    public HashSet<long> GetEtcItemIds() => [.. session.Query<long>("SELECT item_id FROM master_etc_item")];

    public bool ItemExists(long itemId) =>
        session.Exists("SELECT 1 FROM master_item WHERE item_id=@itemId", new { itemId });

    public ItemStats? GetBaseStats(long itemId) =>
        session.QueryFirstOrDefault<ItemRecord>(
            "SELECT attack AS Attack, defence AS Defence, hp AS Hp FROM master_item WHERE item_id=@itemId", new { itemId })
            is { } row ? new ItemStats(row.Attack, row.Defence, row.Hp) : null;

    public long? GetSubType(long itemId) =>
        session.QueryFirstOrDefault<long?>("SELECT sub_type FROM master_item WHERE item_id=@itemId", new { itemId });

    public long? GetSellPrice(long itemId) =>
        session.QueryFirstOrDefault<long?>("SELECT sell_price FROM master_item WHERE item_id=@itemId", new { itemId });

    public ItemPowerUpRule? GetPowerUpRule(long itemId) =>
        session.QueryFirstOrDefault<ItemPowerUpRule>(
            "SELECT c4 AS MaxUpgrade, c5 AS BasePrice, c6 AS PricePerUpgrade,"
            + " attack_up AS AttackGain, defence_up AS DefenceGain, hp_up AS HpGain FROM master_item WHERE item_id=@itemId",
            new { itemId });

    public ItemEnhanceRule? GetEnhanceRule(long itemId) =>
        session.QueryFirstOrDefault<ItemEnhanceRule>(
            "SELECT c4 AS MaxUpgrade, next_item_id AS NextItemId, upgrade_cost AS Price,"
            + " material1 AS Material1, material2 AS Material2, material3 AS Material3,"
            + " material4 AS Material4, material5 AS Material5, material6 AS Material6"
            + " FROM master_item WHERE item_id=@itemId",
            new { itemId });

    /// <summary>
    /// Items of one type and sub_type within a grade range that are part of an upgrade line
    /// (they evolve into, or from, another item). Ordered by item id.
    /// </summary>
    public IReadOnlyList<long> GetUpgradeLineItems(long itemType, long subType, long gradeMin, long gradeMax) =>
        session.Query<long>(
            "SELECT item_id FROM master_item WHERE type=@itemType AND sub_type=@subType"
            + " AND grade BETWEEN @gradeMin AND @gradeMax AND (next_item_id <> -1"
            + " OR item_id IN (SELECT next_item_id FROM master_item)) ORDER BY item_id",
            new { itemType, subType, gradeMin, gradeMax });

    // ------------------------------------------------------------------ item options

    public ItemOptionRule? GetOptionRule(long itemId) =>
        session.QueryFirstOrDefault<ItemOptionRule>(
            "SELECT category AS Category, grade AS Grade, c31 AS HasFixedOptions,"
            + " c32 AS FixedOption1, c33 AS FixedOption2, c34 AS FixedOption3,"
            + " c35 AS FixedOption4, c36 AS FixedOption5, c37 AS FixedOption6"
            + " FROM master_item WHERE item_id=@itemId",
            new { itemId });

    /// <summary>How likely each option count is, for one item source ("Nomal", "Hard", ...).</summary>
    public IReadOnlyList<OptionCountWeight> GetOptionCountWeights(string source) =>
        session.Query<OptionCountWeight>(
            "SELECT option_count AS OptionCount, percent AS Percent FROM master_item_option_count"
            + " WHERE stage_mode_name=@source ORDER BY option_count",
            new { source });

    /// <summary>Options an item of this equipment range and grade may roll (tier 1..grade), by option id.</summary>
    public IReadOnlyList<long> GetRollableOptions(long band, long grade)
    {
        if (!OptionBands.Contains(band))
        {
            throw new ArgumentOutOfRangeException(nameof(band), band, "No option column for this equipment range.");
        }

        return session.Query<long>(
            $"SELECT option_id FROM master_item_option WHERE allow_{band}<>0 AND tier BETWEEN 1 AND @grade ORDER BY option_id",
            new { grade });
    }

    public OptionValueRange? GetOptionValueRange(long optionId) =>
        session.QueryFirstOrDefault<OptionValueRange>(
            "SELECT value1_min AS Value1Min, value1_max AS Value1Max, value2_min AS Value2Min, value2_max AS Value2Max"
            + " FROM master_item_option WHERE option_id=@optionId",
            new { optionId });

    // ------------------------------------------------------------------ heroes

    public HeroCatalogEntry? GetHero(long heroPcid) =>
        session.QueryFirstOrDefault<HeroCatalogEntry>(
            "SELECT price_gems AS PriceGems, weapon_id AS WeaponId, slot300_id AS ShieldId, armor_id AS ArmorId"
            + " FROM master_hero WHERE hero_pcid=@heroPcid",
            new { heroPcid });

    /// <summary>
    /// The item sub_types a hero can equip (series200, 300, 400, 700, 800, 900), in that order;
    /// empty for an unknown hero.
    /// </summary>
    public IReadOnlyList<long> GetHeroSeries(long heroPcid)
    {
        HeroSeriesRow? row = session.QueryFirstOrDefault<HeroSeriesRow>(
            "SELECT series200 AS Weapon, series300 AS Shield, series400 AS Armor,"
            + " series700 AS Accessory700, series800 AS Accessory800, series900 AS Accessory900"
            + " FROM master_hero WHERE hero_pcid=@heroPcid",
            new { heroPcid });
        return row is null ? [] : [row.Weapon, row.Shield, row.Armor, row.Accessory700, row.Accessory800, row.Accessory900];
    }

    private sealed record HeroSeriesRow
    {
        public long Weapon { get; init; }
        public long Shield { get; init; }
        public long Armor { get; init; }
        public long Accessory700 { get; init; }
        public long Accessory800 { get; init; }
        public long Accessory900 { get; init; }
    }

    // ------------------------------------------------------------------ fellows

    /// <summary>The power-up success table, by grade gap.</summary>
    public IReadOnlyList<FellowPowerUpRate> GetFellowPowerUpRates() =>
        session.Query<FellowPowerUpRate>(
            "SELECT grade_gap AS GradeGap, success_percent AS SuccessPercent, fail_points AS FailPoints"
            + " FROM master_fellow_power_up_rate ORDER BY grade_gap");

    /// <summary>The power-up and enhance gold of the (grade, upgrade) row of master_fellow_upgrade.</summary>
    public FellowUpgradeCosts? GetFellowUpgradeCosts(long grade, long upgrade) =>
        session.QueryFirstOrDefault<FellowUpgradeCosts>(
            "SELECT c2 AS PowerUpGold, c3 AS EnhanceGold FROM master_fellow_upgrade WHERE grade=@grade AND upgrade=@upgrade",
            new { grade, upgrade });

    /// <summary>Fellows a draw can give (max_level above 1), by fellow id.</summary>
    public IReadOnlyList<DrawableFellow> GetDrawableFellows() =>
        session.Query<DrawableFellow>(
            "SELECT fellow_id AS FellowId, grade AS Grade FROM master_fellow WHERE max_level > 1 ORDER BY fellow_id");

    // ------------------------------------------------------------------ stages

    public long? GetStageExperience(long stageId) =>
        session.QueryFirstOrDefault<long?>(
            $"SELECT {StageExperienceColumn} FROM master_stage WHERE stage_id=@stageId", new { stageId });

    public StageMonsterSetup? GetStageMonsterSetup(long stageId) =>
        session.QueryFirstOrDefault<StageMonsterSetup>(
            "SELECT monster_level AS MonsterLevel, normal_monster_file AS NormalMonsterFile, hard_monster_file AS HardMonsterFile"
            + " FROM master_stage_monster WHERE stage_id=@stageId",
            new { stageId });

    /// <summary>
    /// A monster file's row for one client monster slot at the highest level up to
    /// <paramref name="maxLevel"/>, as column name → value; null when there is none.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? GetMonsterStats(string monsterFile, long slot, long maxLevel)
    {
        var rows = session.QueryRows(
            "SELECT * FROM master_monster WHERE monster_file=@monsterFile AND slot=@slot AND level<=@maxLevel"
            + " ORDER BY level DESC LIMIT 1",
            new { monsterFile, slot, maxLevel });
        return rows.Count > 0 ? rows[0] : null;
    }

    public IReadOnlyList<StageRewardSlot> GetRewardSlots(long stageId) =>
        session.Query<StageRewardSlot>(
            "SELECT slot AS Slot, item_id AS ItemId, chance AS Chance, min_count AS MinCount, max_count AS MaxCount"
            + " FROM master_stage_reward WHERE stage_id=@stageId",
            new { stageId });

    public IReadOnlyList<StageRandomReward> GetRandomRewards(long stageId) =>
        session.Query<StageRandomReward>(
            "SELECT chance AS Chance, item_type AS ItemType, grade_min AS GradeMin, grade_max AS GradeMax,"
            + " min_count AS MinCount, max_count AS MaxCount FROM master_stage_random_reward WHERE stage_id=@stageId",
            new { stageId });

    public long? GetAreaOfStage(long stageId) =>
        session.QueryFirstOrDefault<long?>("SELECT area_id FROM master_stage_area WHERE stage_id=@stageId", new { stageId });

    public AreaRewardRule? GetAreaReward(long areaId, bool hardMode) =>
        session.QueryFirstOrDefault<AreaRewardRule>(
            hardMode
                ? "SELECT hard_clears AS ClearsNeeded, hard_reward_item_id AS RewardItemId, hard_reward_count AS RewardCount"
                  + " FROM master_stage_attainment WHERE area_id=@areaId"
                : "SELECT normal_clears AS ClearsNeeded, normal_reward_item_id AS RewardItemId, normal_reward_count AS RewardCount"
                  + " FROM master_stage_attainment WHERE area_id=@areaId",
            new { areaId });

    // ------------------------------------------------------------------ shop

    /// <summary>A named price from dt_etc_setting.json's Store table (e.g. "Continue").</summary>
    public Price? GetStorePrice(string name) =>
        session.QueryFirstOrDefault<Price>(
            "SELECT price_item_id AS PriceItemId, price AS Amount FROM master_store_price WHERE price_name=@name", new { name });

    public ShopPackage? GetShopPackage(long packageId) =>
        session.QueryFirstOrDefault<ShopPackage>(
            "SELECT price_item_id AS PriceItemId, price AS Price, draw_count AS DrawCount FROM master_shop_package"
            + " WHERE package_id=@packageId",
            new { packageId });

    public BagExpansion? GetBagExpansion(BagKind bag, long expansionLevel)
    {
        string table = bag == BagKind.Items ? "master_inventory_expansion" : "master_fellow_inventory_expansion";
        return session.QueryFirstOrDefault<BagExpansion>(
            $"SELECT price_item_id AS PriceItemId, price AS Price, add_space AS AddedSlots, total_space AS TotalSlots FROM {table}"
            + " WHERE expansion_level=@expansionLevel",
            new { expansionLevel });
    }
}
