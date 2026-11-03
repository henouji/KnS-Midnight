using KnS.Data.Repositories;

namespace KnS.Game.Access;

/// <summary>The feature_flag table, read on every use so a change applies to the next request.</summary>
public sealed class FeatureFlags(FeatureFlagRepository flags)
{
    /// <summary>The rebuild fourth dungeon ally slot (<c>party_slots: 4</c>); on unless the row says off.</summary>
    public const string FourthFellow = "fourth_fellow";

    /// <summary>The state of a flag whose row is missing (the schema seeds these as rows).</summary>
    public static readonly IReadOnlyDictionary<string, bool> Defaults = new Dictionary<string, bool> { [FourthFellow] = true };

    public bool IsEnabled(string featureKey) => flags.IsEnabled(featureKey, Defaults.GetValueOrDefault(featureKey));
}
