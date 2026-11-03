using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Access;
using KnS.Game.Gateway;
using KnS.Game.Players;
using KnS.Game.Rules;
using KnS.Protocol.Json;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Fellows;

/// <summary>
/// Protocol 302: puts fellows into a setup's slots (case @0x160146, post-handler @0x153ff2).
/// </summary>
/// <remarks>
/// Request {type, fellows: [{fellow_no, fellow_position}] x3}: the whole setup after the change,
/// fellow_no 0 = empty. The client applies the reply, not its request, so a bare reply empties
/// the slots. Fellows the player does not own are left out. The Ally screen sends type 3.
/// </remarks>
public sealed class FellowSetHandler(
    PlayerDirectory directory,
    FellowRepository fellows,
    ILogger<FellowSetHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long setupType = request.Parameters.GetInt64("type");
        HashSet<long> owned = fellows.GetOwnedFellowNumbers(userNumber);
        var slots = new long[FellowRules.SlotsPerSetup];

        foreach (JsonObject entry in request.Parameters.GetList("fellows").OfType<JsonObject>())
        {
            long position = entry.ContainsKey("fellow_position") ? LenientJson.ToInt64(entry["fellow_position"]) : -1;
            long fellowNumber = LenientJson.IsSet(entry["fellow_no"]) ? LenientJson.ToInt64(entry["fellow_no"]) : 0;
            if (position >= 0 && position < slots.Length && (fellowNumber == 0 || owned.Contains(fellowNumber)))
            {
                slots[position] = fellowNumber;
            }
        }

        for (int position = 0; position < slots.Length; position++)
        {
            fellows.SetSlot(userNumber, setupType, position, slots[position]);
        }

        logger.LogInformation("Fellow set: type {Type} -> [{Slots}]", setupType, string.Join(",", slots));
        return ProtocolResult.Success(new FellowSetResponse(
            setupType,
            slots.Select((fellowNumber, position) => new FellowPosition(position, fellowNumber)).ToList()));
    }
}

public sealed record FellowSetResponse(
    [property: JsonPropertyName("type")] long SetupType,
    [property: JsonPropertyName("fellows")] IReadOnlyList<FellowPosition> Slots);

public sealed record FellowPosition(
    [property: JsonPropertyName("fellow_position")] long Position,
    [property: JsonPropertyName("fellow_no")] long FellowNumber);
