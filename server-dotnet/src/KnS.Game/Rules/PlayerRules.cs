namespace KnS.Game.Rules;

/// <summary>
/// Player values the client has no master-data cross-check for. Tune them live: the server
/// picks them up on restart.
/// </summary>
public static class PlayerRules
{
    /// <summary>The exp bar's size when master_level has no row for the level.</summary>
    public const long FallbackMaxExperience = 100;

    public const long Stamina = 100;

    /// <summary>Free item bag slots; the client's cap is these plus the bought ones.</summary>
    public const long FreeItemSlots = 50;

    public const long HeroAttack = 10;
    public const long HeroDefence = 10;
    public const long HeroHp = 100;

    /// <summary>The hero_no of the hero every player owns.</summary>
    public const long DefaultHeroNumber = 1;

    /// <summary>The name shown for a player without a nickname: "Squire" + user number.</summary>
    public static string DefaultNickname(long userNumber) => "Squire" + userNumber;
}
