using System.Data.Common;
using Dapper;
using KnS.Data.Dialects;
using KnS.Data.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace KnS.Data;

/// <summary>
/// Opens sessions on the game database (SQLite file, or MySQL when configured). The schema and
/// the master tables are set up once per process, on the first session.
/// </summary>
public sealed class GameDatabase
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<GameDatabase> _logger;
    private readonly string _connectionString;
    private readonly object _schemaLock = new();
    private volatile bool _schemaReady;

    static GameDatabase()
    {
        // Row types use PascalCase properties for snake_case columns (hero_pcid -> HeroPcid).
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public GameDatabase(DatabaseOptions options, ILogger<GameDatabase> logger)
    {
        _options = options;
        _logger = logger;
        Dialect = options.UsesMySql ? MySqlDialect.Instance : SqliteDialect.Instance;
        _connectionString = options.UsesMySql ? BuildMySqlConnectionString(options) : BuildSqliteConnectionString(options);
    }

    public SqlDialect Dialect { get; }

    /// <summary>Where the data lives, without credentials (for logs and /health).</summary>
    public string Description => _options.UsesMySql
        ? DescribeMySqlUrl(_options.MySqlUrl!)
        : $"sqlite:{_options.SqlitePath}";

    public DataSession OpenSession()
    {
        DbConnection connection = _options.UsesMySql
            ? new MySqlConnection(_connectionString)
            : new SqliteConnection(_connectionString);
        connection.Open();
        var session = new DataSession(connection, Dialect);
        EnsureSchema(session);
        return session;
    }

    private void EnsureSchema(DataSession session)
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaLock)
        {
            if (_schemaReady)
            {
                return;
            }

            new GameSchema(session, _options.MasterDataDirectory, _logger).Create();
            _schemaReady = true;
        }
    }

    private static string BuildSqliteConnectionString(DatabaseOptions options)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(options.SqlitePath));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // DefaultTimeout also bounds how long a command waits on another writer's lock: several
        // players' requests write to the one file.
        return new SqliteConnectionStringBuilder
        {
            DataSource = options.SqlitePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    private static string BuildMySqlConnectionString(DatabaseOptions options)
    {
        var url = new Uri(options.MySqlUrl!);
        string[] credentials = url.UserInfo.Split(':', 2);
        return new MySqlConnectionStringBuilder
        {
            Server = url.Host,
            Port = (uint)(url.Port > 0 ? url.Port : 3306),
            UserID = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
            Database = url.AbsolutePath.TrimStart('/'),
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.VerifyCA,
            ConnectionTimeout = 15,
            Pooling = true,
            MaximumPoolSize = (uint)Math.Max(1, options.MySqlPoolSize),
        }.ToString();
    }

    private static string DescribeMySqlUrl(string mySqlUrl)
    {
        var url = new Uri(mySqlUrl);
        return $"mysql://{url.Host}{url.AbsolutePath}";
    }
}
