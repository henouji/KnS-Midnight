using System.Text.Json.Serialization;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Stages;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Stages;

/// <summary>
/// Protocol 70: the player's stage progress, requested when the world map opens (case @0x15abca).
/// The client wipes its stage-clear bits and area counts and rebuilds them from this reply, so a
/// bare reply would forget every cleared stage.
/// </summary>
public sealed class StageInfoHandler(
    PlayerDirectory directory,
    StageProgressService progress,
    ILogger<StageInfoHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        IReadOnlyList<StageClear> clears = progress.GetClears(userNumber);
        IReadOnlyList<AreaRewardClaim> claimed = progress.GetClaimedAreaRewards(userNumber);
        logger.LogInformation("Stage info: {Count} cleared stages", clears.Count);
        return ProtocolResult.Success(new StageInfoResponse(clears, progress.GetAttainments(userNumber), claimed));
    }
}

/// <param name="Clears">SetStageClear(id, mode != 1) for each.</param>
/// <param name="Attainments">Net_SetStageAreaInfo for each.</param>
/// <param name="ClaimedAreaRewards">Net_SetStageAreaReward for each.</param>
public sealed record StageInfoResponse(
    [property: JsonPropertyName("clear_stage_infos")] IReadOnlyList<StageClear> Clears,
    [property: JsonPropertyName("stage_attainment_infos")] IReadOnlyList<AreaAttainment> Attainments,
    [property: JsonPropertyName("stage_attainment_reward_infos")] IReadOnlyList<AreaRewardClaim> ClaimedAreaRewards);
