#!/usr/bin/env python3
"""Decrypt the item option (enchantment) definitions and write the server's seed.

Offline: reads dt_itemoption_pkg_140 from the decoded APK, names from lg_en_140 text table 5.

  server/seed/master_item_options.csv   table 0, one 72-byte row (18 ints) per option:
      option_id     int 0   2000000-2390xxx; (id // 100) % 10000 is the family the client sums
                            per category (GetEquipItemOptionValueByCategory@0xf9d48)
      name          text table 5, a format: "Total Damage +%d%%", "Freeze Time -^t1", ...
                            (rows with tier 0 are group headers, e.g. "<Buffs>")
      tier          int 1   a random roll needs 1 <= tier <= the item's grade (999 = never)
      family        int 2
      c3            int 3   display / effect code (37, 43, ...), not identified
      value1_min, value1_max      ints 4 / 5   DrawItemOption@0xa2a98 prints "[ %d~%d ]"
      c6, c7        ints 6 / 7   not identified (c6 is set on options only special items use)
      value2_min, value2_max      ints 8 / 9   a second range "[ %d~%d, %d~%d ]" when int 8 > 0
      c10           int 10
      allow_200000, allow_400000, allow_300000, c14, allow_700000, allow_800000, allow_900000
                    ints 11-17: which equipment band may roll the option. The client's item
                    debug popup (Scene_PointerReleaseEvent_SC_POPUP_INVEN @0xa43b8) lists, for an
                    item of band 200000 / 300000 / 400000 / 700000 / 800000 / 900000, the rows
                    with int 11 / 13 / 12 / 15 / 16 / 17 set and 1 <= tier <= grade, minus the
                    families the item already has. int 14 is never tested there.

Usage:
    python tools/dump_item_options.py [game_res_dir] [seed_dir]
"""
import csv
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools" / "analyze"))
from decrypt_dt import load_names, load_package, package_tables  # noqa: E402

OPTION_TABLE, OPTION_TEXT = 0, 5
COLUMNS = ["option_id", "name", "tier", "family", "c3", "value1_min", "value1_max", "c6", "c7",
           "value2_min", "value2_max", "c10", "allow_200000", "allow_400000", "allow_300000",
           "c14", "allow_700000", "allow_800000", "allow_900000"]


def main() -> int:
    res = Path(sys.argv[1] if len(sys.argv) > 1 else
               ROOT / "decoded/kns-apktool-probe/assets/common/game_res")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else ROOT / "server/seed")

    tables = package_tables(load_package((res / "dt_itemoption_pkg_140.dat.jpg").read_bytes()))
    names = load_names(res, OPTION_TEXT)
    rows = []
    for raw in tables[OPTION_TABLE][1]:
        v = struct.unpack("<18i", raw[:72])
        rows.append([v[0], names.get(v[0], "")] + list(v[1:]))
    rows.sort()
    path = out / "master_item_options.csv"
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(COLUMNS)
        w.writerows(rows)
    print("wrote %d rows to %s" % (len(rows), path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
