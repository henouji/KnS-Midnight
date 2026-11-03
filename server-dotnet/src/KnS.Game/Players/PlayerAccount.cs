using KnS.Data;
using KnS.Data.Repositories;
using KnS.Game.Rules;

namespace KnS.Game.Players;

/// <summary>A player's wallet, level and bought bag slots.</summary>
public sealed record PlayerAccount(
    long Gold,
    long Gems,
    long HeroPcid,
    long Level,
    long Experience,
    long MaxExperience,
    long PurchasedItemSlots,
    long PurchasedAllySlots)
{
    /// <summary>What the player holds of a currency item (gold or gems); 0 for any other item.</summary>
    public long BalanceOf(long currencyItemId) => currencyItemId switch
    {
        CurrencyItems.Gold => Gold,
        CurrencyItems.Gems => Gems,
        _ => 0,
    };

    public bool CanAfford(Price price) => BalanceOf(price.PriceItemId) >= price.Amount;

    public long PurchasedSlots(BagKind bag) => bag == BagKind.Items ? PurchasedItemSlots : PurchasedAllySlots;

    /// <summary>The account of a player with no gameuser row yet.</summary>
    public static PlayerAccount NewPlayer(long maxExperience) => new(
        StoredPlayerDefaults.Gold, StoredPlayerDefaults.Gems, StoredPlayerDefaults.HeroPcid,
        StoredPlayerDefaults.Level, 0, maxExperience, 0, 0);
}
