#!/usr/bin/env python3
"""Find Thumb BL/BLX call sites targeting an address in an ARM ELF.

Usage:
    python tools/analyze/xref.py <lib.so> 0xTARGET

Scans executable PT_LOAD bytes for 32-bit Thumb BL/BLX encodings and decodes their
branch target, so it finds callers without needing a full disassembly pass.
"""
import sys
import struct

from elftools.elf.elffile import ELFFile


def main() -> int:
    path, target = sys.argv[1], int(sys.argv[2], 16) & ~1
    with open(path, "rb") as f:
        elf = ELFFile(f)
        segs = [(s['p_vaddr'], s['p_offset'], s['p_filesz'])
                for s in elf.iter_segments()
                if s['p_type'] == 'PT_LOAD' and s['p_flags'] & 1]
        names = {}
        for sec in elf.iter_sections():
            if sec.name in ('.dynsym', '.symtab'):
                for s in sec.iter_symbols():
                    if s.name and s['st_size']:
                        names[s['st_value'] & ~1] = (s.name, s['st_size'])
        hits = []
        for vaddr, off, size in segs:
            f.seek(off)
            data = f.read(size)
            for i in range(0, len(data) - 3, 2):
                hw1, hw2 = struct.unpack_from("<HH", data, i)
                if (hw1 & 0xF800) != 0xF000 or (hw2 & 0xC000) != 0xC000:
                    continue
                s = (hw1 >> 10) & 1
                j1, j2 = (hw2 >> 13) & 1, (hw2 >> 11) & 1
                imm = ((s << 24) | ((~(j1 ^ s) & 1) << 23) | ((~(j2 ^ s) & 1) << 22)
                       | ((hw1 & 0x3FF) << 12) | ((hw2 & 0x7FF) << 1))
                if s:
                    imm -= 1 << 25
                pc = vaddr + i + 4
                tgt = ((pc & ~3) if not (hw2 & 0x1000) else pc) + imm
                if (tgt & ~1) == target:
                    hits.append(vaddr + i)
    for a in hits:
        owner = ""
        for base, (nm, sz) in names.items():
            if base <= a < base + sz:
                owner = "  in <%s+0x%x>" % (nm, a - base)
                break
        print("0x%08x%s" % (a, owner))
    print("# %d call site(s) to 0x%x" % (len(hits), target))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
