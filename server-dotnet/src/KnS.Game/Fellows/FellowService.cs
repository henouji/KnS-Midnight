using KnS.Data.Repositories;
using KnS.Game.Models;
using KnS.Game.Randomness;
using KnS.Game.Rules;

namespace KnS.Game.Fellows;

/// <summary>Fellows ("allies"): their views, draws, and exp from dungeons.</summary>
public sealed class FellowService(FellowRepository fellows, MasterDataRepository masterData, IRandomSource random)
{
    /// <summary>The player's fellows, oldest first; only <paramref name="fellowNumbers"/> when given.</summary>
    public IReadOnlyList<FellowInfo> GetFellowInfos(long userNumber, IReadOnlySet<long>? fellowNumbers = null) =>
        fellows.GetOwned(userNumber)
            .Where(fellow => fellowNumbers is null || fellowNumbers.Contains(fellow.FellowNumber))
            .Select(ToFellowInfo)
            .ToList();

    /// <summary>The collection book, by fellow id; only <paramref name="fellowIds"/> when given.</summary>
    public IReadOnlyList<FellowBookEntry> GetBook(long userNumber, IReadOnlySet<long>? fellowIds = null) =>
        fellows.GetBook(userNumber)
            .Where(entry => fellowIds is null || fellowIds.Contains(entry.FellowId))
            .Select(entry => new FellowBookEntry(entry.FellowId, entry.RewardReceived, entry.Experience, entry.Level))
            .ToList();

    public IReadOnlyList<ActiveFellowSlot> GetActiveSlots(long userNumber) =>
        fellows.GetFilledSlots(userNumber)
            .Select(slot => new ActiveFellowSlot(slot.SetupType, slot.Position, slot.FellowNumber))
            .ToList();

    /// <summary>Gives the player a fellow (and its collection record). Returns the fellow number.</summary>
    public long Grant(long userNumber, long fellowId)
    {
        long fellowNumber = fellows.Add(userNumber, fellowId, FellowRules.StartLevel);
        fellows.RecordInBook(userNumber, fellowId);
        return fellowNumber;
    }

    /// <summary>A uniform drawable fellow of one grade (the new ally of an enhance); null when the grade has none.</summary>
    public long? PickDrawableOfGrade(long grade)
    {
        List<long> ofGrade = masterData.GetDrawableFellows().Where(fellow => fellow.Grade == grade).Select(fellow => fellow.FellowId).ToList();
        return ofGrade.Count == 0 ? null : random.Pick(ofGrade);
    }

    /// <summary>A drawable fellow: the grade by <see cref="FellowRules.DrawGradeWeights"/>, then uniform within it.</summary>
    public long RollDrawnFellow()
    {
        Dictionary<long, List<long>> byGrade = masterData.GetDrawableFellows()
            .GroupBy(fellow => fellow.Grade)
            .ToDictionary(group => group.Key, group => group.Select(fellow => fellow.FellowId).ToList());

        var grades = FellowRules.DrawGradeWeights.Where(entry => byGrade.ContainsKey(entry.Grade)).ToList();
        long grade = random.PickWeighted(grades.Select(entry => entry.Grade).ToList(), grades.Select(entry => entry.Weight).ToList());
        return random.Pick(byGrade[grade]);
    }

    /// <summary>
    /// Gives the dungeon setup's fellows exp and returns the stage result's fellow_info: always
    /// three elements in slot order, <c>{}</c> for an empty slot (the client applies those with a
    /// fellow number). Exp counts within the current level like the hero's;
    /// master_fellow_level fills the bar and master_fellow.max_level caps it.
    /// </summary>
    public IReadOnlyList<object> AwardDungeonExperience(long userNumber, long gain)
    {
        Dictionary<long, long> fellowInSlot = fellows.GetFilledSlots(userNumber)
            .Where(slot => slot.SetupType == FellowRules.DungeonSetupType)
            .ToDictionary(slot => slot.Position, slot => slot.FellowNumber);

        var awarded = new List<long?>();
        for (long position = 0; position < FellowRules.SlotsPerSetup; position++)
        {
            FellowProgressRecord? fellow = fellows.FindProgress(userNumber, fellowInSlot.GetValueOrDefault(position));
            if (fellow is null)
            {
                awarded.Add(null);
                continue;
            }

            Dictionary<long, long> experienceToNext = masterData.GetFellowExperienceTable(fellow.Grade);
            long level = fellow.Level;
            long experience = fellow.Experience + gain;
            while (level < fellow.MaxLevel && experienceToNext.TryGetValue(level, out long needed) && experience >= needed)
            {
                experience -= needed;
                level++;
            }

            if (level >= fellow.MaxLevel)
            {
                experience = 0;
            }

            fellows.SetProgress(fellow.FellowNumber, level, experience);
            awarded.Add(fellow.FellowNumber);
        }

        var infos = GetFellowInfos(userNumber, awarded.OfType<long>().ToHashSet())
            .ToDictionary(info => info.FellowNumber);
        return awarded
            .Select(number => number is { } n ? (object)infos[n] : EmptyObject.Instance)
            .ToList();
    }

    private static FellowInfo ToFellowInfo(FellowRecord fellow) => new()
    {
        FellowNumber = fellow.FellowNumber,
        FellowId = fellow.FellowId,
        Upgrade = fellow.Upgrade,
        Experience = fellow.Experience,
        Level = fellow.Level,
        MaxLevel = fellow.MaxLevel,
        FailCount = fellow.FailCount,
        AttackBonus = fellow.AttackBonus,
        Locked = fellow.Locked,
    };
}
