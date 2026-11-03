#!/usr/bin/env python3
"""Walk a Thumb function's basic blocks from an entry address, in order, and report
the JSON keys it reads (json_object_get args) and the Parsing* helpers it calls.

Usage:
    python tools/analyze/walk_fields.py <lib.so> 0xENTRY [stop_addr ...]

Written for KnS's JanssonParser: each `protocol` case is a chain of basic blocks that
ends by branching to the shared epilogue (0x15a3cc). Linear disassembly misses blocks
placed elsewhere, so we follow branches instead.
"""
import sys
import struct
from collections import OrderedDict

from capstone import Cs, CS_ARCH_ARM, CS_MODE_THUMB, CS_MODE_LITTLE_ENDIAN
from elftools.elf.elffile import ELFFile


class Img:
    def __init__(self, path):
        self.f = open(path, "rb")
        self.elf = ELFFile(self.f)
        self.segs = [(s['p_vaddr'], s['p_offset'], s['p_filesz'])
                     for s in self.elf.iter_segments() if s['p_type'] == 'PT_LOAD']
        self.by_addr = {}
        for sec in self.elf.iter_sections():
            if sec.name in ('.dynsym', '.symtab'):
                for s in sec.iter_symbols():
                    if s.name:
                        self.by_addr.setdefault(s['st_value'] & ~1, s.name)

    def off(self, v):
        for base, o, fsz in self.segs:
            if base <= v < base + fsz:
                return o + (v - base)

    def read(self, v, n):
        o = self.off(v)
        if o is None:
            return b""
        self.f.seek(o)
        return self.f.read(n)

    def u32(self, v):
        b = self.read(v, 4)
        return struct.unpack("<I", b)[0] if len(b) == 4 else None

    def cstr(self, v, m=128):
        b = self.read(v, m)
        if not b:
            return None
        i = b.find(b"\x00")
        s = b[:i] if i >= 0 else b
        try:
            t = s.decode("ascii")
        except UnicodeDecodeError:
            return None
        return t if t and all(32 <= ord(c) < 127 for c in t) else None


def main() -> int:
    img = Img(sys.argv[1])
    entry = int(sys.argv[2], 16) & ~1
    stops = {int(x, 16) & ~1 for x in sys.argv[3:]} or {0x15a3cc}
    md = Cs(CS_ARCH_ARM, CS_MODE_THUMB | CS_MODE_LITTLE_ENDIAN)

    LO, HI = (int(sys.argv[-2], 16), int(sys.argv[-1], 16)) if len(sys.argv) > 4         else (0x15a298, 0x161b78)
    seen, queue, events = set(), [entry], []
    while queue:
        addr = queue.pop(0)
        if addr in seen or addr in stops:
            continue
        seen.add(addr)
        code = img.read(addr, 2048)
        lit = {}          # reg -> literal word
        for ins in md.disasm(code, addr):
            a, mn, op = ins.address, ins.mnemonic, ins.op_str
            if a != addr and a in seen:
                break
            seen.add(a)
            if a in stops:
                break
            if "[pc" in op and mn.startswith("ldr"):
                reg = op.split(",")[0].strip()
                try:
                    imm = op.split("#")[-1].rstrip("]")
                    lit[reg] = img.u32(((a + 4) & ~3) +
                                       (int(imm, 16) if imm.startswith("0x") else int(imm)))
                except ValueError:
                    lit.pop(reg, None)
            elif mn in ("mov", "movs") and "," in op and                     not op.split(",")[1].strip().startswith("#"):
                dst, src = (x.strip() for x in op.split(",")[:2])
                if src in lit:
                    lit[dst] = lit[src]
                else:
                    lit.pop(dst, None)
            elif mn == "add" and op.endswith(", pc"):
                reg = op.split(",")[0].strip()
                if lit.get(reg) is not None:
                    s = img.cstr(a + 4 + lit[reg])
                    if s:
                        events.append(("str", a, s))
                    lit.pop(reg, None)
            elif mn.startswith("blx"):
                tgt = None
                try:
                    tgt = int(op.replace("#", ""), 0) & ~1
                except (ValueError, TypeError):
                    pass
                if tgt is not None:
                    events.append(("call", a, img.by_addr.get(tgt, f"sub_{tgt:x}")))
            elif mn.startswith(("bl", "b.", "b", "bx")):
                try:
                    tgt = int(op.replace("#", ""), 0)
                except (ValueError, TypeError):
                    if mn.startswith("bx"):
                        break
                    continue
                nm = img.by_addr.get(tgt & ~1)
                if nm:
                    events.append(("call", a, nm))
                    # a named helper returns; keep walking
                    if nm in ("abort", "__stack_chk_fail"):
                        break
                    continue
                # unnamed local target: another basic block of this function
                # GCC Thumb-1 uses `BL` as a long *branch* inside a function (LR is
                # already saved on the stack), so capstone's "bl" to an unnamed address
                # inside this function is a tail jump, not a call: follow it and stop.
                if LO <= (tgt & ~1) < HI:
                    queue.append(tgt & ~1)
                    if mn != "bl" or True:
                        break
                break
        # fallthrough handled by linear walk above
    # report in address order
    for kind, a, val in sorted(events, key=lambda e: e[1]):
        print(f"0x{a:08x}  {'KEY ' if kind == 'str' else 'call'}  {val}")
    keys = OrderedDict()
    for kind, a, val in sorted(events, key=lambda e: e[1]):
        if kind == "str":
            keys[val] = None
    print("\n# keys (" + str(len(keys)) + "):")
    print("\n".join(keys))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
