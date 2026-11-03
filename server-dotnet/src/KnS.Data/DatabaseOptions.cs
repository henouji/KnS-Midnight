namespace KnS.Data;

/// <summary>Where the game data lives.</summary>
public sealed record DatabaseOptions
{
    /// <summary>
    /// <c>mysql://user:password@host:3306/database</c> (Azure Database for MySQL). When empty the
    /// server uses the SQLite file at <see cref="SqlitePath"/>.
    /// </summary>
    public string? MySqlUrl { get; init; }

    /// <summary>The SQLite database file (created on first use).</summary>
    public string SqlitePath { get; init; } = Path.Combine("work", "kns.sqlite");

    /// <summary>Most pooled MySQL connections open at once.</summary>
    public int MySqlPoolSize { get; init; } = 16;

    /// <summary>The folder of the master-data seed CSVs (exported from the game by tools/dump_*.py).</summary>
    public string MasterDataDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "seed");

    public bool UsesMySql => !string.IsNullOrEmpty(MySqlUrl);
}
