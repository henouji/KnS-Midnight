#!/usr/bin/env python3
"""Dump the equipment item ids the running game has loaded and write them as the
server's seed file (server/seed/master_items.csv).

SUPERSEDED by tools/dump_drops.py, which decrypts dt_item_pkg_140 offline and writes every
column (stats, grade, prices, upgrade data). This memory dump writes only item_id, category and
sell_price; the server would reseed master_item from that narrower file, so keep it for
cross-checking, not for the seed.

The master tables on disk are encrypted, but the client decrypts them into memory. Each
equipment category is a run of 0xa8-byte rows whose first int is the item id, so this finds
each category's first row (id <base>01, next row's id in the same 100000 band) by scanning
the game's writable memory over adb. The app must be debuggable (run-as) and in the lobby.

Columns: item_id, category, sell_price (row +0x9c, what GetItemSellPrice@0xf5c00 returns for
equipment and what the sell popup adds up).

Usage:
    python tools/dump_master_items.py [out.csv]
"""
import struct
import subprocess
import sys

PKG = "com.com2us.imhero.normal.freefull.google.global.android.common"
ROW = 0xA8
SELL_PRICE = 0x9C
CATEGORIES = (200000, 300000, 400000, 700000, 800000, 900000)


def adb(*args: str) -> bytes:
    return subprocess.check_output(["adb", *args], stderr=subprocess.DEVNULL)


def main() -> int:
    out = sys.argv[1] if len(sys.argv) > 1 else "server/seed/master_items.csv"
    pid = adb("shell", "pidof", PKG).decode().strip()
    maps = adb("shell", "run-as", PKG, "cat", "/proc/%s/maps" % pid).decode().splitlines()
    regions = []
    for line in maps:
        f = line.split()
        if f[1].startswith("rw"):
            a, b = (int(x, 16) for x in f[0].split("-"))
            if b - a <= 96 << 20:
                regions.append((a, b))
    needles = {struct.pack("<I", c + 1): c for c in CATEGORIES}
    ids = {}
    for a, b in regions:
        try:
            d = adb("exec-out", "run-as", PKG, "dd", "if=/proc/%s/mem" % pid, "bs=4096",
                    "skip=%d" % (a // 4096), "count=%d" % ((b - a) // 4096))
        except subprocess.CalledProcessError:
            continue
        for needle, base in needles.items():
            if base in ids:
                continue
            i = d.find(needle)
            while i >= 0:
                if i % 4 == 0 and i + ROW + 4 <= len(d):
                    nxt = struct.unpack_from("<I", d, i + ROW)[0]
                    if base < nxt < base + 100000:
                        rows = []
                        for j in range(i, len(d) - ROW + 1, ROW):
                            v = struct.unpack_from("<I", d, j)[0]
                            if not base < v < base + 100000:
                                break
                            rows.append((v, struct.unpack_from("<i", d, j + SELL_PRICE)[0]))
                        ids[base] = rows
                        break
                i = d.find(needle, i + 4)
    with open(out, "w", newline="") as f:
        f.write("item_id,category,sell_price\n")
        for base in CATEGORIES:
            for item_id, price in ids.get(base, []):
                f.write("%d,%d,%d\n" % (item_id, base, price))
    print("wrote %d ids to %s" % (sum(map(len, ids.values())), out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
