using System.Text;
using System.Text.Json.Serialization;
using KnS.Data;
using KnS.Data.Schema;
using KnS.Protocol.Crypto;
using KnS.Protocol.Json;

namespace KnS.Server.Endpoints;

/// <summary>
/// GET /health, for the App Service health check and smoke tests. "/" answers even when the game
/// cannot work, so this checks what the game needs: the gateway cipher round-trips, the seed CSVs
/// are there and the game database answers a query. Errors report the exception type only (the
/// endpoint is public).
/// </summary>
public sealed class HealthCheck(DatabaseOptions databaseOptions, TimeProvider clock)
{
    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();

    public HealthReport Run(IServiceProvider requestServices)
    {
        bool? cipher = null, seeds = null, database = null;
        string? error = null;
        try
        {
            byte[] ping = Encoding.UTF8.GetBytes("""{"ping":1}""");
            cipher = WireJson.ParseObject(GameGatewayCipher.Decrypt(GameGatewayCipher.Encrypt(ping)))?["ping"]?.GetValue<int>() == 1;
            seeds = MasterTables.All.All(table => File.Exists(Path.Combine(databaseOptions.MasterDataDirectory, table.SeedFile)));
            DataSession session = requestServices.GetRequiredService<DataSession>();
            session.Exists("SELECT count(*) FROM gameuser");
            database = true;
        }
        catch (Exception e)
        {
            error = e.GetType().Name;
        }

        bool healthy = cipher == true && seeds == true && database == true;
        return new HealthReport(
            healthy ? "ok" : "fail",
            new HealthChecks(cipher, seeds, database, error),
            (long)(clock.GetUtcNow() - _startedAt).TotalSeconds);
    }
}

public sealed record HealthReport(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("checks")] HealthChecks Checks,
    [property: JsonPropertyName("uptime_s")] long UptimeSeconds)
{
    [JsonIgnore] public bool IsHealthy => Status == "ok";
}

/// <summary>Each check that ran; one that did not run (an earlier one threw) is left out.</summary>
public sealed record HealthChecks(
    [property: JsonPropertyName("crypto"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Cipher,
    [property: JsonPropertyName("seeds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Seeds,
    [property: JsonPropertyName("db"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Database,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);
