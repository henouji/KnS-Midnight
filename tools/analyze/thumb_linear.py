#!/usr/bin/env python3
"""Linear Thumb disassembly of an address range, tolerant of literal pools.

Unlike disasm.py (which stops at the first data word) this uses capstone's skipdata mode, so
huge functions such as NetworkEvent@0x150f18 and JanssonParser@0x15a298 disassemble in one go.
`ldr rX, [pc, #n]` lines get their literal value appended.

Usage:
    python tools/analyze/thumb_linear.py 0x150f18 0x15a294 > ne.txt
    python tools/analyze/thumb_linear.py 0x15a298 0x1619d0 > jp.txt
    python tools/analyze/thumb_linear.py LO HI [lib.so]

Then annotate a slice with tools/analyze/annotate.py.
"""
import struct
import sys
from pathlib import Path

from capstone import Cs, CS_ARCH_ARM, CS_MODE_THUMB

LIB = Path(__file__).resolve().parents[2] / "work" / "pristine_lib" / "libgame.so"


def main() -> int:
    lo, hi = int(sys.argv[1], 16), int(sys.argv[2], 16)
    data = Path(sys.argv[3] if len(sys.argv) > 3 else LIB).read_bytes()
    md = Cs(CS_ARCH_ARM, CS_MODE_THUMB)
    md.skipdata = True
    for i in md.disasm(data[lo:hi], lo):
        line = "  0x%08x:  %-8s %s" % (i.address, i.mnemonic, i.op_str)
        if i.mnemonic == "ldr" and "[pc, #" in i.op_str:
            off = int(i.op_str.split("#")[1].rstrip("]"), 16)
            lit = ((i.address + 4) & ~3) + off
            line += "      ; lit@0x%08x -> 0x%08x" % (lit, struct.unpack_from("<I", data, lit)[0])
        print(line)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
