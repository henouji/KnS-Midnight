using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Stages;

/// <summary>
/// Protocol 73: "No HP. Use 5 Gems to recover all HP" → Continue (case @0x1600c2, post-handler
/// @0x154cfc). On errno 0 the client revives the hero; otherwise the hero stays dead. The price
/// is the "Continue" row of master_store_price.
/// </summary>
public sealed class StageContinueHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    MasterDataRepository masterData,
    ILogger<StageContinueHandler> logger) : IProtocolHandler
{
    private const string ContinuePriceName = "Continue";

    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        Price price = masterData.GetStorePrice(ContinuePriceName)
            ?? throw new InvalidOperationException($"master_store_price has no {ContinuePriceName} row.");
        PlayerAccount account = profiles.GetAccount(userNumber);
        if (!account.CanAfford(price))
        {
            logger.LogInformation("Continue refused: {Price} of {Item} needed, have {Balance}",
                price.Amount, price.PriceItemId, account.BalanceOf(price.PriceItemId));
            return ProtocolResult.Refused("not enough gems");
        }

        profiles.Pay(userNumber, price);
        logger.LogInformation("Continue: stage {Stage}, paid {Price} of {Item}",
            request.Parameters["stage_no"]?.ToJsonString(), price.Amount, price.PriceItemId);
        return ProtocolResult.Success(new StageContinueResponse(profiles.GetGoods(userNumber)));
    }
}

public sealed record StageContinueResponse(
    [property: JsonPropertyName("update_item_etc_info")] IReadOnlyList<GoodsEntry> Goods)
{
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
}
