namespace KnS.Game.Rules;

/// <summary>
/// Master item ids of the currencies. Balances travel as item_etc_info rows keyed by these ids:
/// ParsingDataToUserGoods resets gold and gems to -1 and refills them from those rows.
/// </summary>
public static class CurrencyItems
{
    public const long Gold = 1900008;
    public const long Gems = 1900009;

    /// <summary>
    /// A stage drop that pays gold when picked up. The client reports it by item number like an
    /// item, so the server sets the amount.
    /// </summary>
    public const long GoldBox = 1900015;
}
