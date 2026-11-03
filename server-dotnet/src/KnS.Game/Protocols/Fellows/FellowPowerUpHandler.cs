using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Fellows;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Randomness;
using KnS.Game.Rules;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Fellows;

/// <summary>
/// Protocol 303: ally power-up (request case @0x14e878, parser @0x160290, post-handler @0x154042). The material ally is used up;
/// the roll, the cost and the Power-up Points follow DrawFellowUpgrade @0xa8158 (docs/rebuild-fellows.md "Power-up and Enhance").
/// The client deletes the material from its own list when the animation ends, so the reply does not list it.
/// </summary>
public sealed class FellowPowerUpHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    FellowService fellowService,
    FellowRepository fellows,
    MasterDataRepository masterData,
    IRandomSource random,
    ILogger<FellowPowerUpHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long targetNumber = request.Parameters.GetInt64("fellow_no");
        long materialNumber = request.Parameters.GetInt64("used_fellow_no");

        FellowUpgradeRecord? target = fellows.FindForUpgrade(userNumber, targetNumber);
        FellowUpgradeRecord? material = fellows.FindForUpgrade(userNumber, materialNumber);
        HashSet<long> slotted = fellows.GetFilledSlots(userNumber).Select(slot => slot.FellowNumber).ToHashSet();
        FellowUpgradeCosts? costs = target is null
            ? null
            : masterData.GetFellowUpgradeCosts(target.Grade, Math.Min(target.Upgrade + 1, FellowRules.MaxUpgrade));
        long cost = Math.Max(0, costs?.PowerUpGold ?? 0);

        string? refusal = null;
        if (target is null || material is null || costs is null || targetNumber == materialNumber
            || FellowRules.IsAngel(target.FellowId) || material.Locked != 0 || slotted.Contains(materialNumber))
        {
            refusal = "invalid power up";
        }
        else if (profiles.GetAccount(userNumber).Gold < cost)
        {
            refusal = "cannot power up";
        }

        if (refusal is not null)
        {
            logger.LogInformation("Fellow power up refused: {Reason} (fellow_no {Target}, used {Material})", refusal, targetNumber, materialNumber);
            return ProtocolResult.Refused(refusal);
        }

        long gap = Math.Max(0, target!.Grade - material!.Grade);
        FellowPowerUpRate? row = masterData.GetFellowPowerUpRates().FirstOrDefault(rate => rate.GradeGap == gap);
        long percent = FellowRules.PowerUpPercent(target.Grade, material.Grade, target.Upgrade, target.FailCount, row?.SuccessPercent ?? 0);
        bool success = random.NextBelow(100) < percent;

        long upgrade = success ? Math.Min(FellowRules.MaxUpgrade, target.Upgrade + 1) : target.Upgrade;
        long failCount = success ? 0 : target.FailCount + (row?.FailPoints ?? 0);
        long bonus = Math.Min(FellowRules.AttackBonusCap, target.AttackBonus + material.AttackBonus);
        fellows.SetUpgradeState(targetNumber, upgrade, failCount, bonus);
        fellows.Delete(materialNumber);
        profiles.AddCurrency(userNumber, gold: -cost);

        logger.LogInformation("Fellow power up: fellow_no {Target} {Result} at {Percent}% ({Gold} G), now +{Upgrade}, points {Points}, used {Material}",
            targetNumber, success ? "succeeded" : "failed", percent, cost, upgrade, failCount, materialNumber);
        return ProtocolResult.Success(new FellowPowerUpResponse
        {
            Goods = profiles.GetGoods(userNumber),
            Fellow = fellowService.GetFellowInfos(userNumber, new HashSet<long> { targetNumber }).Single(),
            UsedFellowNumber = materialNumber,
            UpgradeResult = success ? 1 : 0,
        });
    }
}

public sealed record FellowPowerUpResponse
{
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];

    /// <summary>The target after the power-up: one object, not a list (the parser calls ParsingDataToFellowInfo directly).</summary>
    [JsonPropertyName("fellow_info")] public FellowInfo Fellow { get; init; } = new();

    [JsonPropertyName("used_fellow_no")] public long UsedFellowNumber { get; init; }

    /// <summary>1 = the power-up worked, 0 = it failed (the failure art and the Power-up Points message).</summary>
    [JsonPropertyName("upgrade_result")] public long UpgradeResult { get; init; }

    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
    [JsonPropertyName("unequip_fellow_item_no_list")] public IReadOnlyList<long> UnequippedFellowItems { get; init; } = [];
}
