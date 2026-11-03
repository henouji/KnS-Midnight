using System.Text;
using System.Text.Json.Nodes;
using KnS.Protocol.Crypto;
using KnS.Protocol.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnS.Tests.Integration;

/// <summary>the tester table (KNS_TESTERS_ONLY), on a server of its own (the setting is read when the host starts).</summary>
[Collection(SharedServer.Name)]
public sealed class AllowlistTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kns-allow-" + Guid.NewGuid().ToString("N"));

    // Runs in the shared-server collection so no other host starts while the variable is set;
    // the fixture is unused.
    public AllowlistTests(ServerFixture shared)
    {
        _ = shared;
        Directory.CreateDirectory(_folder);
        string? previousDb = Environment.GetEnvironmentVariable("KNS_DB");
        Environment.SetEnvironmentVariable("KNS_DB", Path.Combine(_folder, "kns.sqlite"));
        Environment.SetEnvironmentVariable("KNS_QUIET", "1");
        Environment.SetEnvironmentVariable("KNS_TESTERS_ONLY", "1");
        Environment.SetEnvironmentVariable("KNS_ADMIN_KEY", "secret");
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KNS_TESTERS_ONLY", null);
            Environment.SetEnvironmentVariable("KNS_ADMIN_KEY", null);
            Environment.SetEnvironmentVariable("KNS_DB", previousDb);
        }
    }

    [Fact]
    public async Task A_tester_is_refused_until_a_device_is_attached_and_again_once_revoked()
    {
        await AdminAsync("", new { email = "Friend@Example.com", note = "alpha" });

        JsonObject refused = await CallAsync(51, new { hub_did = "friend-device" });
        Assert.Equal(1, refused["errno"]!.GetValue<int>());
        Assert.False(refused.ContainsKey("user_no"));

        JsonObject overview = await AdminAsync("attach", new { email = "friend@example.com", device_id = "friend-device" });
        Assert.Empty(overview["refused"]!.AsArray());
        long user = (await CallAsync(51, new { hub_did = "friend-device" }))["user_no"]!.GetValue<long>();
        Assert.Equal(0, (await CallAsync(60, new { user_no = user }, user))["errno"]!.GetValue<int>());

        await AdminAsync("revoke", new { email = "friend@example.com" });
        Assert.Equal(1, (await CallAsync(51, new { hub_did = "friend-device" }))["errno"]!.GetValue<int>());
        Assert.Equal(1, (await CallAsync(60, new { user_no = user }, user))["errno"]!.GetValue<int>());

        await AdminAsync("restore", new { email = "friend@example.com" });
        Assert.Equal(0, (await CallAsync(51, new { hub_did = "friend-device" }))["errno"]!.GetValue<int>());
    }

    [Fact]
    public async Task Open_access_lets_strangers_in_and_still_turns_a_revoked_tester_away()
    {
        string? previousDb = Environment.GetEnvironmentVariable("KNS_DB");
        Environment.SetEnvironmentVariable("KNS_DB", Path.Combine(_folder, "open.sqlite"));
        Environment.SetEnvironmentVariable("KNS_TESTERS_ONLY", "1");
        Environment.SetEnvironmentVariable("KNS_OPEN_ACCESS", "1");
        Environment.SetEnvironmentVariable("KNS_ADMIN_KEY", "secret");
        WebApplicationFactory<Program> factory;
        HttpClient client;
        try
        {
            factory = new WebApplicationFactory<Program>();
            client = factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KNS_TESTERS_ONLY", null);
            Environment.SetEnvironmentVariable("KNS_OPEN_ACCESS", null);
            Environment.SetEnvironmentVariable("KNS_ADMIN_KEY", null);
            Environment.SetEnvironmentVariable("KNS_DB", previousDb);
        }

        using (factory)
        using (client)
        {
            async Task<int> LoginAsync(string device)
            {
                var request = new JsonObject { ["params"] = WireJson.ToJsonObject(new { hub_did = device }), ["protocol"] = 51 };
                using var message = new HttpRequestMessage(HttpMethod.Post, "/1mhero/gateway.php")
                {
                    Content = new ByteArrayContent(GameGatewayCipher.Encrypt(Encoding.UTF8.GetBytes(request.ToJsonString()))),
                };
                using HttpResponseMessage response = await client.SendAsync(message);
                return WireJson.ParseObject(GameGatewayCipher.Decrypt(await response.Content.ReadAsByteArrayAsync()))!["errno"]!.GetValue<int>();
            }

            async Task AdminAsync(string action, object body)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/admin/testers" + (action.Length > 0 ? "/" + action : ""))
                {
                    Content = new StringContent(WireJson.ToJsonObject(body).ToJsonString(), Encoding.UTF8, "application/json"),
                };
                message.Headers.Add("X-Admin-Key", "secret");
                using HttpResponseMessage response = await client.SendAsync(message);
                response.EnsureSuccessStatusCode();
            }

            Assert.Equal(0, await LoginAsync("stranger"));

            await AdminAsync("", new { email = "bad@example.com" });
            await AdminAsync("attach", new { email = "bad@example.com", device_id = "bad-device" });
            Assert.Equal(0, await LoginAsync("bad-device"));
            await AdminAsync("revoke", new { email = "bad@example.com" });
            Assert.Equal(1, await LoginAsync("bad-device"));
            await AdminAsync("restore", new { email = "bad@example.com" });
            Assert.Equal(0, await LoginAsync("bad-device"));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task A_refused_device_is_listed_once_with_its_attempt_count()
    {
        await CallAsync(51, new { hub_did = "stranger" });
        await CallAsync(51, new { hub_did = "stranger" });

        JsonObject overview = await AdminAsync("", null, HttpMethod.Get);
        JsonNode device = overview["refused"]!.AsArray().Single(d => d!["device_id"]!.GetValue<string>() == "stranger")!;
        Assert.Equal(2, device["attempts"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_device_cannot_belong_to_two_emails()
    {
        await AdminAsync("", new { email = "a@example.com" });
        await AdminAsync("", new { email = "b@example.com" });
        await AdminAsync("attach", new { email = "a@example.com", device_id = "shared-device" });

        using HttpResponseMessage response = await SendAdminAsync("attach", new { email = "b@example.com", device_id = "shared-device" }, "secret");

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_endpoint_needs_the_key()
    {
        using HttpResponseMessage wrong = await SendAdminAsync("", null, "nope", HttpMethod.Get);
        using HttpResponseMessage missing = await SendAdminAsync("", null, null, HttpMethod.Get);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missing.StatusCode);
    }

    [Fact]
    public async Task The_admin_flags_endpoint_lists_and_changes_a_flag()
    {
        JsonObject before = await AdminPathAsync("/admin/flags", null, HttpMethod.Get);
        Assert.True(before["flags"]!.AsArray().Single(f => f!["feature_key"]!.GetValue<string>() == "fourth_fellow")!["enabled"]!.GetValue<bool>());

        JsonObject after = await AdminPathAsync("/admin/flags", new { feature_key = "fourth_fellow", enabled = false });
        Assert.False(after["flags"]!.AsArray().Single(f => f!["feature_key"]!.GetValue<string>() == "fourth_fellow")!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_linked_device_plays_the_old_account_until_it_is_unlinked()
    {
        await AdminAsync("", new { email = "old@example.com" });
        await AdminAsync("attach", new { email = "old@example.com", device_id = "old-phone" });
        long oldUser = (await CallAsync(51, new { hub_did = "old-phone" }))["user_no"]!.GetValue<long>();

        JsonObject linked = await AdminPathAsync("/admin/accounts/link", new { device_id = "new-phone", user_no = oldUser });
        Assert.Equal("new-phone", linked["links"]!.AsArray().Single()!["device_id"]!.GetValue<string>());

        // The new phone is no tester itself: the link alone lets it log in and load the old account.
        JsonObject login = await CallAsync(51, new { hub_did = "new-phone" });
        Assert.Equal(0, login["errno"]!.GetValue<int>());
        Assert.Equal(oldUser, login["user_no"]!.GetValue<long>());
        Assert.Equal(0, (await CallAsync(60, new { user_no = oldUser }, oldUser))["errno"]!.GetValue<int>());

        // Revoking the account's own tester closes the linked phone too.
        await AdminAsync("revoke", new { email = "old@example.com" });
        Assert.Equal(1, (await CallAsync(51, new { hub_did = "new-phone" }))["errno"]!.GetValue<int>());
        await AdminAsync("restore", new { email = "old@example.com" });

        await AdminPathAsync("/admin/accounts/unlink", new { device_id = "new-phone" });
        Assert.Equal(1, (await CallAsync(51, new { hub_did = "new-phone" }))["errno"]!.GetValue<int>());
    }

    [Fact]
    public async Task Linking_to_an_unknown_account_is_refused_and_stores_nothing()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/admin/accounts/link")
        {
            Content = new StringContent("""{"device_id":"ghost-phone","user_no":99999999}""", Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Admin-Key", "secret");
        using HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await AdminPathAsync("/admin/accounts", null, HttpMethod.Get))["links"]!.AsArray());
    }

    [Fact]
    public async Task The_wallet_of_an_account_can_be_corrected_within_limits()
    {
        await AdminAsync("", new { email = "wallet@example.com" });
        await AdminAsync("attach", new { email = "wallet@example.com", device_id = "wallet-phone" });
        long user = (await CallAsync(51, new { hub_did = "wallet-phone" }))["user_no"]!.GetValue<long>();

        JsonObject saved = await AdminPathAsync("/admin/accounts/wallet", new { user_no = user, gold = 1234, gems = 56 });
        JsonNode account = saved["accounts"]!.AsArray().Single(a => a!["user_no"]!.GetValue<long>() == user)!;
        Assert.Equal(1234, account["gold"]!.GetValue<long>());
        Assert.Equal(56, account["gems"]!.GetValue<long>());

        foreach (string bad in new[] { $$"""{"user_no":{{user}},"gold":-1,"gems":0}""", $$"""{"user_no":{{user}},"gold":0,"gems":1000000000}""" })
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/admin/accounts/wallet") { Content = new StringContent(bad, Encoding.UTF8, "application/json") };
            message.Headers.Add("X-Admin-Key", "secret");
            using HttpResponseMessage response = await _client.SendAsync(message);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_version_check_needs_no_identity()
    {
        JsonObject check = await CallAsync(48, new { });

        Assert.Equal(0, check["errno"]!.GetValue<int>());
    }

    private async Task<JsonObject> AdminPathAsync(string path, object? body, HttpMethod? method = null)
    {
        using var message = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        if (body is not null)
        {
            message.Content = new StringContent(WireJson.ToJsonObject(body).ToJsonString(), Encoding.UTF8, "application/json");
        }

        message.Headers.Add("X-Admin-Key", "secret");
        using HttpResponseMessage response = await _client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private async Task<JsonObject> AdminAsync(string action, object? body, HttpMethod? method = null)
    {
        using HttpResponseMessage response = await SendAdminAsync(action, body, "secret", method);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private Task<HttpResponseMessage> SendAdminAsync(string action, object? body, string? key, HttpMethod? method = null)
    {
        var message = new HttpRequestMessage(method ?? HttpMethod.Post, "/admin/testers" + (action.Length > 0 ? "/" + action : ""));
        if (body is not null)
        {
            message.Content = new StringContent(WireJson.ToJsonObject(body).ToJsonString(), Encoding.UTF8, "application/json");
        }

        if (key is not null)
        {
            message.Headers.Add("X-Admin-Key", key);
        }

        return _client.SendAsync(message);
    }

    private async Task<JsonObject> CallAsync(int protocol, object parameters, long userNumber = 0)
    {
        var request = new JsonObject { ["params"] = WireJson.ToJsonObject(parameters), ["protocol"] = protocol };
        using var message = new HttpRequestMessage(HttpMethod.Post, "/1mhero/gateway.php")
        {
            Content = new ByteArrayContent(GameGatewayCipher.Encrypt(Encoding.UTF8.GetBytes(request.ToJsonString()))),
        };
        if (userNumber != 0)
        {
            message.Headers.Add("USERNO", userNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using HttpResponseMessage response = await _client.SendAsync(message);
        return WireJson.ParseObject(GameGatewayCipher.Decrypt(await response.Content.ReadAsByteArrayAsync()))!;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
