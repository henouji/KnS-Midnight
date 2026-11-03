#!/usr/bin/env python3
"""Decrypt the fellow ("ally") master data and the shop packages and write the server's seeds.

Offline: reads the files bundled in the decoded APK, no device needed.

  server/seed/master_fellows.csv          dt_fellow_pkg_140 table 0, one 464-byte row per fellow.
                                          fellow_id is int 0, name its English name (text
                                          table 1), grade int 1 (1-6 stars), max_level
                                          int 2 (20 for drawable fellows, 1 for special ones),
                                          resource the string at +264 (sprite name); the rest
                                          stays in raw_hex.
  server/seed/master_shop_packages.csv    dt_shop_pkg_140 table 0: the summon / store packages
                                          the client names in requests (protocol 92 package_id):
                                          price item, price, draws per purchase, group.
  server/seed/master_fellow_upgrades.csv  dt_shop_pkg_140 table 5: one 24-byte row per (grade 1-6,
                                          fellow_upgrade 0-5). sell_price (+0x10) is what
                                          GetFellowSellCostByNo@0xf9318 reads; c2, c3, c5 are
                                          unconfirmed costs (upgrade / evolve, see
                                          GetPriceUpgradeAndEvolve@0xb9f90).
  server/seed/master_fellow_inventory_expansions.csv
                                          dt_shop_pkg_140 table 2: the ally bag expansions, 90
                                          rows like the item bag's table 1 (expansion_level,
                                          price_item_id, price, add_space, total_space, c5):
                                          10000 gold on odd levels, 5 gems on even ones, +3.
                                          Protocol 301 (the "+" of the Ally screen) buys one.
  server/seed/master_fellow_levels.csv    dt_userinfo_pkg_131 table 1: 30 rows (level) of 6
                                          16-byte entries (grade 1-6): level, exp_to_next,
                                          exp_total, c3. GetFellowGrowthDataByLv@0xf60a4 reads
                                          it; exp_to_next is the fellow's exp bar (FellowInfo
                                          +0x30).

Usage:
    python tools/dump_fellows.py [game_res_dir] [seed_dir]
"""
import csv
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools" / "analyze"))
from decrypt_dt import load_names, load_package, package_tables  # noqa: E402


FELLOW_TEXT = 1    # lg_* text table of the fellow names


def cstr(b: bytes) -> str:
    return b.split(b"\0", 1)[0].decode("ascii", "replace")


def main() -> int:
    res = Path(sys.argv[1] if len(sys.argv) > 1 else
               ROOT / "decoded/kns-apktool-probe/assets/common/game_res")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else ROOT / "server/seed")

    _, rows = package_tables(load_package((res / "dt_fellow_pkg_140.dat.jpg").read_bytes()))[0]
    with (out / "master_fellows.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["fellow_id", "name", "grade", "max_level", "c3", "resource", "raw_hex"])
        names = load_names(res, FELLOW_TEXT)
        for raw in rows:
            ints = struct.unpack("<4i", raw[:16])
            w.writerow([ints[0], names.get(ints[0], ""), *ints[1:], cstr(raw[264:296]),
                        raw.hex()])
    print("wrote %d fellows to %s" % (len(rows), out / "master_fellows.csv"))

    _, rows = package_tables(load_package((res / "dt_shop_pkg_140.dat.jpg").read_bytes()))[0]
    with (out / "master_shop_packages.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["package_id", "price_item_id", "price", "draw_count", "package_group"])
        for raw in rows:
            w.writerow(struct.unpack("<5i", raw[:20]))
    print("wrote %d packages to %s" % (len(rows), out / "master_shop_packages.csv"))

    _, rows = package_tables(load_package((res / "dt_shop_pkg_140.dat.jpg").read_bytes()))[5]
    with (out / "master_fellow_upgrades.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["grade", "upgrade", "c2", "c3", "sell_price", "c5"])
        for raw in rows:
            w.writerow(struct.unpack("<6i", raw))
    print("wrote %d rows to %s" % (len(rows), out / "master_fellow_upgrades.csv"))

    _, rows = package_tables(load_package((res / "dt_shop_pkg_140.dat.jpg").read_bytes()))[2]
    path = out / "master_fellow_inventory_expansions.csv"
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["expansion_level", "price_item_id", "price", "add_space", "total_space", "c5"])
        for raw in rows:
            w.writerow(struct.unpack("<6i", raw))
    print("wrote %d rows to %s" % (len(rows), path))

    _, rows = package_tables(load_package((res / "dt_userinfo_pkg_131.dat.jpg").read_bytes()))[1]
    with (out / "master_fellow_levels.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["level", "grade", "exp_to_next", "exp_total", "c3"])
        for raw in rows:
            for grade in range(1, 7):
                level, *rest = struct.unpack("<4i", raw[(grade - 1) * 16:grade * 16])
                w.writerow([level, grade, *rest])
    print("wrote %d rows to %s" % (len(rows) * 6, out / "master_fellow_levels.csv"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
