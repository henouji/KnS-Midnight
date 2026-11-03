namespace KnS.Game.Rules;

public static class ItemOptionRules
{
    /// <summary>An item has at most this many option slots.</summary>
    public const int MaxOptions = 6;

    /// <summary>The equipment ranges whose items get options.</summary>
    public static readonly IReadOnlySet<long> OptionBearingCategories =
        new HashSet<long> { 200000, 300000, 400000, 700000, 800000, 900000 };

    /// <summary>
    /// master_item_option_count's source for a stage mode ("Nomal" is the data's spelling).
    /// </summary>
    public static string OptionCountSource(long stageMode) => stageMode == 2 ? "Hard" : "Nomal";

    /// <summary>Options of one family (option id / 100) exclude each other on an item.</summary>
    public static long FamilyOf(long optionId) => optionId / 100;
}
