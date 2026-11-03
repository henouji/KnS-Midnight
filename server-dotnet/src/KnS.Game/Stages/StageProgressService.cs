using KnS.Data.Repositories;
using KnS.Game.Models;

namespace KnS.Game.Stages;

/// <summary>Cleared stages and the per-area clear counts.</summary>
public sealed class StageProgressService(StageRepository stages, MasterDataRepository masterData)
{
    public long? GetAreaOfStage(long stageId) => masterData.GetAreaOfStage(stageId);

    public void RecordClear(long userNumber, long stageId, long stageMode) =>
        stages.RecordClear(userNumber, stageId, stageMode);

    public IReadOnlyList<StageClear> GetClears(long userNumber) =>
        stages.GetClears(userNumber).Select(clear => new StageClear(clear.StageId, clear.StageMode)).ToList();

    /// <summary>
    /// The clear counts per mode of every area with a clear, by area id. Repeats count: an area
    /// has 6-7 stages but its "Clears x/30" bar needs 15 / 30 (master_stage_attainment).
    /// </summary>
    public IReadOnlyList<AreaAttainment> GetAttainments(long userNumber) =>
        stages.GetAreaClearCounts(userNumber)
            .Select(area => new AreaAttainment(area.AreaId, area.NormalClears, area.HardClears))
            .ToList();

    public AreaAttainment? GetAttainment(long userNumber, long areaId) =>
        GetAttainments(userNumber).FirstOrDefault(area => area.AreaId == areaId);

    public IReadOnlyList<AreaRewardClaim> GetClaimedAreaRewards(long userNumber) =>
        stages.GetClaimedAreaRewards(userNumber).Select(claim => new AreaRewardClaim(claim.AreaId, claim.StageMode)).ToList();
}
