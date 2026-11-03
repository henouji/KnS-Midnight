#!/usr/bin/env python3
"""Disassemble a function (by symbol name or addr) from an ARM ELF, with literal-pool
annotation so PC-relative loads of keys/strings/constants are resolved inline.

Usage:
    python tools/analyze/disasm.py <lib.so> <symbol|0xADDR> [byte_len]

Thumb vs ARM is inferred from the symbol's low bit (st_value & 1 == Thumb). For a
raw 0xADDR with no symbol, pass the real (even) address; add 't' suffix to force
Thumb, 'a' to force ARM, e.g. 0x179b54t.
"""
import sys
import struct
from pathlib import Path

from capstone import Cs, CS_ARCH_ARM, CS_MODE_ARM, CS_MODE_THUMB, CS_MODE_LITTLE_ENDIAN
from elftools.elf.elffile import ELFFile


class Image:
    def __init__(self, path):
        self.f = open(path, "rb")
        self.elf = ELFFile(self.f)
        self.segs = []  # (vaddr, vsize, offset, filesz)
        for seg in self.elf.iter_segments():
            if seg['p_type'] == 'PT_LOAD':
                self.segs.append((seg['p_vaddr'], seg['p_memsz'],
                                  seg['p_offset'], seg['p_filesz']))
        self.syms = {}      # name -> (value, size)
        self.by_addr = {}   # addr(even) -> name
        for sec in self.elf.iter_sections():
            if sec.name in ('.dynsym', '.symtab'):
                for s in sec.iter_symbols():
                    if s.name:
                        self.syms[s.name] = (s['st_value'], s['st_size'])
                        self.by_addr.setdefault(s['st_value'] & ~1, s.name)

    def vaddr_to_off(self, vaddr):
        for v, vsz, off, fsz in self.segs:
            if v <= vaddr < v + fsz:
                return off + (vaddr - v)
        return None

    def read(self, vaddr, n):
        off = self.vaddr_to_off(vaddr)
        if off is None:
            return None
        self.f.seek(off)
        return self.f.read(n)

    def read_u32(self, vaddr):
        b = self.read(vaddr, 4)
        return struct.unpack("<I", b)[0] if b and len(b) == 4 else None

    def cstr(self, vaddr, maxlen=64):
        b = self.read(vaddr, maxlen)
        if not b:
            return None
        nul = b.find(b"\x00")
        s = b[:nul] if nul >= 0 else b
        try:
            txt = s.decode("utf-8")
            if all(32 <= ord(c) < 127 or c in "\t" for c in txt) and len(txt) >= 1:
                return txt
        except UnicodeDecodeError:
            pass
        return None


def resolve_arg(image, insn):
    """If the insn is a pc-relative literal load, return an annotation string."""
    op = insn.op_str
    # e.g. "r3, [pc, #0x1c]"  (Thumb pc = (addr+4) & ~3)
    if "[pc" not in op:
        return None
    try:
        imm = op.split("#")[-1].rstrip("]")
        imm = int(imm, 16) if imm.startswith("0x") else int(imm)
    except ValueError:
        return None
    thumb = insn._cs.mode & CS_MODE_THUMB
    pc = (insn.address + 4) & ~3 if thumb else (insn.address + 8)
    lit_addr = pc + imm
    word = image.read_u32(lit_addr)
    if word is None:
        return None
    ann = f"lit@0x{lit_addr:08x} -> 0x{word:08x}"
    s = image.cstr(word)
    if s is not None:
        ann += f"  \"{s}\""
    elif word & ~1 in image.by_addr:
        ann += f"  <{image.by_addr[word & ~1]}>"
    return ann


def disasm(image, start_even, length, thumb):
    mode = (CS_MODE_THUMB if thumb else CS_MODE_ARM) | CS_MODE_LITTLE_ENDIAN
    md = Cs(CS_ARCH_ARM, mode)
    md.detail = False
    code = image.read(start_even, length)
    for insn in md.disasm(code, start_even):
        tgt = ""
        # name resolution for bl/b targets
        if insn.mnemonic.startswith(("bl", "b.", "b\t", "bx")) or insn.mnemonic == "b":
            try:
                t = int(insn.op_str.replace("#", ""), 0) & ~1
                if t in image.by_addr:
                    tgt = f"   ; <{image.by_addr[t]}>"
            except (ValueError, TypeError):
                pass
        ann = resolve_arg(image, insn)
        line = f"  0x{insn.address:08x}:  {insn.mnemonic:<8} {insn.op_str}{tgt}"
        if ann:
            line += f"      ; {ann}"
        print(line)


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    image = Image(sys.argv[1])
    tok = sys.argv[2]
    force = None
    # Force suffix (t/a) only applies to raw 0xADDR tokens, never symbol names.
    if tok.lower().startswith("0x") and tok[-1] in "ta":
        force = tok[-1]
        tok = tok[:-1]

    if tok in image.syms:
        val, size = image.syms[tok]
        thumb = bool(val & 1)
        start = val & ~1
        length = int(sys.argv[3]) if len(sys.argv) > 3 else (size or 256)
    else:
        start = int(tok, 0)
        thumb = (start & 1) == 1
        start &= ~1
        length = int(sys.argv[3]) if len(sys.argv) > 3 else 256
    if force == 't':
        thumb = True
    elif force == 'a':
        thumb = False

    print(f"# {sys.argv[1]}  {tok}  @0x{start:08x}  len={length}  mode={'THUMB' if thumb else 'ARM'}")
    disasm(image, start, length, thumb)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
