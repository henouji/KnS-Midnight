using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Game.Stages;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Stages;

/// <summary>
/// Protocol 74: claims an area-clear reward, the gem icon next to "Clears x/30" on the Explore
/// screen (parser case @0x15fc1a, post-handler @0x158384). The client offers it once the area's
/// clear count reaches master_stage_attainment.&lt;mode&gt;_clears and it is not claimed yet.
/// </summary>
public sealed class AreaRewardHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    StageProgressService progress,
    StageRepository stages,
    GoodsRepository goods,
    MasterDataRepository masterData,
    ILogger<AreaRewardHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long areaId = request.Parameters.GetInt64("area_id");
        long stageMode = request.Parameters.GetInt64("stage_mode", 1) == 1 ? 1 : 2;
        bool hardMode = stageMode == 2;

        AreaRewardRule? reward = masterData.GetAreaReward(areaId, hardMode);
        bool alreadyClaimed = stages.HasClaimedAreaReward(userNumber, areaId, stageMode);
        AreaAttainment? attainment = progress.GetAttainment(userNumber, areaId);
        long clears = hardMode ? attainment?.HardClears ?? 0 : attainment?.NormalClears ?? 0;
        if (reward is null || alreadyClaimed || clears < reward.ClearsNeeded)
        {
            logger.LogInformation("Area reward refused: area {Area} mode {Mode} (clears {Clears}, row {Row}, claimed {Claimed})",
                areaId, stageMode, clears, reward, alreadyClaimed);
            return ProtocolResult.Refused("cannot claim");
        }

        stages.RecordAreaRewardClaim(userNumber, areaId, stageMode);
        if (reward.RewardItemId is CurrencyItems.Gold or CurrencyItems.Gems)
        {
            profiles.AddCurrency(
                userNumber,
                gold: reward.RewardItemId == CurrencyItems.Gold ? reward.RewardCount : 0,
                gems: reward.RewardItemId == CurrencyItems.Gems ? reward.RewardCount : 0);
        }
        else
        {
            goods.Add(userNumber, reward.RewardItemId, reward.RewardCount);
        }

        logger.LogInformation("Area reward: area {Area} mode {Mode} -> {Count} x {Item}",
            areaId, stageMode, reward.RewardCount, reward.RewardItemId);
        return ProtocolResult.Success(new AreaRewardResponse(profiles.GetGoods(userNumber), new AreaRewardClaim(areaId, stageMode)));
    }
}

/// <param name="Goods">The goods after the reward.</param>
/// <param name="Claim">Net_SetStageAreaReward, then a "received" message box.</param>
public sealed record AreaRewardResponse(
    [property: JsonPropertyName("update_item_etc_info")] IReadOnlyList<GoodsEntry> Goods,
    [property: JsonPropertyName("stage_attainment_reward_info")] AreaRewardClaim Claim);
