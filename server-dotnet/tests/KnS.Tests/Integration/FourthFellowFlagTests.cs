using System.Text.Json.Nodes;

namespace KnS.Tests.Integration;

/// <summary>The fourth_fellow row of feature_flag: off, a flagged request gets the original three positions.</summary>
[Collection(SharedServer.Name)]
public class FourthFellowFlagTests(ServerFixture server)
{
    [Fact]
    public async Task The_schema_seeds_the_flag_on_with_both_dates()
    {
        Assert.Equal(1, server.Scalar("SELECT enabled FROM feature_flag WHERE feature_key='fourth_fellow'"));
        Assert.True(server.Scalar("SELECT created_date FROM feature_flag WHERE feature_key='fourth_fellow'") > 0);
        Assert.True(server.Scalar("SELECT update_date FROM feature_flag WHERE feature_key='fourth_fellow'") > 0);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_flagged_request_is_answered_with_three_dungeon_positions_while_the_flag_is_off()
    {
        long user = (await server.CallGatewayAsync(51, new { hub_did = "flag-off" }))["user_no"]!.GetValue<long>();
        await server.CallGatewayAsync(60, new { user_no = user }, user);
        JsonObject drawn = await server.CallGatewayAsync(92, new { package_id = 9_710_005, use_ticket = 0, group_num = 1, achievement_list = Array.Empty<long>() }, user);
        long[] f = drawn["fellow_info"]!.AsArray().Select(x => x!["fellow_no"]!.GetValue<long>()).ToArray();
        object Setup() => new
        {
            type = 3,
            party_slots = 4,
            fellows = f.Take(4).Select((number, position) => new { fellow_no = number, fellow_position = position }).ToArray(),
        };

        server.Execute("UPDATE feature_flag SET enabled=0 WHERE feature_key='fourth_fellow'");
        try
        {
            JsonObject off = await server.CallGatewayAsync(302, Setup(), user);
            Assert.Equal(3, off["fellows"]!.AsArray().Count);
        }
        finally
        {
            server.Execute("UPDATE feature_flag SET enabled=1 WHERE feature_key='fourth_fellow'");
        }

        JsonObject on = await server.CallGatewayAsync(302, Setup(), user);
        Assert.Equal(4, on["fellows"]!.AsArray().Count);
    }
}
