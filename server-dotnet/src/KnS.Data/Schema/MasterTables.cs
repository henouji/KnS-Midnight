namespace KnS.Data.Schema;

/// <summary>A read-only table loaded from a seed CSV exported from the game's data.</summary>
/// <param name="Table">Table name.</param>
/// <param name="SeedFile">CSV file name in the master-data folder; its header row names the columns.</param>
/// <param name="KeyColumns">Primary key columns; null means the first CSV column.</param>
public sealed record MasterTable(string Table, string SeedFile, IReadOnlyList<string>? KeyColumns = null);

public static class MasterTables
{
    public static readonly IReadOnlyList<MasterTable> All =
    [
        new("master_item", "master_items.csv"),
        new("master_hero", "master_heroes.csv"),
        new("master_etc_item", "master_etc_items.csv"),
        new("master_material", "master_materials.csv"),
        new("master_level", "master_levels.csv"),
        new("master_stage", "master_stages.csv"),
        new("master_stage_area", "master_stage_areas.csv"),
        new("master_inventory_expansion", "master_inventory_expansions.csv"),
        new("master_monster", "master_monsters.csv", ["monster_file", "slot", "level"]),
        new("master_stage_monster", "master_stage_monsters.csv"),
        new("master_fellow", "master_fellows.csv"),
        new("master_shop_package", "master_shop_packages.csv"),
        new("master_store_price", "master_store_prices.csv"),
        new("master_fellow_upgrade", "master_fellow_upgrades.csv", ["grade", "upgrade"]),
        new("master_fellow_level", "master_fellow_levels.csv", ["level", "grade"]),
        new("master_fellow_power_up_rate", "master_fellow_power_up_rates.csv"),
        new("master_stage_reward", "master_stage_rewards.csv", ["stage_id", "slot"]),
        new("master_stage_random_reward", "master_stage_random_rewards.csv"),
        new("master_fellow_inventory_expansion", "master_fellow_inventory_expansions.csv"),
        new("master_stage_attainment", "master_stage_attainments.csv"),
        new("master_item_option", "master_item_options.csv"),
        new("master_item_option_count", "master_item_option_counts.csv", ["stage_mode_name", "option_count"]),
    ];
}
