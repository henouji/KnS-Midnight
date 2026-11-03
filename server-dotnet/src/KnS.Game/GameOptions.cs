namespace KnS.Game;

/// <summary>Switches for testing and for running against differently built APKs.</summary>
public sealed record GameOptions
{
    /// <summary>The package of the original game; its archived addition data lives under this name.</summary>
    public const string GamePackage = "com.com2us.imhero.normal.freefull.google.global.android.common";

    /// <summary>
    /// Sends <c>hero_info: []</c> and no starter items (KNS_EMPTY_HERO), to test whether a new
    /// account is expected to go through character creation instead.
    /// </summary>
    public bool EmptyHeroList { get; init; }

    /// <summary>Turns the client's music and sound effects off at login (KNS_MUTE).</summary>
    public bool MuteAudio { get; init; }

    /// <summary>Debug: every stage reward slot hits and new items get the most options allowed (KNS_DROP_ALL).</summary>
    public bool DropEverything { get; init; }

    /// <summary>
    /// Offers the archived addition files in protocol 48 (KNS_SERVE_ADDITION). Off by default: the
    /// current APK bundles them, and listing them makes a fresh install download 239 MB it discards.
    /// </summary>
    public bool ServeAdditionFiles { get; init; }

    /// <summary>The archived addition data: &lt;root&gt;/&lt;version&gt;/&lt;file&gt;.</summary>
    public string AdditionFilesRoot { get; init; } = Path.Combine("original", GamePackage, "files");

    /// <summary>
    /// Seeds a deterministic random source (KNS_RNG_SEED) that draws the same numbers as the
    /// Python server's, so tools/parity_check.py can compare the two. Null = truly random.
    /// </summary>
    public ulong? RandomSeed { get; init; }

    /// <summary>
    /// Only testers in the tester table (device attached, not revoked) may use the game gateway
    /// (KNS_TESTERS_ONLY). Anyone else gets an error popup and shows up in the refused list.
    /// </summary>
    public bool TestersOnly { get; init; }

    /// <summary>
    /// With <see cref="TestersOnly"/>: everyone may play except devices attached to a revoked tester
    /// (KNS_OPEN_ACCESS). The tester table then works as a blocklist.
    /// </summary>
    public bool OpenAccess { get; init; }
}
