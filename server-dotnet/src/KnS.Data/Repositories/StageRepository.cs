namespace KnS.Data.Repositories;

public sealed record RolledRewardRecord
{
    public long ItemId { get; init; }
    public long Count { get; init; }
}

public sealed record EventOfferRecord
{
    public long ItemNumber { get; init; }
    public long ItemId { get; init; }
    public long Count { get; init; }
}

public sealed record StageClearRecord
{
    public long StageId { get; init; }
    public long StageMode { get; init; }
}

public sealed record AreaClearCountRecord
{
    public long AreaId { get; init; }
    public long NormalClears { get; init; }
    public long HardClears { get; init; }
}

public sealed record AreaRewardClaimRecord
{
    public long AreaId { get; init; }
    public long StageMode { get; init; }
}

/// <summary>
/// Stage progress and the state between a stage start and its result: the rolled reward slots
/// (stage_roll), the offered counted items (stage_event_offer), clears (stage_clear) and claimed
/// area rewards (stage_area_reward).
/// </summary>
public sealed class StageRepository(DataSession session)
{
    public void ClearRolledRewards(long userNumber) =>
        session.Execute("DELETE FROM stage_roll WHERE user_no=@userNumber", new { userNumber });

    public void AddRolledReward(long userNumber, long slot, long itemId, long count) =>
        session.Execute(
            "INSERT INTO stage_roll(user_no, slot, item_id, count) VALUES(@userNumber, @slot, @itemId, @count)",
            new { userNumber, slot, itemId, count });

    /// <summary>The reward slots the last stage start hit, in slot order.</summary>
    public IReadOnlyList<RolledRewardRecord> GetRolledRewards(long userNumber) =>
        session.Query<RolledRewardRecord>(
            "SELECT item_id AS ItemId, count AS Count FROM stage_roll WHERE user_no=@userNumber ORDER BY slot",
            new { userNumber });

    public void ClearEventOffers(long userNumber) =>
        session.Execute("DELETE FROM stage_event_offer WHERE user_no=@userNumber", new { userNumber });

    /// <summary>Records an offered counted item and returns the item number the client reports it by.</summary>
    public long AddEventOffer(long userNumber, long itemId, long count) =>
        session.Insert(
            "INSERT INTO stage_event_offer(user_no, item_id, count) VALUES(@userNumber, @itemId, @count)",
            new { userNumber, itemId, count }) + WireIdentifiers.EventItemNumberOffset;

    public IReadOnlyList<EventOfferRecord> GetEventOffers(long userNumber) =>
        session.Query<EventOfferRecord>(
            "SELECT rowid_alias + @offset AS ItemNumber, item_id AS ItemId, count AS Count FROM stage_event_offer"
            + " WHERE user_no=@userNumber ORDER BY rowid_alias",
            new { offset = WireIdentifiers.EventItemNumberOffset, userNumber });

    public void RecordClear(long userNumber, long stageId, long stageMode) =>
        session.Execute(
            "INSERT INTO stage_clear(user_no, stage_id, stage_mode, clear_count) VALUES(@userNumber, @stageId, @stageMode, 1)"
            + $" {session.Dialect.OnConflictUpdate("user_no, stage_id, stage_mode")} clear_count=clear_count+1",
            new { userNumber, stageId, stageMode });

    public IReadOnlyList<StageClearRecord> GetClears(long userNumber) =>
        session.Query<StageClearRecord>(
            "SELECT stage_id AS StageId, stage_mode AS StageMode FROM stage_clear WHERE user_no=@userNumber"
            + " ORDER BY stage_id, stage_mode",
            new { userNumber });

    /// <summary>Every clear per area and mode; repeats count (the "Clears x/30" bars).</summary>
    public IReadOnlyList<AreaClearCountRecord> GetAreaClearCounts(long userNumber) =>
        session.Query<AreaClearCountRecord>(
            "SELECT a.area_id AS AreaId,"
            + " SUM(CASE WHEN c.stage_mode=1 THEN c.clear_count ELSE 0 END) AS NormalClears,"
            + " SUM(CASE WHEN c.stage_mode!=1 THEN c.clear_count ELSE 0 END) AS HardClears"
            + " FROM stage_clear c JOIN master_stage_area a ON a.stage_id=c.stage_id WHERE c.user_no=@userNumber"
            + " GROUP BY a.area_id ORDER BY a.area_id",
            new { userNumber });

    public IReadOnlyList<AreaRewardClaimRecord> GetClaimedAreaRewards(long userNumber) =>
        session.Query<AreaRewardClaimRecord>(
            "SELECT area_id AS AreaId, stage_mode AS StageMode FROM stage_area_reward WHERE user_no=@userNumber"
            + " ORDER BY area_id, stage_mode",
            new { userNumber });

    public bool HasClaimedAreaReward(long userNumber, long areaId, long stageMode) =>
        session.Exists(
            "SELECT 1 FROM stage_area_reward WHERE user_no=@userNumber AND area_id=@areaId AND stage_mode=@stageMode",
            new { userNumber, areaId, stageMode });

    public void RecordAreaRewardClaim(long userNumber, long areaId, long stageMode) =>
        session.Execute(
            "INSERT INTO stage_area_reward(user_no, area_id, stage_mode) VALUES(@userNumber, @areaId, @stageMode)",
            new { userNumber, areaId, stageMode });
}
