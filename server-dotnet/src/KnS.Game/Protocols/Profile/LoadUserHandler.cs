using System.Globalization;
using System.Text.Json.Serialization;
using KnS.Game.Fellows;
using KnS.Game.Access;
using KnS.Game.Gateway;
using KnS.Game.Inventory;
using KnS.Game.Models;
using KnS.Game.Players;

namespace KnS.Game.Protocols.Profile;

/// <summary>Protocol 60: the full player snapshot (case @0x15bc2c).</summary>
public sealed class LoadUserHandler(
    PlayerDirectory directory,
    LastLoadedPlayer lastLoaded,
    PlayerProfileService profiles,
    InventoryService inventory,
    FellowService fellows,
    GameOptions options,
    TimeProvider clock) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        Player player = directory.Resolve(request.Parameters);
        lastLoaded.Set(player);
        DateTimeOffset now = clock.GetUtcNow();
        long userNumber = player.UserNumber;

        return ProtocolResult.Success(new LoadUserResponse
        {
            UserInfo = profiles.BuildUserInfo(player),
            Goods = profiles.GetGoods(userNumber),
            ActiveFellows = fellows.GetActiveSlots(userNumber),
            ServerTime = now.ToUnixTimeSeconds(),
            // ParsingDataToWTime splits this on "- :"; the original server sent its local time.
            ServerDateTime = TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            // Every owned hero: switching heroes looks the pcid up here and crashes on one it never got.
            Heroes = options.EmptyHeroList
                ? []
                : inventory.GetHeroes(userNumber).Select(hero => inventory.BuildHeroInfo(userNumber, hero.HeroNumber, hero.HeroPcid)).ToList(),
            Fellows = fellows.GetFellowInfos(userNumber),
            FellowBook = fellows.GetBook(userNumber),
        });
    }
}

/// <summary>The protocol 60 reply; each part has its own ParsingDataTo* helper in the client.</summary>
public sealed record LoadUserResponse
{
    [JsonPropertyName("user_info")] public required UserInfo UserInfo { get; init; }
    [JsonPropertyName("item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];
    [JsonPropertyName("fellow_active_info")] public IReadOnlyList<ActiveFellowSlot> ActiveFellows { get; init; } = [];
    [JsonPropertyName("server_time")] public long ServerTime { get; init; }
    [JsonPropertyName("last_grade")] public long LastGrade { get; init; }
    [JsonPropertyName("server_date_time")] public string ServerDateTime { get; init; } = "";
    [JsonPropertyName("hero_info")] public IReadOnlyList<HeroInfo> Heroes { get; init; } = [];
    [JsonPropertyName("fellow_info")] public IReadOnlyList<FellowInfo> Fellows { get; init; } = [];
    [JsonPropertyName("fellow_book_info")] public IReadOnlyList<FellowBookEntry> FellowBook { get; init; } = [];
    [JsonPropertyName("daily_reward_info")] public DailyRewardInfo DailyReward { get; init; } = new();
    [JsonPropertyName("buff_etc_info")] public IReadOnlyList<object> Buffs { get; init; } = [];
    [JsonPropertyName("master_pvp_recovery_info")] public PvpRecoveryInfo PvpRecovery { get; init; } = new(0);
}

/// <summary>daily_reward_info, also read by ParsingDataToFriendShipRewardInfo@0x14af84.</summary>
public sealed record DailyRewardInfo
{
    [JsonPropertyName("daily_reward_time")] public long RewardTime { get; init; }
    [JsonPropertyName("daily_reward_count")] public long RewardCount { get; init; }
    [JsonPropertyName("daily_reward_reset_time")] public long ResetTime { get; init; }
    [JsonPropertyName("invite_count")] public long InviteCount { get; init; }
    [JsonPropertyName("invite_time")] public long InviteTime { get; init; }
    [JsonPropertyName("send_count")] public long SendCount { get; init; }
    [JsonPropertyName("recv_count")] public long ReceiveCount { get; init; }
    [JsonPropertyName("friendship_date")] public string FriendshipDate { get; init; } = "";
}

public sealed record PvpRecoveryInfo([property: JsonPropertyName("PvpRecoveryValue")] long Value);
