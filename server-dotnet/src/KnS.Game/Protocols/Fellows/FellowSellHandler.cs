using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Fellows;

/// <summary>
/// Protocol 306: sells fellows (case @0x15d1ba, post-handler @0x1540a0) for the price the client
/// shows (GetFellowSellCostByNo@0xf9318: master_fellow_upgrade.sell_price by grade and upgrade).
/// Locked fellows and fellows in an active slot are refused.
/// </summary>
public sealed class FellowSellHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    FellowRepository fellows,
    ILogger<FellowSellHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        HashSet<long> active = fellows.GetFilledSlots(userNumber).Select(slot => slot.FellowNumber).ToHashSet();
        var sold = new List<long>();
        var failed = new List<long>();
        long earned = 0;

        foreach (long fellowNumber in request.Parameters.GetList("sell_fellows").Select(LenientJson.ToInt64))
        {
            FellowSaleRecord? sale = fellows.FindSaleInfo(userNumber, fellowNumber);
            if (sale is null || sale.Locked != 0 || active.Contains(fellowNumber))
            {
                failed.Add(fellowNumber);
                continue;
            }

            fellows.Delete(fellowNumber);
            earned += sale.SellPrice;
            sold.Add(fellowNumber);
        }

        profiles.AddCurrency(userNumber, gold: earned);
        logger.LogInformation("Fellow sell: sold [{Sold}] for {Gold} G, failed [{Failed}]",
            string.Join(",", sold), earned, string.Join(",", failed));
        return ProtocolResult.Success(new FellowSellResponse(profiles.GetGoods(userNumber), sold, failed));
    }
}

public sealed record FellowSellResponse(
    [property: JsonPropertyName("update_item_etc_info")] IReadOnlyList<GoodsEntry> Goods,
    [property: JsonPropertyName("success_list")] IReadOnlyList<long> Sold,
    [property: JsonPropertyName("fail_list")] IReadOnlyList<long> Failed)
{
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];

    /// <summary>Fellow items freed by the sale (fellows carry none yet).</summary>
    [JsonPropertyName("unequip_fellow_item_no_list")] public IReadOnlyList<long> UnequippedFellowItems { get; init; } = [];
}
