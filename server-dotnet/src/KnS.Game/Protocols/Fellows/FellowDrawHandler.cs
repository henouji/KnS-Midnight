using System.Text.Json.Serialization;
using KnS.Data.Repositories;
using KnS.Game.Fellows;
using KnS.Game.Gateway;
using KnS.Game.Models;
using KnS.Game.Players;
using KnS.Game.Rules;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Fellows;

/// <summary>
/// Protocol 92: ally draw, the gacha (case @0x15c7b4, post-handler @0x152a2a). The package
/// (master_shop_package) sets the price and the draws per purchase. Only gem-priced packages are
/// served; friendship-point and ticket packages are refused.
/// </summary>
public sealed class FellowDrawHandler(
    PlayerDirectory directory,
    PlayerProfileService profiles,
    FellowService fellows,
    MasterDataRepository masterData,
    ILogger<FellowDrawHandler> logger) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        long userNumber = directory.ResolveCurrent(request).UserNumber;
        long packageId = request.Parameters.GetInt64("package_id");
        ShopPackage? package = masterData.GetShopPackage(packageId);
        PlayerAccount account = profiles.GetAccount(userNumber);
        if (package is null || package.PriceItemId != CurrencyItems.Gems || account.Gems < package.Price)
        {
            logger.LogInformation("Draw: package {Package} refused ({Row}, gems {Gems})", packageId, package, account.Gems);
            return ProtocolResult.Refused("cannot draw");
        }

        var drawnNumbers = new HashSet<long>();
        var drawnIds = new HashSet<long>();
        for (long i = 0; i < package.DrawCount; i++)
        {
            long fellowId = fellows.RollDrawnFellow();
            drawnNumbers.Add(fellows.Grant(userNumber, fellowId));
            drawnIds.Add(fellowId);
        }

        profiles.AddCurrency(userNumber, gems: -package.Price);
        logger.LogInformation("Draw: package {Package} -> [{Fellows}] for {Price} gems, gems now {Gems}",
            packageId, string.Join(",", drawnIds), package.Price, profiles.GetAccount(userNumber).Gems);
        return ProtocolResult.Success(new FellowDrawResponse
        {
            Goods = profiles.GetGoods(userNumber),
            DrawnFellows = fellows.GetFellowInfos(userNumber, drawnNumbers),
            FellowBook = fellows.GetBook(userNumber, drawnIds),
        });
    }
}

public sealed record FellowDrawResponse
{
    [JsonPropertyName("update_item_etc_info")] public IReadOnlyList<GoodsEntry> Goods { get; init; } = [];

    /// <summary>The drawn fellows. The draw animation dereferences the first one: an empty list crashes the game.</summary>
    [JsonPropertyName("fellow_info")] public IReadOnlyList<FellowInfo> DrawnFellows { get; init; } = [];

    [JsonPropertyName("fellow_book_info")] public IReadOnlyList<FellowBookEntry> FellowBook { get; init; } = [];
    [JsonPropertyName("social_reward_keys")] public IReadOnlyList<object> SocialRewardKeys { get; init; } = [];
    [JsonPropertyName("update_achievement_info")] public IReadOnlyList<object> Achievements { get; init; } = [];
    [JsonPropertyName("image_file_download_url")] public string ImageFileDownloadUrl { get; init; } = "";
    [JsonPropertyName("story_info")] public StoryInfo Story { get; init; } = new();
    [JsonPropertyName("story_card_infos")] public IReadOnlyList<object> StoryCards { get; init; } = [];
}

public sealed record StoryInfo
{
    [JsonPropertyName("story_id")] public long StoryId { get; init; }
    [JsonPropertyName("quest1_count")] public long Quest1Count { get; init; }
    [JsonPropertyName("quest2_count")] public long Quest2Count { get; init; }
    [JsonPropertyName("quest3_count")] public long Quest3Count { get; init; }
    [JsonPropertyName("quest4_count")] public long Quest4Count { get; init; }
    [JsonPropertyName("story_open_remain_time")] public long OpensInSeconds { get; init; }
}
