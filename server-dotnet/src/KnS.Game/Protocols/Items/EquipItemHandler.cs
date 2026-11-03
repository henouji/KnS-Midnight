using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Protocol;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Items;

/// <summary>
/// Protocol 80: equips an item on a hero (case @0x15e1de). The client runs ChangeEquip with the
/// hero_no and item_no read back from the reply, so the reply must echo them.
/// </summary>
public sealed class EquipItemHandler(
    PlayerDirectory directory,
    ItemRepository items,
    HeroRepository heroes,
    ILogger<EquipItemHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        RequestParameters parameters = request.Parameters;
        long userNumber = parameters.IsSet("user_no")
            ? parameters.GetInt64("user_no")
            : directory.ResolveCurrent(request).UserNumber;
        long heroNumber = parameters.GetInt64("hero_no", PlayerRules.DefaultHeroNumber);
        long itemNumber = parameters.GetInt64("item_no");

        long? itemId = StarterGear.FindByItemNumber(itemNumber)?.ItemId ?? items.FindOwned(userNumber, itemNumber)?.ItemId;
        if (itemId is { } id)
        {
            heroes.UnequipFromOtherHeroes(userNumber, itemNumber, heroNumber);
            heroes.Equip(userNumber, heroNumber, EquipmentRanges.CategoryOf(id), itemNumber);
        }

        logger.LogInformation("Equip: hero {Hero} item_no {ItemNumber} item_id {ItemId}", heroNumber, itemNumber, itemId);
        return ProtocolResult.Success(new EquipItemResponse(heroNumber, itemNumber));
    }
}

public sealed record EquipItemResponse(
    [property: JsonPropertyName("hero_no")] long HeroNumber,
    [property: JsonPropertyName("item_no")] long ItemNumber)
{
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
}
