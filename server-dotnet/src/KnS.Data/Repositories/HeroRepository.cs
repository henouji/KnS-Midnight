namespace KnS.Data.Repositories;

public sealed record OwnedHeroRecord
{
    /// <summary>The per-user hero id the client echoes back.</summary>
    public long HeroNumber { get; init; }

    public long HeroPcid { get; init; }
}

public sealed record EquippedItemRecord
{
    /// <summary>The equipment range: 200000 weapon, 300000 shield, 400000 armor, ...</summary>
    public long Category { get; init; }

    public long ItemNumber { get; init; }
}

/// <summary>The heroes a player owns (user_hero) and what each one wears (hero_equip).</summary>
public sealed class HeroRepository(DataSession session)
{
    public void EnsureOwned(long userNumber, long heroNumber, long heroPcid) =>
        session.Execute(
            $"{session.Dialect.InsertOrIgnore} INTO user_hero(user_no, hero_no, hero_pcid) VALUES(@userNumber, @heroNumber, @heroPcid)",
            new { userNumber, heroNumber, heroPcid });

    public void Add(long userNumber, long heroNumber, long heroPcid) =>
        session.Execute(
            "INSERT INTO user_hero(user_no, hero_no, hero_pcid) VALUES(@userNumber, @heroNumber, @heroPcid)",
            new { userNumber, heroNumber, heroPcid });

    public IReadOnlyList<OwnedHeroRecord> GetOwned(long userNumber) =>
        session.Query<OwnedHeroRecord>(
            "SELECT hero_no AS HeroNumber, hero_pcid AS HeroPcid FROM user_hero WHERE user_no=@userNumber ORDER BY hero_no",
            new { userNumber });

    public long? FindHeroNumber(long userNumber, long heroPcid) =>
        session.QueryFirstOrDefault<long?>(
            "SELECT hero_no FROM user_hero WHERE user_no=@userNumber AND hero_pcid=@heroPcid",
            new { userNumber, heroPcid });

    public IReadOnlyList<EquippedItemRecord> GetEquipment(long userNumber, long heroNumber) =>
        session.Query<EquippedItemRecord>(
            "SELECT category AS Category, item_no AS ItemNumber FROM hero_equip"
            + " WHERE user_no=@userNumber AND hero_no=@heroNumber ORDER BY category",
            new { userNumber, heroNumber });

    public void Equip(long userNumber, long heroNumber, long category, long itemNumber) =>
        session.Execute(
            $"{session.Dialect.InsertOrReplace} INTO hero_equip(user_no, hero_no, category, item_no)"
            + " VALUES(@userNumber, @heroNumber, @category, @itemNumber)",
            new { userNumber, heroNumber, category, itemNumber });

    /// <summary>Takes an item off every hero except <paramref name="heroNumber"/>: an item is worn by one hero at a time.</summary>
    public void UnequipFromOtherHeroes(long userNumber, long itemNumber, long heroNumber) =>
        session.Execute(
            "DELETE FROM hero_equip WHERE user_no=@userNumber AND item_no=@itemNumber AND hero_no!=@heroNumber",
            new { userNumber, itemNumber, heroNumber });

    /// <summary>Moves a worn item to another equipment range (after it evolved into a different item).</summary>
    public void SetCategoryOfItem(long userNumber, long itemNumber, long category) =>
        session.Execute(
            "UPDATE hero_equip SET category=@category WHERE user_no=@userNumber AND item_no=@itemNumber",
            new { category, userNumber, itemNumber });
}
