using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;
using KnS.Game.Players;
using KnS.Protocol;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Items;

/// <summary>
/// Protocol 84: locks or unlocks an item (case @0x15e182 reads only <c>item_no</c> and <c>item_lock</c>; NetworkEvent has no
/// post-handler for it). Locked items are skipped by the client's sell and material pickers and refused by the sell handler.
/// The starter gear has no row to lock: it is echoed unlocked.
/// </summary>
public sealed class LockItemHandler(
    PlayerDirectory directory,
    ItemRepository items,
    ILogger<LockItemHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long itemNumber = request.Parameters.GetInt64("item_no");
        bool wanted = request.Parameters.GetInt64("item_lock") != 0;

        bool locked = items.SetLocked(userNumber, itemNumber, wanted) && wanted;
        logger.LogInformation("Lock: item_no {ItemNumber} lock {Wanted} -> {Locked}", itemNumber, wanted, locked);
        return ProtocolResult.Success(new LockItemResponse(itemNumber, locked ? 1 : 0));
    }
}

public sealed record LockItemResponse(
    [property: JsonPropertyName("item_no")] long ItemNumber,
    [property: JsonPropertyName("item_lock")] long Locked);
