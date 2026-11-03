using System.Text.Json.Serialization;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Players;

namespace KnS.Game.Protocols.Profile;

/// <summary>
/// Protocol 61: the item inventory (case @0x15bb7c). Its item numbers must match the ones in
/// protocol 60's hero_item_infos: the client resolves each equipped slot to an inventory node by
/// item number (GetItemNodeByUniqueItemID).
/// </summary>
public sealed class InventoryHandler(PlayerDirectory directory, InventoryService inventory, GameOptions options) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        Dictionary<long, long> wornBy = inventory.GetWornItems(userNumber);
        var items = new List<object>();
        if (!options.EmptyHeroList)
        {
            items.AddRange(inventory.GetStarterItems(userNumber, wornBy));
        }

        items.AddRange(inventory.GetOwnedItems(userNumber, wornBy));
        return ProtocolResult.Success(new InventoryResponse(items.Count, items));
    }
}

public sealed record InventoryResponse(
    [property: JsonPropertyName("inventory_count")] long ItemCount,
    [property: JsonPropertyName("item_infos")] IReadOnlyList<object> Items);
