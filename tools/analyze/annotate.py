#!/usr/bin/env python3
"""Annotate a thumb_linear.py listing: name `bl` targets and resolve string literals.

  - `bl #0x16b26c` gets `<json_object_get>` from the lib's dynamic symbols.
  - `ldr rX, [pc,#n]` + `add rX, pc` pairs get the C string they point at, e.g.
    `add r1, pc   "fellow_info"` -- that is how the JSON keys a protocol case reads show up.

Usage:
    python tools/analyze/annotate.py listing.txt LO HI [lib.so]

Finding a protocol's code (see docs/protocol-gamegw.md): JanssonParser dispatches on the
protocol number with a binary search of `cmp rX, #N` / `bne` / `bl case`; the case body reads
the reply keys. NetworkEvent has the same search for the post-handler that applies the parsed
reply to the game state. For example 92 -> parser 0x15c7b4, post-handler 0x152a2a.
"""
import re
import sys
from pathlib import Path

from elftools.elf.elffile import ELFFile

LIB = Path(__file__).resolve().parents[2] / "work" / "pristine_lib" / "libgame.so"


def main() -> int:
    lo, hi = int(sys.argv[2], 16), int(sys.argv[3], 16)
    lib = Path(sys.argv[4] if len(sys.argv) > 4 else LIB)
    data = lib.read_bytes()
    syms = {s["st_value"] & ~1: s.name
            for s in ELFFile(lib.open("rb")).get_section_by_name(".dynsym").iter_symbols()
            if s["st_info"]["type"] == "STT_FUNC"}
    regs = {}
    for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
        m = re.match(r"\s+0x([0-9a-f]+):\s+(\S+)\s*(.*)", line)
        if not m:
            continue
        addr, mn, ops = int(m.group(1), 16), m.group(2), m.group(3)
        if addr < lo:
            continue
        if addr > hi:
            break
        out = line.rstrip()
        target = re.match(r"#0x([0-9a-f]+)", ops)
        if mn in ("bl", "blx") and target:
            out += "   <%s>" % syms.get(int(target.group(1), 16), "?")
        lit = re.search(r"(r\d+), \[pc.*-> 0x([0-9a-f]+)", ops)
        if mn == "ldr" and lit:
            regs[lit.group(1)] = int(lit.group(2), 16)
        pc_add = re.match(r"(r\d+), pc$", ops)
        if mn == "add" and pc_add and pc_add.group(1) in regs:
            at = (addr + 4 + regs[pc_add.group(1)]) & 0xFFFFFFFF
            text = data[at:at + 40].split(b"\0")[0]
            if text and all(32 <= c < 127 for c in text):
                out += '   "%s"' % text.decode()
        print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
