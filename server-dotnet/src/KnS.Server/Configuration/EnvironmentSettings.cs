using System.Globalization;
using KnS.Data;
using KnS.Game;

namespace KnS.Server.Configuration;

/// <summary>
/// Reads the KNS_* environment variables the Python server used, so the same settings (local
/// shells, Azure app settings) drive both servers.
/// </summary>
public static class EnvironmentSettings
{
    public static ServerOptions ReadServerOptions(string[] args) => new()
    {
        LogDirectory = Read("KNS_LOG_DIR") ?? "work",
        TrafficLog = !IsSet("KNS_QUIET") || IsSet("KNS_TRAFFIC_LOG"),
        TrafficRetentionDays = Math.Max(1, ReadInt("KNS_TRAFFIC_DAYS") ?? 7),
        // The port: first argument, else PORT / WEBSITES_PORT (App Service), else 8080.
        Port = args.Length > 0 && int.TryParse(args[0], CultureInfo.InvariantCulture, out int argPort)
            ? argPort
            : ReadInt("PORT") ?? ReadInt("WEBSITES_PORT") ?? 8080,
        Quiet = IsSet("KNS_QUIET"),
        BreakActiveUser = IsSet("KNS_BREAK_AU"),
        GatewayDelay = ReadSeconds("KNS_GW_DELAY"),
        BillingDelay = ReadSeconds("KNS_BILLING_DELAY"),
        AdminKey = Read("KNS_ADMIN_KEY"),
    };

    public static DatabaseOptions ReadDatabaseOptions()
    {
        var options = new DatabaseOptions
        {
            MySqlUrl = Read("KNS_DATABASE_URL") is { } url && url.StartsWith("mysql://", StringComparison.Ordinal) ? url : null,
        };
        if (Read("KNS_DB") is { } sqlitePath)
        {
            // KNS_DB points tests at a scratch database so they never touch real saved progress.
            options = options with { SqlitePath = sqlitePath };
        }

        if (ReadInt("KNS_DB_POOL") is { } poolSize)
        {
            options = options with { MySqlPoolSize = poolSize };
        }

        if (Read("KNS_SEED_DIR") is { } seedDirectory)
        {
            options = options with { MasterDataDirectory = seedDirectory };
        }

        return options;
    }

    public static GameOptions ReadGameOptions() => new()
    {
        EmptyHeroList = IsSet("KNS_EMPTY_HERO"),
        MuteAudio = IsSet("KNS_MUTE"),
        DropEverything = IsSet("KNS_DROP_ALL"),
        ServeAdditionFiles = IsSet("KNS_SERVE_ADDITION"),
        RandomSeed = ReadSeed("KNS_RNG_SEED"),
        TestersOnly = IsSet("KNS_TESTERS_ONLY"),
        OpenAccess = IsSet("KNS_OPEN_ACCESS"),
    };

    private static string? Read(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    /// <summary>Like the Python server: any non-empty value switches the flag on (even "0").</summary>
    private static bool IsSet(string name) => Read(name) is not null;

    private static int? ReadInt(string name) =>
        int.TryParse(Read(name), CultureInfo.InvariantCulture, out int value) ? value : null;

    private static TimeSpan ReadSeconds(string name) =>
        double.TryParse(Read(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    /// <summary>A 64-bit seed; negative values wrap like the Python server's (seed &amp; 2^64-1).</summary>
    private static ulong? ReadSeed(string name)
    {
        string? text = Read(name);
        if (ulong.TryParse(text, CultureInfo.InvariantCulture, out ulong unsignedSeed))
        {
            return unsignedSeed;
        }

        return long.TryParse(text, CultureInfo.InvariantCulture, out long signedSeed) ? unchecked((ulong)signedSeed) : null;
    }
}
