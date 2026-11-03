#!/usr/bin/env python3
"""Decrypt the item and stage master data and write the server's item and drop seeds.

Offline: reads the files bundled in the decoded APK, no device needed. Supersedes the memory
dumps in tools/dump_master_items.py and tools/dump_master_tables.py (etc items): same ids and
values, plus every other column. Every item table carries its English `name` (lg_en_140, see
decrypt_dt.load_names).

  server/seed/master_items.csv          dt_item_pkg_140 tables 0-7, one 168-byte row (42 ints)
                                        per equipment item:
      item_id       int 0
      name          text table 2
      type          int 1   2 weapon, 3 / 4 the other two hero slots, 5-9 other kinds
      category      item_id band (200000, 300000, ...): the equip slot the server keys on
      sub_type      int 2   200 / 201 ... (201xxx = hero-specific variants, hero in c18)
      grade         int 3   the "Item Grade" stars
      attack, attack_up, defence, defence_up, hp, hp_up      ints 7-12
                    the base value is what DrawEquItemStatusByID@0x11e590 prints for an item
                    id; the *_up values are unconfirmed (per-upgrade step or roll range)
      resource      ints 14-17, sprite name
      next_item_id  int 23  the item an upgrade turns it into (-1 = none)
      upgrade_cost  int 24
      material1..6  ints 25-30, upgrade materials (1701001 ... ; -1 = none)
      buy_price     int 38
      sell_price    int 39  row +0x9c, GetItemSellPrice@0xf5c00
      c4 c5 c6 c13 c18-c22 c31-c37 c40 c41    not identified yet
  server/seed/master_etc_items.csv      dt_item_pkg_140 table 10, 48-byte rows of 19xxxxx ids
                                        (potions, gold pile, currencies, tickets): item_id,
                                        name (text table 4), kind, value, c3-c11.
  server/seed/master_materials.csv      dt_item_pkg_140 tables 8 (16xxxxx books) and 9
                                        (17xxxxx upgrade materials), 24-byte rows: item_id,
                                        name (text tables 2 / 3), c1-c5 (not identified;
                                        c3 / c4 look like buy / sell prices).
  server/seed/master_stage_rewards.csv  dt_stage_pkg_140 tables 0, 1, 2, 6 (736-byte stage rows):
                                        ints 118-173 are 14 reward slots of 4 ints
                                        (item_id, chance %, min_count, max_count) -- the
                                        items the Explore screen previews for the stage.
                                        item_id is an equipment id, a fellow id (1xxxxx) or
                                        an etc / material id (17xxxxx, 19xxxxx).
  server/seed/master_stage_attainments.csv  dt_stage_pkg_140 table 5, 52 rows of 8 ints, one per
                                        area (GetStageAttainmentDataByID@0xf4db0): area_id, c1,
                                        then (clears needed, reward item id, reward count) for
                                        normal and for hard mode: 15 clears -> 3 gems on the first
                                        area, 30 clears -> 5 gems on the others. The "Clears x/30"
                                        bar of the Explore screen; protocol 74 claims it.
  server/seed/master_stage_random_rewards.csv  the same rows, ints 178-183 (set on the 25 area
                                        bosses): a random equipment item -- (chance %,
                                        grade_min, grade_max, item_type, min_count,
                                        max_count); item_type is master_item.type (2 =
                                        weapon: the "?" weapon in the boss preview).
                                        Inferred from the pattern, the client code that
                                        reads it is not traced.

Usage:
    python tools/dump_drops.py [game_res_dir] [seed_dir]
"""
import csv
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools" / "analyze"))
from decrypt_dt import load_names, load_package, package_tables  # noqa: E402

ITEM_TABLES = range(8)          # equipment; 8 books, 9 materials, 10 etc items
BOOK_TABLE, MATERIAL_TABLE, ETC_TABLE = 8, 9, 10
EQUIPMENT_TEXT, MATERIAL_TEXT, ETC_TEXT = 2, 3, 4     # lg_* text tables (books are in 2)
STAGE_TABLES = (0, 1, 2, 6)     # 3 is a single duplicate of stage 3000001
REWARD_FIRST, REWARD_SLOTS = 118, 14
RANDOM_REWARD = 178             # ints 178-183
ATTAINMENT_TABLE = 5            # dt_stage_pkg_140: the area-clear rewards, 8-int rows

ITEM_COLUMNS = (["item_id", "name", "type", "category", "sub_type", "grade", "c4", "c5", "c6",
                 "attack", "attack_up", "defence", "defence_up", "hp", "hp_up", "c13",
                 "resource"] + ["c%d" % i for i in range(18, 23)] +
                ["next_item_id", "upgrade_cost"] + ["material%d" % i for i in range(1, 7)] +
                ["c%d" % i for i in range(31, 38)] + ["buy_price", "sell_price", "c40", "c41"])


ETC_COLUMNS = ["item_id", "name", "kind", "value"] + ["c%d" % i for i in range(3, 12)]
MATERIAL_COLUMNS = ["item_id", "name"] + ["c%d" % i for i in range(1, 6)]


def item_row(raw: bytes, names: dict) -> list:
    v = struct.unpack("<42i", raw)
    resource = raw[56:72].split(b"\0", 1)[0].decode("ascii", "replace")
    return ([v[0], names.get(v[0], ""), v[1], v[0] // 100000 * 100000, v[2], v[3], v[4], v[5], v[6]] + list(v[7:14])
            + [resource] + list(v[18:23]) + [v[23], v[24]] + list(v[25:31]) + list(v[31:38])
            + [v[38], v[39], v[40], v[41]])


def write(path: Path, cols: list, rows: list) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(cols)
        w.writerows(rows)
    print("wrote %d rows to %s" % (len(rows), path))


def main() -> int:
    res = Path(sys.argv[1] if len(sys.argv) > 1 else
               ROOT / "decoded/kns-apktool-probe/assets/common/game_res")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else ROOT / "server/seed")

    tables = package_tables(load_package((res / "dt_item_pkg_140.dat.jpg").read_bytes()))
    equipment_names = load_names(res, EQUIPMENT_TEXT)
    rows = sorted(item_row(raw, equipment_names) for t in ITEM_TABLES for raw in tables[t][1])
    write(out / "master_items.csv", ITEM_COLUMNS, rows)

    names = load_names(res, ETC_TEXT)
    rows = []
    for raw in tables[ETC_TABLE][1]:
        v = struct.unpack("<12i", raw)
        rows.append([v[0], names.get(v[0], "")] + list(v[1:]))
    write(out / "master_etc_items.csv", ETC_COLUMNS, sorted(rows))

    names = {**equipment_names, **load_names(res, MATERIAL_TEXT)}
    rows = []
    for t in (BOOK_TABLE, MATERIAL_TABLE):
        for raw in tables[t][1]:
            v = struct.unpack("<6i", raw)
            rows.append([v[0], names.get(v[0], "")] + list(v[1:]))
    write(out / "master_materials.csv", MATERIAL_COLUMNS, sorted(rows))

    tables = package_tables(load_package((res / "dt_stage_pkg_140.dat.jpg").read_bytes()))
    rewards, randoms = [], []
    for t in STAGE_TABLES:
        for raw in tables[t][1]:
            v = struct.unpack("<184i", raw)
            chance, grade_lo, grade_hi, item_type, lo, hi = v[RANDOM_REWARD:RANDOM_REWARD + 6]
            if chance > 0:
                randoms.append([v[0], chance, item_type, grade_lo, grade_hi, lo, hi])
            for slot in range(REWARD_SLOTS):
                item_id, chance, lo, hi = v[REWARD_FIRST + slot * 4:REWARD_FIRST + slot * 4 + 4]
                if item_id > 0:
                    rewards.append([v[0], slot, item_id, chance, lo, hi])
    rewards.sort()
    with (out / "master_stage_rewards.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["stage_id", "slot", "item_id", "chance", "min_count", "max_count"])
        w.writerows(rewards)
    print("wrote %d stage rewards to %s" % (len(rewards), out / "master_stage_rewards.csv"))
    randoms.sort()
    with (out / "master_stage_random_rewards.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["stage_id", "chance", "item_type", "grade_min", "grade_max",
                    "min_count", "max_count"])
        w.writerows(randoms)
    print("wrote %d random rewards to %s" % (len(randoms),
                                             out / "master_stage_random_rewards.csv"))

    rows = sorted(struct.unpack("<8i", raw) for raw in tables[ATTAINMENT_TABLE][1])
    with (out / "master_stage_attainments.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["area_id", "c1", "normal_clears", "normal_reward_item_id",
                    "normal_reward_count", "hard_clears", "hard_reward_item_id",
                    "hard_reward_count"])
        w.writerows(rows)
    print("wrote %d area rewards to %s" % (len(rows), out / "master_stage_attainments.csv"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
