using KnS.Data.Repositories;
using KnS.Game.Models;
using KnS.Game.Rules;

namespace KnS.Game.Players;

/// <summary>A player's account: wallet, level, exp and the user_info / goods views of it.</summary>
public sealed class PlayerProfileService(
    PlayerRepository players,
    GoodsRepository goods,
    MasterDataRepository masterData,
    TimeProvider clock)
{
    public PlayerAccount GetAccount(long userNumber)
    {
        PlayerAccountRecord? record = players.FindAccount(userNumber);
        if (record is null)
        {
            return PlayerAccount.NewPlayer(MaxExperienceAt(Data.StoredPlayerDefaults.Level));
        }

        return new PlayerAccount(
            record.Gold, record.Gems, record.HeroPcid, record.Level, record.Experience,
            MaxExperienceAt(record.Level), record.PurchasedItemSlots, record.PurchasedAllySlots);
    }

    /// <summary>Adds (or, with negative amounts, takes) gold and gems.</summary>
    public void AddCurrency(long userNumber, long gold = 0, long gems = 0) =>
        players.AddCurrency(userNumber, gold, gems);

    /// <summary>Takes a price from the wallet (gold or gems; other price items cost nothing here).</summary>
    public void Pay(long userNumber, Price price) =>
        players.AddCurrency(
            userNumber,
            gold: price.PriceItemId == CurrencyItems.Gold ? -price.Amount : 0,
            gems: price.PriceItemId == CurrencyItems.Gems ? -price.Amount : 0);

    /// <summary>Awards a cleared stage's exp, levelling up while the bar is full. Returns the exp gained.</summary>
    public long AwardStageExperience(long userNumber, long stageId)
    {
        long stageExperience = masterData.GetStageExperience(stageId) ?? 0;
        long gain = Math.Max(0, stageExperience);
        Dictionary<long, long> experienceToNext = masterData.GetExperienceTable();

        PlayerAccount account = GetAccount(userNumber);
        long level = account.Level;
        long experience = account.Experience + gain;
        // The last level never fills.
        while (experienceToNext.ContainsKey(level + 1) && experience >= experienceToNext[level])
        {
            experience -= experienceToNext[level];
            level++;
        }

        players.SetLevel(userNumber, level, experience);
        return gain;
    }

    public UserInfo BuildUserInfo(Player player)
    {
        PlayerAccount account = GetAccount(player.UserNumber);
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        return new UserInfo
        {
            UserNumber = player.UserNumber,
            HubId = player.Nickname,
            Nickname = player.Nickname,
            Level = account.Level,
            Experience = account.Experience,
            MaxExperience = account.MaxExperience,
            Gold = account.Gold,
            Gems = account.Gems,
            Stamina = PlayerRules.Stamina,
            MaxStamina = PlayerRules.Stamina,
            FreeItemSlots = PlayerRules.FreeItemSlots,
            PurchasedItemSlots = account.PurchasedItemSlots,
            PurchasedAllySlots = account.PurchasedAllySlots,
            ActiveHeroPcid = account.HeroPcid,
            HeroAttack = PlayerRules.HeroAttack,
            HeroDefence = PlayerRules.HeroDefence,
            HeroHp = PlayerRules.HeroHp,
            NextShopUpdateTime = now + 3600,
        };
    }

    /// <summary>
    /// item_etc_info: gold and gems first, then the counted items. The client keeps only the ids
    /// it knows and ignores the rest.
    /// </summary>
    public IReadOnlyList<GoodsEntry> GetGoods(long userNumber)
    {
        PlayerAccount account = GetAccount(userNumber);
        var entries = new List<GoodsEntry>
        {
            new(CurrencyItems.Gold, account.Gold),
            new(CurrencyItems.Gems, account.Gems),
        };
        entries.AddRange(goods.GetAll(userNumber).Select(row => new GoodsEntry(row.ItemId, row.Count)));
        return entries;
    }

    private long MaxExperienceAt(long level) =>
        masterData.GetExperienceToNextLevel(level) ?? PlayerRules.FallbackMaxExperience;
}
