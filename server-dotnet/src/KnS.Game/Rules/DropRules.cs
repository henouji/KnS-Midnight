namespace KnS.Game.Rules;

/// <summary>How a stage start (71) offers drops and a stage result (72) pays them out.</summary>
public static class DropRules
{
    /// <summary>Gold piles the client rolls; it reports their total back as reward_gold.</summary>
    public const long GoldPileCount = 10;

    /// <summary>Per-pile gold cap for those piles.</summary>
    public const long GoldPileMax = 100;

    /// <summary>
    /// drop_item_list key of the chests: each entry goes to a random non-boss monster, which drops
    /// it when it dies.
    /// </summary>
    public const string ChestListKey = "1";

    /// <summary>drop_item_list key of the boss's drop: the boss drops all of it at once.</summary>
    public const string BossListKey = "4";

    /// <summary>The client reads at most this many entries per drop list.</summary>
    public const int MaxDropsPerList = 4;

    /// <summary>master_stage_reward ids up to this are fellows (allies).</summary>
    public const long MaxFellowItemId = 199999;

    /// <summary>The 72 parser reads at most this many drop_fellow_infos.</summary>
    public const int MaxDroppedFellows = 5;

    /// <summary>drop_event_item_list has two slots.</summary>
    public const int MaxEventItemKinds = 2;

    /// <summary>
    /// Gold Box amount = base + per level * master_stage_monster.monster_level (stage 1 = 250 G).
    /// Own tuning: the data has no amount.
    /// </summary>
    public const long GoldBoxBase = 200;

    public const long GoldBoxPerMonsterLevel = 50;

    /// <summary>
    /// master_stage_reward slot groups, each rolled once with a cumulative 0-99 roll like the
    /// client's GetDropItemID: at most one slot per group hits.
    /// </summary>
    public static readonly IReadOnlyList<(long First, long Last)> RewardSlotGroups = [(0, 9), (10, 13)];

    /// <summary>The prefix of the monster files of the 25 area bosses (dt_st_boss_*).</summary>
    public const string BossMonsterFilePrefix = "dt_st_boss";
}
