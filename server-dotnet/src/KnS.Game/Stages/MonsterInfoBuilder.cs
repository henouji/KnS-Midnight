using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KnS.Data.Repositories;

namespace KnS.Game.Stages;

/// <summary>
/// The monster stats a stage start sends: without them the client's 13 monster slots stay
/// all-zero and monsters spawn dead (MaxHP 0), never attacking or moving.
/// </summary>
public sealed partial class MonsterInfoBuilder(MasterDataRepository masterData)
{
    /// <summary>One row per client monster slot.</summary>
    public const int MonsterSlots = 13;

    /// <summary>
    /// The keys of a monster_info row, in the order ParsingDataToMonsterData@0x14b2ec reads them.
    /// Every row carries all 47; the client copies a row with key &gt; 0 into slot key % 100.
    /// </summary>
    public static readonly IReadOnlyList<string> RowKeys =
    [
        "key", "Stage",
        "Attack1 Attribute", "Attack1 Value", "Attack1 Attribute Value",
        "Attack1 Mez", "Attack1 Mez Value", "Attack1 Mez Time",
        "Attack2 Attribute", "Attack2 Value", "Attack2 Attribute Value",
        "Attack2 Mez", "Attack2 Mez Value", "Attack2 Mez Time",
        "Attack3 Attribute", "Attack3 Value", "Attack3 Attribute Value",
        "Attack3 Mez", "Attack3 Mez Value", "Attack3 Mez Time",
        "Defence",
        "Fire Registance", "Lightning Registance", "Poison Registance",
        "Physics Immune", "Fire Immune", "Lightning Immune", "Poison Immune",
        "Bloodsucking Immune", "Frozen Immune",
        "Stun Immune", "Atk Debuff Immune", "Def Debuff Immune",
        "MaxHP", "Cooldown(millisec)", "Sight", "AttackNuckback", "IdlePoint(Pixel)",
        "ReflexProb", "AtkProb", "BulletSpd(Sec)", "BulletAttackNum",
        "CommoditiesItemProb", "BornTime(sec)", "BornProb", "MoveSpeed(pixel/ms)",
        "ExplosionTime(sec)",
    ];

    /// <summary>Stats used only for a stage without master_monster data.</summary>
    private static readonly IReadOnlyList<(string Key, long Value)> FallbackStats =
    [
        ("Attack1 Value", 5), ("Defence", 0), ("MaxHP", 10),
        ("Cooldown(millisec)", 1500), ("Sight", 600), ("AtkProb", 50),
        ("BornTime(sec)", 20), ("BornProb", 10), ("MoveSpeed(pixel/ms)", 100),
        ("CommoditiesItemProb", 20),
    ];

    /// <summary>
    /// The 13 monster_info rows of a stage. master_stage_monster gives the stage's monster level
    /// and file (the hard file in hard mode); master_monster holds that file's row per slot and
    /// level. A slot without a row for the exact level uses the closest lower level (boss files
    /// have a single row for most slots).
    /// </summary>
    public IReadOnlyList<JsonObject> Build(long stageId, long stageMode)
    {
        StageMonsterSetup? setup = masterData.GetStageMonsterSetup(stageId);
        var rows = new List<JsonObject>();
        for (int slot = 0; slot < MonsterSlots; slot++)
        {
            var row = new JsonObject();
            foreach (string key in RowKeys)
            {
                row[key] = 0;
            }

            IReadOnlyDictionary<string, object?>? stats = setup is null
                ? null
                : masterData.GetMonsterStats(stageMode == 2 ? setup.HardMonsterFile : setup.NormalMonsterFile, slot, setup.MonsterLevel);
            if (stats is not null)
            {
                foreach (string key in RowKeys.Skip(1))
                {
                    row[key] = ToJson(stats[key == "Stage" ? "level" : ColumnOf(key)]);
                }
            }
            else
            {
                foreach ((string key, long value) in FallbackStats)
                {
                    row[key] = value;
                }

                row["Stage"] = stageId;
            }

            row["key"] = 1000 + slot;
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The master_monster column of a monster_info key: "Cooldown(millisec)" → cooldown_millisec.</summary>
    public static string ColumnOf(string key) => NonAlphanumeric().Replace(key.ToLowerInvariant(), "_").Trim('_');

    private static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        long number => number,
        int number => number,
        double number => number,
        decimal number => number,
        string text => text,
        _ => value.ToString(),
    };

    [GeneratedRegex("[^0-9a-z]+")]
    private static partial Regex NonAlphanumeric();
}
