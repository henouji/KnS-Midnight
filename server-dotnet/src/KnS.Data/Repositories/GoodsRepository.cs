namespace KnS.Data.Repositories;

public sealed record GoodsRecord
{
    public long ItemId { get; init; }
    public long Count { get; init; }
}

/// <summary>Counted etc items a player holds (user_goods): tickets, collectibles, ...</summary>
public sealed class GoodsRepository(DataSession session)
{
    public IReadOnlyList<GoodsRecord> GetAll(long userNumber) =>
        session.Query<GoodsRecord>(
            "SELECT item_id AS ItemId, count AS Count FROM user_goods WHERE user_no=@userNumber ORDER BY item_id",
            new { userNumber });

    public void Add(long userNumber, long itemId, long count)
    {
        session.Execute(
            $"{session.Dialect.InsertOrIgnore} INTO user_goods(user_no, item_id, count) VALUES(@userNumber, @itemId, 0)",
            new { userNumber, itemId });
        session.Execute(
            "UPDATE user_goods SET count=count+@count WHERE user_no=@userNumber AND item_id=@itemId",
            new { count, userNumber, itemId });
    }
}
