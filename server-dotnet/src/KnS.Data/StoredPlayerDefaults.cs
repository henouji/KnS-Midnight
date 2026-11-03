namespace KnS.Data;

/// <summary>
/// What a new player starts with, as stored: these are the column defaults of gameuser, so a new
/// row starts with them.
/// </summary>
public static class StoredPlayerDefaults
{
    public const long Gold = 10_000;
    public const long Gems = 1_000;

    /// <summary>The hero row in the client's master data that every player owns.</summary>
    public const long HeroPcid = 1;

    public const long Level = 1;
}
