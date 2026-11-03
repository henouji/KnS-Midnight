using System.Text.Json.Serialization;

namespace KnS.Game.Models;

/// <summary>A fellow_info element, parsed by ParsingDataToFellowInfo@0x14d0bc.</summary>
public sealed record FellowInfo
{
    [JsonPropertyName("fellow_no")] public long FellowNumber { get; init; }
    [JsonPropertyName("fellow_equip_weapon")] public long EquippedWeapon { get; init; }
    [JsonPropertyName("fellow_equip_accessory01")] public long EquippedAccessory1 { get; init; }
    [JsonPropertyName("fellow_equip_accessory02")] public long EquippedAccessory2 { get; init; }
    [JsonPropertyName("fellow_id")] public long FellowId { get; init; }
    [JsonPropertyName("fellow_upgrade")] public long Upgrade { get; init; }
    [JsonPropertyName("fellow_exp")] public long Experience { get; init; }
    [JsonPropertyName("fellow_level")] public long Level { get; init; }
    [JsonPropertyName("fellow_max_level")] public long MaxLevel { get; init; }
    [JsonPropertyName("fellow_fail_count")] public long FailCount { get; init; }
    [JsonPropertyName("fellow_awaken_level")] public long AwakenLevel { get; init; }
    [JsonPropertyName("fellow_attack_bonus")] public long AttackBonus { get; init; }
    [JsonPropertyName("fellow_passive_skill_level")] public long PassiveSkillLevel { get; init; }
    [JsonPropertyName("fellow_mission_skill01")] public long MissionSkill1 { get; init; }
    [JsonPropertyName("fellow_mission_skill02")] public long MissionSkill2 { get; init; }
    [JsonPropertyName("fellow_lock")] public long Locked { get; init; }
    [JsonPropertyName("next_fellow_attribute_reset_info")] public AttributeResetInfo NextAttributeReset { get; init; } = new(0);
}

public sealed record AttributeResetInfo([property: JsonPropertyName("AttributeResetValue")] long Value);

/// <summary>A fellow_book_info element: the collection record of one fellow id.</summary>
public sealed record FellowBookEntry(
    [property: JsonPropertyName("fellow_id")] long FellowId,
    [property: JsonPropertyName("recv_reward")] long RewardReceived,
    [property: JsonPropertyName("exp")] long Experience,
    [property: JsonPropertyName("level")] long Level);

/// <summary>A fellow_active_info element (ParsingDataToUserFellowSetting@0x14c790).</summary>
public sealed record ActiveFellowSlot(
    [property: JsonPropertyName("type")] long SetupType,
    [property: JsonPropertyName("fellow_position")] long Position,
    [property: JsonPropertyName("fellow_no")] long FellowNumber);
