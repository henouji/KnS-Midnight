using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Fellows;
using KnS.Game.Access;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Game.Stages;
using KnS.Protocol;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Stages;

/// <summary>
/// Protocol 72: dungeon stage result (case @0x15fcd0). Pays the gold the client counted, claims
/// the picked-up chests (reward_items) and event items (event_items), and on a clear awards exp,
/// fellow exp and the rolled fellows.
/// </summary>
public sealed class StageResultHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    StageRewardService rewards,
    StageProgressService progress,
    FellowService fellows,
    GatewayTrace trace,
    ILogger<StageResultHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        RequestParameters parameters = request.Parameters;
        Player player = directory.ResolveCurrent(request);
        long userNumber = player.UserNumber;
        PendingDrops offered = rewards.GetPendingDrops(userNumber);

        // The gold piles the client counted; a value that is not a number pays nothing.
        profiles.AddCurrency(userNumber, gold: Math.Max(0, parameters.GetInt64OrFallback("reward_gold")));

        logger.LogInformation("Stage result: reward_gold={Gold}, gold now {Balance}; reward_items={Rewards} event_items={Events} max_damage={MaxDamage}",
            parameters["reward_gold"]?.ToJsonString(), profiles.GetAccount(userNumber).Gold,
            parameters["reward_items"]?.ToJsonString() ?? "null", parameters["event_items"]?.ToJsonString() ?? "null",
            parameters["max_damage"]?.ToJsonString());

        IReadOnlyList<JsonNode?> rewardItems = parameters.GetList("reward_items");
        List<ItemInfo> claimedItems = rewards.ClaimItems(userNumber, rewardItems);
        IEnumerable<long> reportedNumbers = parameters.GetList("event_items").Concat(rewardItems)
            .Select(node => LenientJson.IsInteger(node, out long number) ? number : (long?)null)
            .OfType<long>();
        IReadOnlyList<EventOfferRecord> credited = rewards.ClaimEventOffers(userNumber, reportedNumbers.ToList());
        IReadOnlyList<object> chests = StageRewardService.OrderChests(rewardItems, claimedItems, credited);
        logger.LogInformation("Stage result: claimed items [{Items}], credited offers [{Offers}]",
            string.Join(",", claimedItems.Select(i => i.ItemNumber)), string.Join(",", credited.Select(o => $"{o.ItemId}x{o.Count}")));

        long stageId = parameters.GetInt64("stage_no");
        JsonNode? stageResult = parameters.CopyOrDefault("stage_result", 1);
        IReadOnlyList<object> fellowExperience = Enumerable.Repeat<object>(EmptyObject.Instance, FellowRules.SlotsPerSetup).ToList();
        IReadOnlyList<long> droppedFellowNumbers = [];
        bool cleared = !parameters.Contains("stage_result") || LenientJson.NumericallyEquals(parameters["stage_result"], 1);
        if (cleared)
        {
            long gain = profiles.AwardStageExperience(userNumber, stageId);
            PlayerAccount account = profiles.GetAccount(userNumber);
            logger.LogInformation("Stage result: +{Gain} exp -> Lv.{Level} {Exp}/{MaxExp}", gain, account.Level, account.Experience, account.MaxExperience);
            fellowExperience = fellows.AwardDungeonExperience(userNumber, gain);
            droppedFellowNumbers = rewards.GrantDroppedFellows(userNumber);
            progress.RecordClear(userNumber, stageId, parameters.GetInt64("stage_mode", 1));
        }

        IReadOnlyList<FellowInfo> droppedFellows = droppedFellowNumbers.Count > 0
            ? fellows.GetFellowInfos(userNumber, droppedFellowNumbers.ToHashSet())
            : [];
        if (droppedFellows.Count > 0)
        {
            logger.LogInformation("Stage result: dropped fellows [{Fellows}]", string.Join(",", droppedFellows.Select(f => f.FellowId)));
        }

        trace.Notes["drops"] = StageDropAudit.Build(
            offered, rewardItems, parameters.GetList("event_items"), claimedItems, credited, cleared,
            droppedFellows.Select(f => f.FellowId).ToList(), parameters.UnreadNames, trace.Warnings);

        long? areaId = progress.GetAreaOfStage(stageId);
        object attainment = areaId is { } area && area != 0
            ? progress.GetAttainment(userNumber, area) ?? (object)EmptyObject.Instance
            : EmptyObject.Instance;

        return ProtocolResult.Success(new StageResultResponse
        {
            StageResult = stageResult,
            UserInfo = profiles.BuildUserInfo(player),
            Goods = profiles.GetGoods(userNumber),
            Chests = chests,
            FellowExperience = fellowExperience,
            DroppedFellows = droppedFellows,
            FellowBook = fellows.GetBook(userNumber, droppedFellows.Select(f => f.FellowId).ToHashSet()),
            AreaAttainment = attainment,
        });
    }
}

public sealed record StageResultResponse
{
    [JsonPropertyName("stage_result")] public JsonNode? StageResult { get; init; }
    [JsonPropertyName("user_info")] public required UserInfo UserInfo { get; init; }
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];

    /// <summary>The result screen's drops (one chest each): <see cref="ItemInfo"/> or <see cref="CountedItemInfo"/>.</summary>
    [JsonPropertyName("item_infos")] public IReadOnlyList<object> Chests { get; init; } = [];

    /// <summary>The dungeon setup's three slots after the exp: <see cref="FellowInfo"/> or {} for an empty slot.</summary>
    [JsonPropertyName("fellow_info")] public IReadOnlyList<object> FellowExperience { get; init; } = [];

    [JsonPropertyName("drop_fellow_infos")] public IReadOnlyList<FellowInfo> DroppedFellows { get; init; } = [];
    [JsonPropertyName("fellow_book_info")] public IReadOnlyList<FellowBookEntry> FellowBook { get; init; } = [];

    /// <summary>The stage's area counts (<see cref="AreaAttainment"/>), which replace the client's; {} when none.</summary>
    [JsonPropertyName("stage_attainment_info")] public object AreaAttainment { get; init; } = EmptyObject.Instance;
}
