namespace KnS.Game.Rules;

public static class FellowRules
{
    public const long StartLevel = 1;

    /// <summary>Positions 0..2 per setup.</summary>
    public const int SlotsPerSetup = 3;

    /// <summary>The fellow setup dungeons use (the Ally screen edits this one).</summary>
    public const long DungeonSetupType = 3;

    /// <summary>The highest fellow_upgrade ("+5").</summary>
    public const long MaxUpgrade = 5;

    /// <summary>The highest grade (<c>FellowMaxGrade</c>): nothing enhances past it.</summary>
    public const long MaxGrade = 6;

    /// <summary>Cap of the attack bonus the power-ups and enhances accumulate (<c>FellowAtkMax</c>).</summary>
    public const long AttackBonusCap = 99;

    /// <summary>The angel allies (110001-110006, master row +0x198, the evolution materials): they cannot take a slot and are neither powered up nor enhanced.</summary>
    public static bool IsAngel(long fellowId) => fellowId is >= 110001 and <= 110006;

    /// <summary>
    /// The success chance (percent) of a power-up (DrawFellowUpgrade @0xa8158): 100 for a material of a higher grade or a target already at
    /// the top upgrade, else the gap's row of master_fellow_power_up_rate plus 5 per 10 Power-up Points, at most 100.
    /// </summary>
    public static long PowerUpPercent(long targetGrade, long materialGrade, long targetUpgrade, long targetFailCount, long baseSuccessPercent) =>
        materialGrade > targetGrade || targetUpgrade >= MaxUpgrade
            ? 100
            : Math.Min(100, baseSuccessPercent + targetFailCount / 10 * 5);

    /// <summary>
    /// Draw odds: the relative weight of each grade (1-6 stars), then a uniform pick inside it.
    /// Tuning, not original data; the real odds lived in the original server.
    /// </summary>
    public static readonly IReadOnlyList<(long Grade, long Weight)> DrawGradeWeights =
        [(1, 30), (2, 25), (3, 20), (4, 13), (5, 8), (6, 4)];
}
