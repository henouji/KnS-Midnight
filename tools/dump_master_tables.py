#!/usr/bin/env python3
"""Dump the hero and etc-item master tables the running game has loaded and write them as
the server's seed files (server/seed/master_heroes.csv, server/seed/master_etc_items.csv).

Like tools/dump_master_items.py this reads the decrypted tables out of the game's memory
over adb (debuggable build, run-as); the app must be in the lobby.

  heroes     0xb8-byte rows. GetCharacterDataByID@0xf49fc loads the table owner from the GOT
             slot at libgame+0x1fd2c4: row count at owner+0x1ec8, row pointer at owner+0x1ed4.
  etc items  0x30-byte rows of 19xxxxx ids (potions, gold pile, currencies, tickets). Found by
             scanning writable memory for row 1900001 followed by another 19xxxxx row.
             No longer written: tools/dump_drops.py exports the same rows offline (with names).
  levels     0x28-byte player level rows (GetUserDataByLv@0xf611c): level, exp to next level,
             cumulative exp, ... Found as rows 1, 2, 3 at that stride whose row 1 has
             exp == cumulative exp.
  stages     0x2e0-byte stage rows (GetStageDataByID@0xf5040), the run starting at 3000001.
  areas      stage package 4 (GetStageAreaIDByStageID@0xf4dfc): 0x98-byte rows of world id,
             area_id, area level and 35 stage-id slots, of which slots k with k//7 even and
             k%7 in (1, 3, 5) hold the area's stages. Written one row per stage. The packages
             hang off owner+0x2228 (owner from HERO_GOT): count at +0, blob at +0xc; the blob
             has a big-endian offset table and a big-endian 16-bit row count per package.
  inventory  0x18-byte inventory-expansion rows (table 1 of the package at owner+0x21e0):
             level, price item id, price, slots added, total slots added, c5.

Usage:
    python tools/dump_master_tables.py [seed_dir]
"""
import csv
import re
import struct
import subprocess
import sys
from pathlib import Path

PKG = "com.com2us.imhero.normal.freefull.google.global.android.common"
HERO_GOT = 0x1FD2C4
HERO_ROW = 0xB8
ETC_ROW = 0x30
ETC_FIRST = 1900001

# Hero row layout (int index -> column). Names are what the Characters screen shows or how
# the client uses the field; cN columns are not identified yet (raw_hex keeps every byte).
HERO_INTS = {0: "hero_pcid", 1: "class_id", 10: "attack", 11: "c11", 12: "c12", 13: "c13",
             14: "crit_rate", 15: "max_hp", 20: "c20",
             # default gear AddDataCharacterInfo creates for a new hero (row+0x54/0x58/0x5c)
             21: "weapon_id", 22: "slot300_id", 23: "armor_id", 24: "slot600_id",
             # SC_POPUP_STORECHAR compares gameInfo+0xaf0 (gems) against row+0x68
             26: "price_gems", 27: "c27", 28: "series200", 29: "series300", 30: "series400",
             31: "series700", 32: "series800", 33: "series900", 42: "c42", 43: "c43",
             44: "text_id", 45: "c45"}
# Etc-item row: id, kind (1 potion / gold pile, 2-3 currency, 1000 misc), value (potion
# amount), the rest unidentified.
ETC_COLS = ["item_id", "kind", "value"] + ["c%d" % i for i in range(3, 12)]
LEVEL_ROW = 0x28
# DrawResult reads exp_to_next (+4) for the exp bar; max_stamina matches the HUD's 28 at Lv1.
LEVEL_COLS = ["level", "exp_to_next", "exp_total", "c3", "max_stamina", "c5", "c6", "c7",
              "c8", "c9"]
STAGE_ROW = 0x2E0
STAGE_FIRST = 3000001
# Stage row ints worth a column (the rest stays in raw_hex). Units/meaning are unconfirmed:
# c87 (20/24/28 for stages 1-3) is the exp candidate, c88 the stage that unlocks it.
STAGE_INTS = {0: "stage_id", 65: "c65", 69: "c69", 86: "c86", 87: "c87", 88: "prev_stage",
              105: "c105", 107: "c107", 108: "c108", 109: "c109", 110: "c110", 111: "c111"}


def adb(*args: str) -> bytes:
    return subprocess.check_output(["adb", *args], stderr=subprocess.DEVNULL)


class Mem:
    def __init__(self):
        self.pid = adb("shell", "pidof", PKG).decode().strip()
        self.maps = adb("shell", "run-as", PKG, "cat",
                        "/proc/%s/maps" % self.pid).decode().splitlines()
        self.base = next(int(l.split("-")[0], 16) for l in self.maps
                         if "libgame" in l and " 00000000 " in l)

    def read(self, addr: int, n: int) -> bytes:
        page = addr & ~0xFFF
        cnt = (addr + n - page + 0xFFF) // 0x1000
        d = adb("exec-out", "run-as", PKG, "dd", "if=/proc/%s/mem" % self.pid, "bs=4096",
                "skip=%d" % (page // 4096), "count=%d" % cnt)
        return d[addr - page:addr - page + n]

    def u32(self, addr: int) -> int:
        return struct.unpack("<I", self.read(addr, 4))[0]

    def rw_regions(self):
        for line in self.maps:
            f = line.split()
            if f[1].startswith("rw"):
                a, b = (int(x, 16) for x in f[0].split("-"))
                if b - a <= 96 << 20:
                    yield a, b


def cstr(b: bytes) -> str:
    return b.split(b"\0", 1)[0].decode("ascii", "replace")


def dump_heroes(mem: Mem) -> list:
    owner = mem.u32(mem.base + HERO_GOT)
    count, table = mem.u32(owner + 0x1EC8), mem.u32(owner + 0x1ED4)
    data = mem.read(table, HERO_ROW * count)
    rows = []
    for i in range(count):
        raw = data[i * HERO_ROW:(i + 1) * HERO_ROW]
        ints = struct.unpack("<46i", raw)
        row = {name: ints[idx] for idx, name in HERO_INTS.items()}
        row["resource"] = cstr(raw[8:40])        # e.g. ch_knight, ch_demonhunter
        row["feature"] = cstr(raw[136:168])      # AttackSpeed / Critical
        row["raw_hex"] = raw.hex()
        rows.append(row)
    return rows


def dump_etc_items(mem: Mem) -> list:
    needle = struct.pack("<I", ETC_FIRST)
    for a, b in mem.rw_regions():
        try:
            d = mem.read(a, b - a)
        except subprocess.CalledProcessError:
            continue
        i = d.find(needle)
        while i >= 0:
            if i % 4 == 0 and i + 2 * ETC_ROW <= len(d) \
                    and 1900000 < struct.unpack_from("<I", d, i + ETC_ROW)[0] < 2000000:
                rows = []
                for j in range(i, len(d) - ETC_ROW + 1, ETC_ROW):
                    ints = struct.unpack_from("<12i", d, j)
                    if not 1900000 < ints[0] < 2000000:
                        break
                    rows.append(dict(zip(ETC_COLS, ints)))
                return rows
            i = d.find(needle, i + 4)
    return []


def _scan(mem: Mem, pattern: bytes, check) -> tuple:
    """First (region data, offset) where the regex `pattern` matches and check(d, i) holds."""
    rx = re.compile(pattern, re.S)
    for a, b in mem.rw_regions():
        try:
            d = mem.read(a, b - a)
        except subprocess.CalledProcessError:
            continue
        for m in rx.finditer(d):
            if m.start() % 4 == 0 and check(d, m.start()):
                return d, m.start()
    return b"", -1


def dump_levels(mem: Mem) -> list:
    i32 = lambda n: struct.pack("<i", n)  # noqa: E731
    pad = b".{%d}" % (LEVEL_ROW - 4)
    def cumulative(d, i):   # row 1: exp == total > 1; rows 2-3: total = previous + exp
        r = [struct.unpack_from("<3i", d, i + k * LEVEL_ROW) for k in range(3)]
        return 1 < r[0][1] == r[0][2] and all(r[k][2] == r[k - 1][2] + r[k][1] > r[k - 1][2]
                                               for k in (1, 2))
    d, i = _scan(mem, re.escape(i32(1)) + pad + re.escape(i32(2)) + pad + re.escape(i32(3)),
                 cumulative)
    rows = []
    while i >= 0 and i + LEVEL_ROW <= len(d):
        ints = struct.unpack_from("<10i", d, i)
        if ints[0] != len(rows) + 1:
            break
        rows.append(dict(zip(LEVEL_COLS, ints)))
        i += LEVEL_ROW
    return rows


def dump_stages(mem: Mem) -> list:
    d, i = _scan(mem, re.escape(struct.pack("<i", STAGE_FIRST)),
                 lambda d, i: i + STAGE_ROW + 4 <= len(d)
                 and struct.unpack_from("<i", d, i + STAGE_ROW)[0] == STAGE_FIRST + 1)
    rows = []
    while i >= 0 and i + STAGE_ROW <= len(d):
        raw = d[i:i + STAGE_ROW]
        ints = struct.unpack("<184i", raw)
        if not 3000000 < ints[0] < 4000000:
            break
        row = {name: ints[idx] for idx, name in STAGE_INTS.items()}
        row["map"] = cstr(raw[4:36])                 # e.g. map_castle_e
        row["raw_hex"] = raw.hex()
        rows.append(row)
        i += STAGE_ROW
    return rows


STAGE_PKG = 0x2228
# Inventory expansions: table 1 of the package at owner+0x21e0. The item popup
# (SC_POPUP_ITEM_EQUIP @0xbfcc8) takes row (gameInfo+0xb80 / add_space), checks the wallet
# against price_item_id (1900008 gold / 1900009 gems) and price, and sends protocol 106 with
# expansion_level = row level.
EXPANSION_PKG = 0x21E0
EXPANSION_ROW = 0x18
EXPANSION_COLS = ["expansion_level", "price_item_id", "price", "add_space", "total_space", "c5"]

AREA_ROW = 0x98
AREA_COLS = ["stage_id", "area_id", "world_id", "area_level"]


def stage_package(mem: Mem, idx: int) -> tuple:
    """(row address, row count) of stage package `idx` (GetStageDataInPackage@0xf4d1c)."""
    return package(mem, STAGE_PKG, idx)


def package(mem: Mem, pkg_off: int, idx: int) -> tuple:
    """(row address, row count) of table `idx` of the package at owner+pkg_off."""
    pkg = mem.u32(mem.base + HERO_GOT) + pkg_off
    n, blob = mem.u32(pkg), mem.u32(pkg + 0xC)
    off = struct.unpack(">i", mem.read(blob + (idx + 3) * 4, 4))[0]
    o = blob + n * 4 + 0x5A + off
    return o + 6, struct.unpack(">H", mem.read(o, 2))[0]


def dump_stage_areas(mem: Mem) -> list:
    addr, count = stage_package(mem, 4)
    data = mem.read(addr, AREA_ROW * count)
    rows = []
    for r in range(count):
        ints = struct.unpack_from("<38i", data, r * AREA_ROW)
        for k in range(35):
            stage = ints[3 + k]
            if not (k // 7) & 1 and k % 7 in (1, 3, 5) and 3000000 < stage < 4000000:
                rows.append(dict(zip(AREA_COLS, (stage, ints[1], ints[0], ints[2]))))
    return rows


def dump_inventory_expansions(mem: Mem) -> list:
    addr, count = package(mem, EXPANSION_PKG, 1)
    data = mem.read(addr, EXPANSION_ROW * count)
    return [dict(zip(EXPANSION_COLS, struct.unpack_from("<6i", data, r * EXPANSION_ROW)))
            for r in range(count)]


def write(path: Path, cols: list, rows: list) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        w.writerows(rows)
    print("wrote %d rows to %s" % (len(rows), path))


def main() -> int:
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "server/seed")
    mem = Mem()
    hero_cols = ["hero_pcid", "class_id", "resource", "feature"] + \
        [n for n in HERO_INTS.values() if n not in ("hero_pcid", "class_id")] + ["raw_hex"]
    write(out / "master_heroes.csv", hero_cols, dump_heroes(mem))
    write(out / "master_levels.csv", LEVEL_COLS, dump_levels(mem))
    write(out / "master_stages.csv", ["stage_id", "map"] +
          [n for n in STAGE_INTS.values() if n != "stage_id"] + ["raw_hex"], dump_stages(mem))
    write(out / "master_stage_areas.csv", AREA_COLS, dump_stage_areas(mem))
    write(out / "master_inventory_expansions.csv", EXPANSION_COLS,
          dump_inventory_expansions(mem))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
