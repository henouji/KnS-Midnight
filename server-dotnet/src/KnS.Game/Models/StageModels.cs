using System.Text.Json.Serialization;

namespace KnS.Game.Models;

/// <summary>stage_attainment_info: an area's clear counts per mode (Net_SetStageAreaInfo).</summary>
public sealed record AreaAttainment(
    [property: JsonPropertyName("area_id")] long AreaId,
    [property: JsonPropertyName("normal_user_clear_count")] long NormalClears,
    [property: JsonPropertyName("hard_user_clear_count")] long HardClears);

/// <summary>A claimed area-clear reward (Net_SetStageAreaReward).</summary>
public sealed record AreaRewardClaim(
    [property: JsonPropertyName("area_id")] long AreaId,
    [property: JsonPropertyName("stage_mode")] long StageMode);

/// <summary>A cleared stage (SetStageClear).</summary>
public sealed record StageClear(
    [property: JsonPropertyName("stage_id")] long StageId,
    [property: JsonPropertyName("stage_mode")] long StageMode);
