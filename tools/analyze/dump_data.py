#!/usr/bin/env python3
"""Hex+ascii dump of bytes at a virtual address, and locate ASCII strings by value.

Usage:
    python tools/analyze/dump_data.py <lib.so> <0xVADDR> [len]      # hexdump at vaddr
    python tools/analyze/dump_data.py <lib.so> --find <substr>      # find string + vaddr
    python tools/analyze/dump_data.py <lib.so> --deref <0xVADDR>    # read ptr, dump target
"""
import sys
import struct
from pathlib import Path

from elftools.elf.elffile import ELFFile


class Image:
    def __init__(self, path):
        self.f = open(path, "rb")
        self.elf = ELFFile(self.f)
        self.segs = []
        for seg in self.elf.iter_segments():
            if seg['p_type'] == 'PT_LOAD':
                self.segs.append((seg['p_vaddr'], seg['p_offset'], seg['p_filesz']))

    def off(self, vaddr):
        for v, o, fsz in self.segs:
            if v <= vaddr < v + fsz:
                return o + (vaddr - v)
        return None

    def read(self, vaddr, n):
        o = self.off(vaddr)
        if o is None:
            return None
        self.f.seek(o)
        return self.f.read(n)

    def whole(self):
        self.f.seek(0)
        return self.f.read()


def hexdump(data, base):
    for i in range(0, len(data), 16):
        chunk = data[i:i + 16]
        hx = " ".join(f"{b:02x}" for b in chunk)
        asc = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
        print(f"  0x{base + i:08x}  {hx:<47}  {asc}")


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    img = Image(sys.argv[1])

    if sys.argv[2] == "--find":
        needle = sys.argv[3].encode()
        blob = img.whole()
        # map file offset back to a vaddr
        start = 0
        while True:
            i = blob.find(needle, start)
            if i < 0:
                break
            vaddr = None
            for v, o, fsz in img.segs:
                if o <= i < o + fsz:
                    vaddr = v + (i - o)
                    break
            # show the full NUL-terminated string
            end = blob.find(b"\x00", i)
            s = blob[i:end] if end >= 0 else blob[i:i + 48]
            va = f"0x{vaddr:08x}" if vaddr is not None else "(no vaddr)"
            print(f"  file@0x{i:08x}  vaddr={va}  {s!r}")
            start = i + 1
        return 0

    if sys.argv[2] == "--deref":
        vaddr = int(sys.argv[3], 0)
        ptr = struct.unpack("<I", img.read(vaddr, 4))[0]
        print(f"  [0x{vaddr:08x}] = 0x{ptr:08x}")
        data = img.read(ptr, 64)
        if data:
            hexdump(data, ptr)
        return 0

    vaddr = int(sys.argv[2], 0)
    n = int(sys.argv[3], 0) if len(sys.argv) > 3 else 64
    data = img.read(vaddr, n)
    if data is None:
        print(f"  (0x{vaddr:08x} not in a loadable segment)")
        return 1
    hexdump(data, vaddr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
