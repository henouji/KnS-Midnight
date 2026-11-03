using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace KnS.Tests.Integration;

[Collection(SharedServer.Name)]
public class GameGatewayTests(ServerFixture server)
{
    [Fact]
    public async Task Health_reports_every_check_ok()
    {
        using HttpResponseMessage response = await server.Client.GetAsync("/health");
        var report = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", report["status"]!.GetValue<string>());
        Assert.True(report["checks"]!["db"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Replies_are_flat_with_the_envelope_fields_first()
    {
        JsonObject reply = await server.CallGatewayAsync(51, new { hub_did = "tests-flat", hub_uid = "-1" });

        Assert.Equal(["protocol", "errno", "error", "user_no", "session_key"], reply.Select(p => p.Key).Take(5));
        Assert.Equal(51, reply["protocol"]!.GetValue<int>());
        Assert.Equal(0, reply["errno"]!.GetValue<int>());
    }

    [Fact]
    public async Task Login_issues_a_stable_user_number_per_device()
    {
        long first = (await server.CallGatewayAsync(51, new { hub_did = "tests-stable" }))["user_no"]!.GetValue<long>();
        long again = (await server.CallGatewayAsync(51, new { hub_did = "tests-stable" }))["user_no"]!.GetValue<long>();

        Assert.True(first > 10_000);
        Assert.Equal(first, again);
    }

    [Fact]
    public async Task A_new_player_loads_with_the_default_hero_wearing_the_starter_gear()
    {
        long user = (await server.CallGatewayAsync(51, new { hub_did = "tests-new-player" }))["user_no"]!.GetValue<long>();

        JsonObject loaded = await server.CallGatewayAsync(60, new { user_no = user }, user);
        JsonObject inventory = await server.CallGatewayAsync(61, new { user_no = user }, user);

        JsonNode hero = loaded["hero_info"]![0]!;
        Assert.Equal(1, hero["hero_pcid"]!.GetValue<long>());
        long[] worn = hero["hero_item_infos"]!.AsArray().Select(i => i!["item_no"]!.GetValue<long>()).ToArray();
        Assert.Equal([1001, 1003, 1002], worn);
        Assert.Equal(3, inventory["inventory_count"]!.GetValue<long>());
        Assert.Equal(99_999, loaded["user_info"]!["user_gold"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_cleared_stage_pays_its_gold_and_shows_on_the_world_map()
    {
        long user = (await server.CallGatewayAsync(51, new { hub_did = "tests-stage" }))["user_no"]!.GetValue<long>();
        await server.CallGatewayAsync(60, new { user_no = user }, user);

        JsonObject start = await server.CallGatewayAsync(71, new { stage_id = 3000001, stage_mode = 1 }, user);
        JsonArray monsters = start["stage_info"]!["monster_info_3000001"]!.AsArray();
        JsonObject result = await server.CallGatewayAsync(72, new
        {
            stage_no = 3000001,
            stage_mode = 1,
            stage_result = 1,
            reward_gold = 100,
            reward_items = Array.Empty<long>(),
            event_items = Array.Empty<long>(),
        }, user);
        JsonObject map = await server.CallGatewayAsync(70, new { user_no = user }, user);

        Assert.Equal(13, monsters.Count);
        Assert.Equal(47, monsters[0]!.AsObject().Count);
        Assert.True(result["user_info"]!["user_gold"]!.GetValue<long>() >= 99_999 + 100);
        Assert.Equal(3, result["fellow_info"]!.AsArray().Count);
        Assert.Contains(map["clear_stage_infos"]!.AsArray(), clear => clear!["stage_id"]!.GetValue<long>() == 3000001);
    }

    [Fact]
    public async Task The_traffic_log_audits_the_drops_a_stage_result_reports()
    {
        long user = (await server.CallGatewayAsync(51, new { hub_did = "tests-traffic" }))["user_no"]!.GetValue<long>();
        await server.CallGatewayAsync(71, new { stage_id = 3000001, stage_mode = 1 }, user);
        await server.CallGatewayAsync(72, new
        {
            stage_no = 3000001,
            stage_result = 1,
            reward_gold = 10,
            reward_items = new List<long> { 999_999 },
            event_items = Array.Empty<long>(),
            drop_fellow_list = new List<long> { 100211 },
        }, user);

        IReadOnlyList<JsonObject> lines = server.ReadTraffic().Where(line => line["user_no"]?.GetValue<long>() == user).ToList();
        JsonObject start = lines.Single(line => line["protocol"]!.GetValue<int>() == 71);
        JsonObject result = lines.Single(line => line["protocol"]!.GetValue<int>() == 72);

        Assert.Equal("StageStart", start["name"]!.GetValue<string>());
        Assert.NotNull(start["notes"]!["rolled"]);
        Assert.Equal(3000001, start["request"]!["params"]!["stage_id"]!.GetValue<long>());
        Assert.NotNull(start["reply"]!["drop_item_list"]);
        Assert.Equal([999_999L], result["notes"]!["drops"]!["not_offered"]!.AsArray().Select(n => n!.GetValue<long>()));
        Assert.Equal(["drop_fellow_list"], result["unread"]!.AsArray().Select(n => n!.GetValue<string>()));
        string[] warnings = result["warnings"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Contains(warnings, warning => warning.Contains("never offered: [999999]", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("drop_fellow_list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_request_answers_errno_1_without_payload()
    {
        long user = (await server.CallGatewayAsync(51, new { hub_did = "tests-refused" }))["user_no"]!.GetValue<long>();

        JsonObject reply = await server.CallGatewayAsync(201, new { hero_pcid = 99 }, user);

        Assert.Equal(["protocol", "errno", "error"], reply.Select(p => p.Key));
        Assert.Equal(1, reply["errno"]!.GetValue<int>());
        Assert.Equal("unknown hero", reply["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_unhandled_protocol_gets_a_bare_success()
    {
        JsonObject reply = await server.CallGatewayAsync(140, new { });

        Assert.Equal(["protocol", "errno", "error"], reply.Select(p => p.Key));
        Assert.Equal(0, reply["errno"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_body_that_does_not_decrypt_is_answered_with_errno_minus_1()
    {
        JsonObject reply = await server.PostGatewayAsync(Encoding.ASCII.GetBytes("not ciphertext"));

        Assert.Equal(-1, reply["errno"]!.GetValue<int>());
        Assert.Equal("decrypt failed", reply["error"]!.GetValue<string>());
    }
}
