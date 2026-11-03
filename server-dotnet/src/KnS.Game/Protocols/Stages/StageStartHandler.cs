using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Game.Stages;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Stages;

/// <summary>
/// Protocol 71: dungeon stage start (case @0x15e324). Request {friend_type, stage_id,
/// stage_mode, friend_user_no}; it carries no user_no.
/// </summary>
/// <remarks>
/// The reply must repeat user_info: the client runs UpdateUserInfo / UpdateUserGoods on every
/// stage reply, and a missing user_info copies zero gold and gems over the real balance.
/// </remarks>
public sealed class StageStartHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    MonsterInfoBuilder monsters,
    StageRewardService rewards,
    GatewayTrace trace,
    ILogger<StageStartHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long stageId = request.Parameters.GetInt64("stage_id");
        long stageMode = request.Parameters.GetInt64("stage_mode", 1);
        Player player = directory.ResolveCurrent(request);
        IReadOnlyList<JsonObject> monsterRows = monsters.Build(stageId, stageMode);

        UserInfo userInfo = profiles.BuildUserInfo(player);
        IReadOnlyList<GoodsEntry> goods = profiles.GetGoods(player.UserNumber);
        DropItemList drops = rewards.OfferDrops(player.UserNumber, stageId, stageMode);
        IReadOnlyList<EventDropOffer> eventDrops = rewards.OfferEventItems(player.UserNumber);
        logger.LogInformation("Stage {Stage} drops{Boss}: chests [{Chests}] boss [{BossDrops}], event items [{Events}]",
            stageId, drops.BossDrops.Count > 0 ? " (boss list)" : "",
            string.Join(",", drops.Chests.Select(d => d.ItemId)), string.Join(",", drops.BossDrops.Select(d => d.ItemId)),
            string.Join(",", eventDrops.Select(e => $"{e.ItemId}x{e.Count}")));
        // Every slot the roll hit; the fellows among them are granted by the result, not offered here.
        trace.Notes["rolled"] = StageDropAudit.DescribeRolled(rewards.GetPendingDrops(player.UserNumber).Rolled);

        return ProtocolResult.Success(new StageStartResponse
        {
            UserInfo = userInfo,
            Goods = goods,
            Stage = new StageSetup
            {
                StageNumber = stageId,
                // The client reads monster_info_<N> and <N+1>, N = the selected stage's id.
                MonsterInfo = new Dictionary<string, object>
                {
                    [$"monster_info_{stageId}"] = monsterRows,
                    [$"monster_info_{stageId + 1}"] = monsterRows,
                },
            },
            Drops = drops,
            EventDrops = eventDrops,
        });
    }
}

public sealed record StageStartResponse
{
    [JsonPropertyName("user_info")] public required UserInfo UserInfo { get; init; }
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];
    [JsonPropertyName("stage_info")] public required StageSetup Stage { get; init; }
    [JsonPropertyName("verify_info")] public VerifyInfo Verify { get; init; } = new();
    [JsonPropertyName("drop_item_list")] public required DropItemList Drops { get; init; }

    /// <summary>
    /// Two slots: the client flags <see cref="EventDropOffer.Count"/> random monsters per slot,
    /// which drop the slot's item when they die.
    /// </summary>
    [JsonPropertyName("drop_event_item_list")] public IReadOnlyList<EventDropOffer> EventDrops { get; init; } = [];
}

public sealed record StageSetup
{
    [JsonPropertyName("stage_no")] public long StageNumber { get; init; }
    [JsonPropertyName("rand_key")] public long RandomKey { get; init; } = 1;
    [JsonPropertyName("drop_count")] public long GoldPileCount { get; init; } = DropRules.GoldPileCount;
    [JsonPropertyName("drop_gold_max")] public long GoldPileMax { get; init; } = DropRules.GoldPileMax;
    [JsonPropertyName("gold_gain_bonus")] public long GoldGainBonus { get; init; }

    /// <summary>monster_info_&lt;stage&gt; keys: the stage's 13 monster stat rows.</summary>
    [JsonExtensionData] public Dictionary<string, object> MonsterInfo { get; init; } = [];
}

public sealed record VerifyInfo
{
    [JsonPropertyName("verify_type")] public long VerifyType { get; init; }
    [JsonPropertyName("attack")] public long Attack { get; init; } = PlayerRules.HeroAttack;
    [JsonPropertyName("defence")] public long Defence { get; init; } = PlayerRules.HeroDefence;
    [JsonPropertyName("hp")] public long Hp { get; init; } = PlayerRules.HeroHp;
}
