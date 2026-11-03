#!/usr/bin/env python3
"""Decrypt the dungeon monster tables (dt_st_*.dat) and write the server's seed files.

Offline: reads the files bundled in the decoded APK, no device needed.

  server/seed/master_monsters.csv       one row per (monster_file, slot, level): the 46 stat
                                        ints of a monster row, columns named after the
                                        protocol-71 monster_info keys (gamegw.MONSTER_KEYS).
                                        A file has 13 tables = the client's 13 monster slots;
                                        rows are indexed by monster level.
  server/seed/master_stage_monsters.csv per stage: monster level (master_stage c69, column
                                        index 69 of the stage row) and the normal / hard
                                        monster file names stored in the stage row (+0x118 /
                                        +0x138; bosses name their own dt_st_boss_<x>).

Usage:
    python tools/dump_monsters.py [game_res_dir] [seed_dir]
"""
import csv
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools" / "analyze"))
sys.path.insert(0, str(ROOT / "server"))
from decrypt_dt import load_package, package_tables  # noqa: E402
from gamegw import MONSTER_KEYS, monster_column  # noqa: E402

LEVEL_INDEX = 69                 # stage row int holding the monster level
NORMAL_NAME, HARD_NAME = 0x118, 0x138
ROW_INTS = len(MONSTER_KEYS) - 1  # every column but `key`


def cstr(b: bytes) -> str:
    return b.split(b"\0", 1)[0].decode("ascii", "replace")


def main() -> int:
    res = Path(sys.argv[1] if len(sys.argv) > 1 else
               ROOT / "decoded/kns-apktool-probe/assets/common/game_res")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else ROOT / "server/seed")
    cols = ["monster_file", "slot"] + [monster_column(k) for k in MONSTER_KEYS[1:]]
    cols[2] = "level"            # the "Stage" column is the monster level
    n = 0
    with (out / "master_monsters.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(cols)
        for path in sorted(res.glob("dt_st_*.dat.jpg")):
            name = path.name[:-len(".dat.jpg")]
            tables = package_tables(load_package(path.read_bytes()))
            if any(size != ROW_INTS * 4 for size, _ in tables):
                print("skipped %s: row size %s is not a monster row" % (name, {s for s, _ in tables}))
                continue
            for slot, (_, rows) in enumerate(tables):
                for raw in rows:
                    w.writerow([name, slot, *struct.unpack("<%di" % ROW_INTS, raw)])
                    n += 1
    print("wrote %d monster rows to %s" % (n, out / "master_monsters.csv"))

    stages = list(csv.DictReader((out / "master_stages.csv").open(encoding="utf-8")))
    with (out / "master_stage_monsters.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["stage_id", "monster_level", "normal_monster_file", "hard_monster_file"])
        for r in stages:
            raw = bytes.fromhex(r["raw_hex"])
            w.writerow([r["stage_id"], struct.unpack_from("<i", raw, LEVEL_INDEX * 4)[0],
                        cstr(raw[NORMAL_NAME:NORMAL_NAME + 32]),
                        cstr(raw[HARD_NAME:HARD_NAME + 32])])
    print("wrote %d stages to %s" % (len(stages), out / "master_stage_monsters.csv"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
