using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using KnS.Protocol.Crypto;
using KnS.Protocol.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnS.Tests.Integration;

/// <summary>
/// The whole server in memory on a scratch SQLite database. The server reads its settings from
/// KNS_* environment variables, which are process-wide, so every integration test shares this
/// one instance (see <see cref="SharedServer"/>).
/// </summary>
public sealed class ServerFixture : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kns-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public ServerFixture()
    {
        Directory.CreateDirectory(_folder);
        Environment.SetEnvironmentVariable("KNS_DB", Path.Combine(_folder, "kns.sqlite"));
        Environment.SetEnvironmentVariable("KNS_QUIET", "1");
        Environment.SetEnvironmentVariable("KNS_RNG_SEED", "12345");
        Environment.SetEnvironmentVariable("KNS_TRAFFIC_LOG", "1");
        Environment.SetEnvironmentVariable("KNS_LOG_DIR", _folder);
        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public HttpClient Client { get; }

    /// <summary>Runs one statement on the scratch database, to set up a state the gateway cannot reach quickly (a max-level ally, a gold balance).</summary>
    public void Execute(string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(_folder, "kns.sqlite"));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The first column of the first row of a query on the scratch database.</summary>
    public long Scalar(string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(_folder, "kns.sqlite"));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Sends one game gateway request and returns the decrypted reply.</summary>
    public async Task<JsonObject> CallGatewayAsync(int protocol, object parameters, long userNumber = 0)
    {
        var request = new JsonObject { ["params"] = WireJson.ToJsonObject(parameters), ["protocol"] = protocol };
        byte[] body = GameGatewayCipher.Encrypt(Encoding.UTF8.GetBytes(request.ToJsonString()));
        return await PostGatewayAsync(body, userNumber);
    }

    public async Task<JsonObject> PostGatewayAsync(byte[] body, long userNumber = 0)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/1mhero/gateway.php") { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        if (userNumber != 0)
        {
            message.Headers.Add("USERNO", userNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using HttpResponseMessage response = await Client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/plain; charset=ISO-8859-1", response.Content.Headers.ContentType?.ToString());
        byte[] reply = await response.Content.ReadAsByteArrayAsync();
        return WireJson.ParseObject(GameGatewayCipher.Decrypt(reply))!;
    }

    /// <summary>Every traffic log line written so far.</summary>
    public IReadOnlyList<JsonObject> ReadTraffic() =>
        Directory.EnumerateFiles(Path.Combine(_folder, "traffic"), "*.jsonl")
            .Order(StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder in %TEMP%; leaving it behind is harmless.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SharedServer : ICollectionFixture<ServerFixture>
{
    public const string Name = "server";
}
