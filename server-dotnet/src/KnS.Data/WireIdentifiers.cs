namespace KnS.Data;

/// <summary>
/// How the identifiers the client sees map onto table row ids. Changing any of these breaks
/// existing saves: the numbers are already in players' data on the client and the server.
/// </summary>
public static class WireIdentifiers
{
    /// <summary>gameuser.user_no = rowid_alias + this. Keeps clear of 0, the client's "not logged in yet".</summary>
    public const long UserNumberOffset = 10_000;

    /// <summary>Item number = item.rowid_alias + this. Keeps clear of the fixed starter gear (1001..1003).</summary>
    public const long ItemNumberOffset = 2_000;

    /// <summary>Fellow number = fellow.rowid_alias + this.</summary>
    public const long FellowNumberOffset = 7_000;

    /// <summary>Event item number = stage_event_offer.rowid_alias + this.</summary>
    public const long EventItemNumberOffset = 1_000_000;
}
