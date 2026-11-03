namespace KnS.Data.Repositories;

public sealed record FeatureFlagRecord
{
    public string FeatureKey { get; init; } = "";

    public bool Enabled { get; init; }

    /// <summary>Unix seconds.</summary>
    public double CreatedDate { get; init; }

    /// <summary>Unix seconds of the last change.</summary>
    public double UpdateDate { get; init; }
}

/// <summary>Server-side feature switches (feature_flag), changed without a restart.</summary>
public sealed class FeatureFlagRepository(DataSession session)
{
    private const string FlagColumns = "feature_key AS FeatureKey, enabled AS Enabled, created_date AS CreatedDate, update_date AS UpdateDate";

    /// <summary>The flag's state, or <paramref name="fallback"/> when the table has no such row.</summary>
    public bool IsEnabled(string featureKey, bool fallback) =>
        session.QueryFirstOrDefault<long?>("SELECT enabled FROM feature_flag WHERE feature_key=@featureKey", new { featureKey }) is { } enabled
            ? enabled != 0
            : fallback;

    public IReadOnlyList<FeatureFlagRecord> List() =>
        session.Query<FeatureFlagRecord>($"SELECT {FlagColumns} FROM feature_flag ORDER BY feature_key");

    /// <summary>Turns a flag on or off, creating its row when missing.</summary>
    public void Set(string featureKey, bool enabled, double now) =>
        session.Execute(
            "INSERT INTO feature_flag(feature_key, enabled, created_date, update_date) VALUES(@featureKey, @enabled, @now, @now)"
            + $" {session.Dialect.OnConflictUpdate("feature_key")} enabled=@enabled, update_date=@now",
            new { featureKey, enabled = enabled ? 1 : 0, now });
}
