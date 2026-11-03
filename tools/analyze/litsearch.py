#!/usr/bin/env python3
"""Find the functions whose literal pool holds a given 32-bit value.

Thumb code loads big constants (struct offsets such as 0x9e8, stage ids, ids like 0x12f for a
protocol number) with `ldr rX, [pc, #n]`, so a value that a function uses shows up as a
4-byte-aligned word inside that function's literal pool. This scans the whole code range for
the words and prints the nearest preceding exported function, which is the fastest way to answer
"who reads gameInfo+0x1510" or "who compares against protocol 0x12f".

Noisy by nature: a value can also be an unrelated constant, and small values (< 0x100) are
usually encoded in the instruction instead. Check a hit with thumb_linear.py + annotate.py.

Usage:
    python tools/analyze/litsearch.py 0x1510 [0x9e8 ...] [--lib libgame.so]

Output: value, literal-pool address, function, function address.
"""
import argparse
import bisect
import struct
from pathlib import Path

from elftools.elf.elffile import ELFFile

LIB = Path(__file__).resolve().parents[2] / "work" / "pristine_lib" / "libgame.so"
CODE_START, CODE_END = 0x40000, 0x1C6000


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("values", nargs="+", help="32-bit values, hex or decimal")
    ap.add_argument("--lib", type=Path, default=LIB)
    args = ap.parse_args()

    data = args.lib.read_bytes()
    with args.lib.open("rb") as f:
        funcs = sorted((s["st_value"] & ~1, s.name)
                       for s in ELFFile(f).get_section_by_name(".dynsym").iter_symbols()
                       if s["st_info"]["type"] == "STT_FUNC" and s["st_value"])
    addrs = [a for a, _ in funcs]
    for text in args.values:
        value = int(text, 0)
        word = struct.pack("<I", value)
        at = data.find(word)
        while at != -1:
            if at % 4 == 0 and CODE_START < at < CODE_END:
                k = bisect.bisect_right(addrs, at) - 1
                print("%#x  %#x  %s  %#x" % (value, at, funcs[k][1], funcs[k][0]))
            at = data.find(word, at + 1)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
