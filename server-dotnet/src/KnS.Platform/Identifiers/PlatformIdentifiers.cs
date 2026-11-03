using System.Globalization;

namespace KnS.Platform.Identifiers;

/// <summary>The time-derived ids the platform endpoints hand out.</summary>
public sealed class PlatformIdentifiers(TimeProvider clock)
{
    /// <summary>A Hub uid: "100" + the current millisecond, modulo 10^7.</summary>
    public string NewHubUid() => "100" + Format(clock.GetUtcNow().ToUnixTimeMilliseconds() % 10_000_000);

    /// <summary>A Hive guest uid: "905" + the current hundredth of a second, modulo 10^8.</summary>
    public string NewHiveGuestUid() => "905" + Format(clock.GetUtcNow().ToUnixTimeMilliseconds() / 10 % 100_000_000);

    /// <summary>A Hive device id: "539" + the current tenth of a second, modulo 10^7.</summary>
    public string NewHiveDeviceId() => "539" + Format(clock.GetUtcNow().ToUnixTimeMilliseconds() / 100 % 10_000_000);

    public long UnixSeconds => clock.GetUtcNow().ToUnixTimeSeconds();

    public double UnixTime => clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0;

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
