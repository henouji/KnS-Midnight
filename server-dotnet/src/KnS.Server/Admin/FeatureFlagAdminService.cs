using System.Text.RegularExpressions;
using KnS.Data.Repositories;

namespace KnS.Server.Admin;

/// <summary>The owner's switch for server-side feature flags.</summary>
public sealed partial class FeatureFlagAdminService(FeatureFlagRepository flags, AdminWork work, TimeProvider clock)
{
    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex KeyPattern();

    public IReadOnlyList<FeatureFlagRecord> List() => flags.List();

    public AdminResult Set(string featureKey, bool enabled)
    {
        featureKey = featureKey.Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(featureKey))
        {
            return AdminResult.Invalid("needs feature_key (a-z, 0-9, _) and enabled (true/false)");
        }

        return work.Run(() =>
        {
            flags.Set(featureKey, enabled, clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0);
            return AdminResult.Ok;
        });
    }
}
