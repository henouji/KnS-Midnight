using System.Text.Json.Serialization;

namespace KnS.Game.Models;

/// <summary>user_info, parsed by ParsingDataToUserInfo@0x14c354 and applied by UpdateUserInfo.</summary>
public sealed record UserInfo
{
    [JsonPropertyName("user_no")] public long UserNumber { get; init; }

    /// <summary>
    /// UpdateUserInfo copies a non-empty value to the player name the result screen shows. Guest
    /// logins store hub_uid "-1", so this carries the nickname.
    /// </summary>
    [JsonPropertyName("user_hub_id")] public string HubId { get; init; } = "";

    [JsonPropertyName("user_nickname")] public string Nickname { get; init; } = "";

    /// <summary>UpdateUserInfo skips the whole update while this is 0 or less.</summary>
    [JsonPropertyName("user_level")] public long Level { get; init; }

    [JsonPropertyName("user_exp")] public long Experience { get; init; }
    [JsonPropertyName("user_max_exp")] public long MaxExperience { get; init; }
    [JsonPropertyName("user_gold")] public long Gold { get; init; }
    [JsonPropertyName("user_cash")] public long Gems { get; init; }
    [JsonPropertyName("user_friendship_point")] public long FriendshipPoints { get; init; }
    [JsonPropertyName("user_fellow_ticket")] public long FellowTickets { get; init; }
    [JsonPropertyName("user_item_ticket")] public long ItemTickets { get; init; }
    [JsonPropertyName("user_pvp_point")] public long PvpPoints { get; init; }
    [JsonPropertyName("user_stamina")] public long Stamina { get; init; }
    [JsonPropertyName("user_max_stamina")] public long MaxStamina { get; init; }
    [JsonPropertyName("user_max_stamina_buff")] public long MaxStaminaBuff { get; init; }
    [JsonPropertyName("user_stamina_time_buff")] public long StaminaTimeBuff { get; init; }
    [JsonPropertyName("user_remind_time_stamina")] public long StaminaRefillSeconds { get; init; }
    [JsonPropertyName("user_new_mail")] public long NewMail { get; init; }

    /// <summary>Free item slots; the client's item cap is this plus <see cref="PurchasedItemSlots"/>.</summary>
    [JsonPropertyName("user_inventory_size")] public long FreeItemSlots { get; init; }

    [JsonPropertyName("user_inventory_size_cash")] public long PurchasedItemSlots { get; init; }
    [JsonPropertyName("user_fellow_inventory_size_cash")] public long PurchasedAllySlots { get; init; }
    [JsonPropertyName("user_fellow_item_inventory_size_cash")] public long PurchasedAllyItemSlots { get; init; }

    /// <summary>The hero the town and stages use (UpdateUserInfo copies it to gameInfo+0xb90).</summary>
    [JsonPropertyName("user_hero_pcid")] public long ActiveHeroPcid { get; init; }

    [JsonPropertyName("hero_attack")] public long HeroAttack { get; init; }
    [JsonPropertyName("hero_defence")] public long HeroDefence { get; init; }
    [JsonPropertyName("hero_hp")] public long HeroHp { get; init; }
    [JsonPropertyName("user_next_shop_update_time")] public long NextShopUpdateTime { get; init; }
}
