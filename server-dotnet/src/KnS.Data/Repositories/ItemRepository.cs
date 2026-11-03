namespace KnS.Data.Repositories;

public sealed record ItemRecord
{
    public long ItemNumber { get; init; }
    public long ItemId { get; init; }
    public long Upgrade { get; init; }
    public long Attack { get; init; }
    public long Defence { get; init; }
    public long Hp { get; init; }
    public bool Locked { get; init; }
}

public sealed record ItemOptionRecord
{
    public long OptionId { get; init; }
    public long Value1 { get; init; }
    public long Value2 { get; init; }
}

/// <summary>Attack / defence / hp of an item.</summary>
public readonly record struct ItemStats(long Attack, long Defence, long Hp);

/// <summary>
/// Items (item), their options (item_option) and the starter gear a player gave up
/// (starter_sold). Item numbers are the client's ids (<see cref="WireIdentifiers.ItemNumberOffset"/>).
/// </summary>
public sealed class ItemRepository(DataSession session)
{
    private const string ItemColumns =
        "rowid_alias + @offset AS ItemNumber, item_id AS ItemId, upgrade AS Upgrade, attack AS Attack, defence AS Defence, hp AS Hp, locked AS Locked";

    private const long Offset = WireIdentifiers.ItemNumberOffset;

    /// <summary>An item the player owns (claimed), or null.</summary>
    public ItemRecord? FindOwned(long userNumber, long itemNumber) =>
        session.QueryFirstOrDefault<ItemRecord>(
            $"SELECT {ItemColumns} FROM item WHERE rowid_alias=@rowId AND user_no=@userNumber AND claimed=1",
            new { offset = Offset, rowId = itemNumber - Offset, userNumber });

    /// <summary>Every owned item, oldest first.</summary>
    public IReadOnlyList<ItemRecord> GetOwned(long userNumber) =>
        session.Query<ItemRecord>(
            $"SELECT {ItemColumns} FROM item WHERE user_no=@userNumber AND claimed=1 ORDER BY rowid_alias",
            new { offset = Offset, userNumber });

    /// <summary>The items the last stage start offered that are not picked up yet, oldest first.</summary>
    public IReadOnlyList<ItemRecord> GetUnclaimed(long userNumber) =>
        session.Query<ItemRecord>(
            $"SELECT {ItemColumns} FROM item WHERE user_no=@userNumber AND claimed=0 ORDER BY rowid_alias",
            new { offset = Offset, userNumber });

    /// <summary>Stores a new item (claimed = owned; unclaimed = offered by a stage start) and returns its item number.</summary>
    public long Add(long userNumber, long itemId, bool claimed, ItemStats stats)
    {
        long rowId = session.Insert(
            "INSERT INTO item(user_no, item_id, claimed, attack, defence, hp)"
            + " VALUES(@userNumber, @itemId, @claimed, @attack, @defence, @hp)",
            new { userNumber, itemId, claimed = claimed ? 1 : 0, attack = stats.Attack, defence = stats.Defence, hp = stats.Hp });
        return rowId + Offset;
    }

    /// <summary>Turns an offered item into an owned one. Returns the item, or null when there was none to claim.</summary>
    public ItemRecord? Claim(long userNumber, long itemNumber)
    {
        long rowId = itemNumber - Offset;
        int changed = session.Execute(
            "UPDATE item SET claimed=1 WHERE rowid_alias=@rowId AND user_no=@userNumber", new { rowId, userNumber });
        return changed == 0
            ? null
            : session.QueryFirstOrDefault<ItemRecord>($"SELECT {ItemColumns} FROM item WHERE rowid_alias=@rowId", new { offset = Offset, rowId });
    }

    /// <summary>One upgrade level up, adding the per-level stat gains.</summary>
    public void PowerUp(long itemNumber, ItemStats gain) =>
        session.Execute(
            "UPDATE item SET upgrade=upgrade+1, attack=attack+@attack, defence=defence+@defence, hp=hp+@hp WHERE rowid_alias=@rowId",
            new { attack = gain.Attack, defence = gain.Defence, hp = gain.Hp, rowId = itemNumber - Offset });

    /// <summary>Turns the item into <paramref name="nextItemId"/> at upgrade 0 with that item's base stats.</summary>
    public void Evolve(long itemNumber, long nextItemId, ItemStats baseStats) =>
        session.Execute(
            "UPDATE item SET item_id=@nextItemId, upgrade=0, attack=@attack, defence=@defence, hp=@hp WHERE rowid_alias=@rowId",
            new { nextItemId, attack = baseStats.Attack, defence = baseStats.Defence, hp = baseStats.Hp, rowId = itemNumber - Offset });

    /// <summary>Locks or unlocks an owned item. Returns false when the player owns no such item.</summary>
    public bool SetLocked(long userNumber, long itemNumber, bool locked) =>
        session.Execute(
            "UPDATE item SET locked=@locked WHERE rowid_alias=@rowId AND user_no=@userNumber AND claimed=1",
            new { locked = locked ? 1 : 0, rowId = itemNumber - Offset, userNumber }) > 0;

    /// <summary>Removes an item and its options.</summary>
    public void Delete(long itemNumber)
    {
        session.Execute("DELETE FROM item WHERE rowid_alias=@rowId", new { rowId = itemNumber - Offset });
        session.Execute("DELETE FROM item_option WHERE item_no=@itemNumber", new { itemNumber });
    }

    /// <summary>Removes the items the previous stage start offered and the player did not pick up.</summary>
    public void DeleteUnclaimed(long userNumber)
    {
        session.Execute(
            "DELETE FROM item_option WHERE item_no IN (SELECT rowid_alias + @offset FROM item WHERE user_no=@userNumber AND claimed=0)",
            new { offset = Offset, userNumber });
        session.Execute("DELETE FROM item WHERE user_no=@userNumber AND claimed=0", new { userNumber });
    }

    public IReadOnlyList<ItemOptionRecord> GetOptions(long itemNumber) =>
        session.Query<ItemOptionRecord>(
            "SELECT option_id AS OptionId, value1 AS Value1, value2 AS Value2 FROM item_option WHERE item_no=@itemNumber ORDER BY slot",
            new { itemNumber });

    public void SetOption(long userNumber, long itemNumber, int slot, long optionId, long value1, long value2) =>
        session.Execute(
            $"{session.Dialect.InsertOrReplace} INTO item_option(item_no, slot, user_no, option_id, value1, value2)"
            + " VALUES(@itemNumber, @slot, @userNumber, @optionId, @value1, @value2)",
            new { itemNumber, slot, userNumber, optionId, value1, value2 });

    public HashSet<long> GetGivenUpStarterItems(long userNumber) =>
        [.. session.Query<long>("SELECT item_no FROM starter_sold WHERE user_no=@userNumber", new { userNumber })];

    /// <summary>Records that a starter item was sold or used up as a material.</summary>
    public void MarkStarterItemGivenUp(long userNumber, long itemNumber) =>
        session.Execute("INSERT INTO starter_sold(user_no, item_no) VALUES(@userNumber, @itemNumber)", new { userNumber, itemNumber });
}
