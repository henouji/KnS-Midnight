using System.Text.Json.Nodes;

namespace KnS.Tests.Integration;

/// <summary>
/// Ally power-up (303) and enhance (307), docs/rebuild-fellows.md "Power-up and Enhance". The replies are what the original client's parsers
/// read: one <c>fellow_info</c> object (not a list), <c>used_fellow_no</c> / <c>upgrade_result</c> for 303, <c>used_fellows</c> for 307.
/// </summary>
[Collection(SharedServer.Name)]
public class FellowUpgradeTests(ServerFixture server)
{
    private const long FellowOffset = 7_000;
    private const long Grade1 = 100001, OtherGrade1 = 100011, Grade2 = 100141, Grade4 = 100002, Grade6 = 100211;

    private async Task<long> NewPlayerAsync(string device, long gold = 90_000)
    {
        long user = (await server.CallGatewayAsync(51, new { hub_did = device }))["user_no"]!.GetValue<long>();
        await server.CallGatewayAsync(60, new { user_no = user }, user);
        server.Execute($"UPDATE gameuser SET gold={gold} WHERE user_no={user}");
        return user;
    }

    private long Grant(long user, long fellowId, long level = 1, long upgrade = 0, long bonus = 0, long failCount = 0, long locked = 0)
    {
        server.Execute(
            "INSERT INTO fellow(user_no, fellow_id, level, upgrade, attack_bonus, fail_count, locked)"
            + $" VALUES({user}, {fellowId}, {level}, {upgrade}, {bonus}, {failCount}, {locked})");
        return server.Scalar("SELECT MAX(rowid_alias) FROM fellow") + FellowOffset;
    }

    private long Gold(long user) => server.Scalar($"SELECT gold FROM gameuser WHERE user_no={user}");

    private long Owned(long fellowNumber) => server.Scalar($"SELECT COUNT(*) FROM fellow WHERE rowid_alias={fellowNumber - FellowOffset}");

    private Task<JsonObject> PowerUpAsync(long user, long target, long material) =>
        server.CallGatewayAsync(303, new { fellow_no = target, used_fellow_no = material }, user);

    private Task<JsonObject> EnhanceAsync(long user, params long[] used) =>
        server.CallGatewayAsync(307, new { used_fellows = used, group_num = 1 }, user);

    private static bool Refused(JsonObject reply) => reply["errno"]?.GetValue<long>() is not (null or 0);

    [Fact]
    public async Task A_power_up_with_a_higher_grade_material_always_works_charges_the_row_gold_and_uses_the_material()
    {
        long user = await NewPlayerAsync("up-sure");
        long target = Grant(user, Grade1, upgrade: 2);
        long material = Grant(user, Grade2);

        JsonObject reply = await PowerUpAsync(user, target, material);

        Assert.Equal(1, reply["upgrade_result"]!.GetValue<long>());
        Assert.Equal(material, reply["used_fellow_no"]!.GetValue<long>());
        Assert.IsType<JsonObject>(reply["fellow_info"]);
        Assert.Equal(target, reply["fellow_info"]!["fellow_no"]!.GetValue<long>());
        Assert.Equal(3, reply["fellow_info"]!["fellow_upgrade"]!.GetValue<long>());
        Assert.Equal(0, Owned(material));
        Assert.Equal(90_000 - 1300, Gold(user)); // +2 -> +3 costs c2 of master_fellow_upgrade (1, 3)
    }

    [Fact]
    public async Task A_power_up_costs_the_c2_of_the_next_upgrade_row()
    {
        long user = await NewPlayerAsync("up-cost");
        long target = Grant(user, Grade1, upgrade: 0);
        long material = Grant(user, Grade2);

        await PowerUpAsync(user, target, material);

        Assert.Equal(90_000 - 500, Gold(user)); // master_fellow_upgrade (1, 1).c2 = 500
    }

    [Fact]
    public async Task A_failed_power_up_adds_the_gap_points_keeps_the_upgrade_and_still_uses_the_material()
    {
        long user = await NewPlayerAsync("up-fail");
        // Grade 2 target, grade 1 material: gap 1 = 50 %, 15 points. Retry until the seeded roll fails, then check the books.
        long target = Grant(user, Grade2, upgrade: 0);
        JsonObject? failed = null;
        for (var attempt = 0; attempt < 40 && failed is null; attempt++)
        {
            long material = Grant(user, Grade1);
            JsonObject reply = await PowerUpAsync(user, target, material);
            Assert.Equal(0, Owned(material));
            if (reply["upgrade_result"]!.GetValue<long>() == 0)
            {
                failed = reply;
            }
            else
            {
                server.Execute($"UPDATE fellow SET upgrade=0, fail_count=0 WHERE rowid_alias={target - FellowOffset}");
            }
        }

        Assert.NotNull(failed);
        Assert.Equal(0, failed["fellow_info"]!["fellow_upgrade"]!.GetValue<long>());
        Assert.Equal(15, failed["fellow_info"]!["fellow_fail_count"]!.GetValue<long>());
    }

    [Fact]
    public async Task Power_up_points_raise_the_chance_five_percent_per_ten()
    {
        long user = await NewPlayerAsync("up-points");
        // Gap 4 (grade 6 target, grade 2 material) = 6 %; 160 points = +80 %: 86 %, so either result is valid and the points tell which.
        long target = Grant(user, Grade6, upgrade: 0, failCount: 160);
        long material = Grant(user, Grade2);

        JsonObject reply = await PowerUpAsync(user, target, material);

        // A success resets the points to 0, a failure adds the gap's 1 point.
        long result = reply["upgrade_result"]!.GetValue<long>();
        long points = reply["fellow_info"]!["fellow_fail_count"]!.GetValue<long>();
        Assert.Equal(result == 1 ? 0 : 161, points);
    }

    [Fact]
    public async Task A_power_up_carries_the_attack_bonus_over_capped_at_99()
    {
        long user = await NewPlayerAsync("up-bonus");
        long target = Grant(user, Grade1, bonus: 80);
        long material = Grant(user, Grade2, bonus: 40);

        JsonObject reply = await PowerUpAsync(user, target, material);

        Assert.Equal(99, reply["fellow_info"]!["fellow_attack_bonus"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_power_up_is_refused_for_a_locked_slotted_or_same_material_and_too_little_gold()
    {
        long user = await NewPlayerAsync("up-refuse");
        long target = Grant(user, Grade1);
        long locked = Grant(user, Grade2, locked: 1);
        long slotted = Grant(user, Grade2);
        await server.CallGatewayAsync(302, new { type = 3, fellows = new[] { new { fellow_no = slotted, fellow_position = 0 } } }, user);
        long free = Grant(user, Grade2);

        Assert.True(Refused(await PowerUpAsync(user, target, locked)));
        Assert.True(Refused(await PowerUpAsync(user, target, slotted)));
        Assert.True(Refused(await PowerUpAsync(user, target, target)));
        Assert.True(Refused(await PowerUpAsync(user, target, 99_999)));
        server.Execute($"UPDATE gameuser SET gold=10 WHERE user_no={user}");
        Assert.True(Refused(await PowerUpAsync(user, target, free)));

        Assert.Equal(1, Owned(locked));
        Assert.Equal(1, Owned(slotted));
        Assert.Equal(1, Owned(free));
        Assert.Equal(10, Gold(user));
    }

    [Fact]
    public async Task A_slotted_target_can_be_powered_up()
    {
        long user = await NewPlayerAsync("up-slotted-target");
        long target = Grant(user, Grade1);
        await server.CallGatewayAsync(302, new { type = 3, fellows = new[] { new { fellow_no = target, fellow_position = 0 } } }, user);
        long material = Grant(user, Grade2);

        JsonObject reply = await PowerUpAsync(user, target, material);

        Assert.Equal(1, reply["upgrade_result"]!.GetValue<long>());
    }

    [Fact]
    public async Task An_enhance_merges_two_maxed_allies_of_one_grade_into_a_new_ally_of_the_next_grade()
    {
        long user = await NewPlayerAsync("enh-ok", gold: 90_000);
        long first = Grant(user, Grade1, level: 20, upgrade: 5, bonus: 30);
        long second = Grant(user, OtherGrade1, level: 20, upgrade: 5, bonus: 20);

        JsonObject reply = await EnhanceAsync(user, first, second);

        Assert.Equal([first, second], reply["used_fellows"]!.AsArray().Select(n => n!.GetValue<long>()));
        JsonObject created = Assert.IsType<JsonObject>(reply["fellow_info"]);
        long newId = created["fellow_id"]!.GetValue<long>();
        Assert.Equal(2, server.Scalar($"SELECT grade FROM master_fellow WHERE fellow_id={newId}"));
        Assert.Equal(1, created["fellow_level"]!.GetValue<long>());
        Assert.Equal(0, created["fellow_upgrade"]!.GetValue<long>());
        Assert.Equal(50, created["fellow_attack_bonus"]!.GetValue<long>());
        Assert.Equal(0, Owned(first));
        Assert.Equal(0, Owned(second));
        Assert.Equal(1, Owned(created["fellow_no"]!.GetValue<long>()));
        Assert.Contains(reply["fellow_book_info"]!.AsArray(), entry => entry!["fellow_id"]!.GetValue<long>() == newId);
        Assert.Equal(90_000 - 3400, Gold(user)); // master_fellow_upgrade (1, 5).c3 = 3400
    }

    [Fact]
    public async Task An_enhance_is_refused_below_max_level_or_upgrade_across_grades_in_a_setup_or_at_grade_6()
    {
        long user = await NewPlayerAsync("enh-refuse");
        long ready1 = Grant(user, Grade1, level: 20, upgrade: 5);
        long ready2 = Grant(user, OtherGrade1, level: 20, upgrade: 5);
        long lowLevel = Grant(user, Grade1, level: 19, upgrade: 5);
        long lowUpgrade = Grant(user, Grade1, level: 20, upgrade: 4);
        long otherGrade = Grant(user, Grade2, level: 20, upgrade: 5);
        long topA = Grant(user, Grade6, level: 20, upgrade: 5);
        long topB = Grant(user, 100221, level: 20, upgrade: 5);
        await server.CallGatewayAsync(302, new { type = 3, fellows = new[] { new { fellow_no = ready2, fellow_position = 0 } } }, user);

        Assert.True(Refused(await EnhanceAsync(user, ready1, lowLevel)));
        Assert.True(Refused(await EnhanceAsync(user, ready1, lowUpgrade)));
        Assert.True(Refused(await EnhanceAsync(user, ready1, otherGrade)));
        Assert.True(Refused(await EnhanceAsync(user, ready1, ready2)));
        Assert.True(Refused(await EnhanceAsync(user, ready1, ready1)));
        Assert.True(Refused(await EnhanceAsync(user, ready1)));
        Assert.True(Refused(await EnhanceAsync(user, topA, topB)));

        Assert.All(new[] { ready1, ready2, lowLevel, lowUpgrade, otherGrade, topA, topB }, number => Assert.Equal(1, Owned(number)));
        Assert.Equal(90_000, Gold(user));
    }

    [Fact]
    public async Task An_enhance_is_refused_without_the_gold()
    {
        long user = await NewPlayerAsync("enh-poor", gold: 100);
        long first = Grant(user, Grade4, level: 20, upgrade: 5);
        long second = Grant(user, 100012, level: 20, upgrade: 5);

        Assert.True(Refused(await EnhanceAsync(user, first, second)));
        Assert.Equal(1, Owned(first));
    }

    [Fact]
    public async Task Angels_are_neither_powered_up_nor_enhanced()
    {
        long user = await NewPlayerAsync("up-angel");
        long angel = Grant(user, 110001, level: 1, upgrade: 5);
        long otherAngel = Grant(user, 110002, level: 1, upgrade: 5);
        long material = Grant(user, Grade2);

        Assert.True(Refused(await PowerUpAsync(user, angel, material)));
        Assert.True(Refused(await EnhanceAsync(user, angel, otherAngel)));

        Assert.Equal(1, Owned(material));
        Assert.Equal(90_000, Gold(user));
    }

    [Fact]
    public async Task The_fail_count_and_attack_bonus_reach_the_ally_list_in_60()
    {
        long user = await NewPlayerAsync("up-list");
        long target = Grant(user, Grade1, bonus: 7, failCount: 30);

        JsonObject loaded = await server.CallGatewayAsync(60, new { user_no = user }, user);

        JsonNode info = loaded["fellow_info"]!.AsArray().Single(f => f!["fellow_no"]!.GetValue<long>() == target)!;
        Assert.Equal(7, info["fellow_attack_bonus"]!.GetValue<long>());
        Assert.Equal(30, info["fellow_fail_count"]!.GetValue<long>());
    }
}
