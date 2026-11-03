using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Fellows;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Fellows;

/// <summary>
/// Protocol 307: ally enhance (request case @0x14e578, parser @0x15d28a, post-handler @0x158108). Two allies of the same grade, both
/// at +5 and max level, become one new ally of the next grade. It always succeeds. The original server picked the new ally by a rule
/// that is not in the client; this one draws a uniform ally of grade + 1 (docs/rebuild-fellows.md "What the server should do").
/// </summary>
public sealed class FellowEnhanceHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    FellowService fellowService,
    FellowRepository fellows,
    MasterDataRepository masterData,
    ILogger<FellowEnhanceHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        List<long> used = request.Parameters.GetList("used_fellows").Select(LenientJson.ToInt64).ToList();
        long firstNumber = used.ElementAtOrDefault(0);
        long secondNumber = used.ElementAtOrDefault(1);

        FellowUpgradeRecord? first = fellows.FindForUpgrade(userNumber, firstNumber);
        FellowUpgradeRecord? second = fellows.FindForUpgrade(userNumber, secondNumber);
        HashSet<long> slotted = fellows.GetFilledSlots(userNumber).Select(slot => slot.FellowNumber).ToHashSet();
        long cost = first is null ? 0 : Math.Max(0, masterData.GetFellowUpgradeCosts(first.Grade, FellowRules.MaxUpgrade)?.EnhanceGold ?? 0);

        string? refusal = null;
        if (used.Count != 2 || first is null || second is null || firstNumber == secondNumber || first.Grade != second.Grade
            || FellowRules.IsAngel(first.FellowId) || FellowRules.IsAngel(second.FellowId)
            || slotted.Contains(firstNumber) || slotted.Contains(secondNumber) || first.Locked != 0 || second.Locked != 0)
        {
            refusal = "invalid enhance";
        }
        else if (first.Grade >= FellowRules.MaxGrade || !IsReady(first) || !IsReady(second) || profiles.GetAccount(userNumber).Gold < cost)
        {
            refusal = "cannot enhance";
        }

        long? newFellowId = refusal is null ? fellowService.PickDrawableOfGrade(first!.Grade + 1) : null;
        if (refusal is null && newFellowId is null)
        {
            refusal = "no ally to enhance into";
        }

        if (refusal is not null)
        {
            logger.LogInformation("Fellow enhance refused: {Reason} (used {First}, {Second})", refusal, firstNumber, secondNumber);
            return ProtocolResult.Refused(refusal);
        }

        long bonus = Math.Min(FellowRules.AttackBonusCap, first!.AttackBonus + second!.AttackBonus);
        fellows.Delete(firstNumber);
        fellows.Delete(secondNumber);
        long newNumber = fellowService.Grant(userNumber, newFellowId!.Value);
        fellows.SetUpgradeState(newNumber, upgrade: 0, failCount: 0, attackBonus: bonus);
        profiles.AddCurrency(userNumber, gold: -cost);

        logger.LogInformation("Fellow enhance: {First} + {Second} -> fellow_id {NewId} as fellow_no {NewNumber} ({Gold} G)",
            firstNumber, secondNumber, newFellowId, newNumber, cost);
        return ProtocolResult.Success(new FellowEnhanceResponse
        {
            Goods = profiles.GetGoods(userNumber),
            Fellow = fellowService.GetFellowInfos(userNumber, new HashSet<long> { newNumber }).Single(),
            UsedFellows = [firstNumber, secondNumber],
            FellowBook = fellowService.GetBook(userNumber, new HashSet<long> { newFellowId.Value }),
        });
    }

    private static bool IsReady(FellowUpgradeRecord fellow) =>
        fellow.Upgrade >= FellowRules.MaxUpgrade && fellow.Level >= fellow.MaxLevel;
}

public sealed record FellowEnhanceResponse
{
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];

    /// <summary>The new ally: one object (ParsingDataToFellowInfo directly); the client opens its detail panel.</summary>
    [JsonPropertyName("fellow_info")] public FellowInfo Fellow { get; init; } = new();

    /// <summary>The two allies that were used up; the client deletes them from its list when the animation ends.</summary>
    [JsonPropertyName("used_fellows")] public IReadOnlyList<long> UsedFellows { get; init; } = [];

    [JsonPropertyName("fellow_book_info")] public IReadOnlyList<FellowBookEntry> FellowBook { get; init; } = [];
}
