#!/usr/bin/env python3
"""Annotate a Thumb function: resolve `ldr rX,[pc,#n]; add rX,pc` string refs."""
import sys, struct
from capstone import Cs, CS_ARCH_ARM, CS_MODE_THUMB, CS_MODE_LITTLE_ENDIAN
from elftools.elf.elffile import ELFFile

class Img:
    def __init__(self, p):
        self.f = open(p, 'rb'); self.elf = ELFFile(self.f)
        self.segs = [(s['p_vaddr'], s['p_offset'], s['p_filesz'])
                     for s in self.elf.iter_segments() if s['p_type'] == 'PT_LOAD']
        self.by_addr = {}
        for sec in self.elf.iter_sections():
            if sec.name in ('.dynsym', '.symtab'):
                for s in sec.iter_symbols():
                    if s.name:
                        self.by_addr.setdefault(s['st_value'] & ~1, s.name)
                        if s.name == SYM: self.sym = (s['st_value'], s['st_size'])  # noqa
    def off(self, v):
        for base, o, fsz in self.segs:
            if base <= v < base + fsz: return o + (v - base)
    def read(self, v, n):
        o = self.off(v)
        if o is None: return b''
        self.f.seek(o); return self.f.read(n)
    def u32(self, v):
        b = self.read(v, 4)
        return struct.unpack('<I', b)[0] if len(b) == 4 else None
    def cstr(self, v, m=96):
        b = self.read(v, m)
        if not b: return None
        i = b.find(b'\x00'); s = b[:i] if i >= 0 else b
        try:
            t = s.decode('ascii')
        except UnicodeDecodeError: return None
        return t if t and all(32 <= ord(c) < 127 for c in t) else None

SYM = sys.argv[2]
img = Img(sys.argv[1])
if SYM.lower().startswith('0x'):
    start = int(SYM, 16) & ~1
    length = int(sys.argv[3]) if len(sys.argv) > 3 else 512
else:
    val, size = img.sym
    start, length = val & ~1, (int(sys.argv[3]) if len(sys.argv) > 3 else size)
md = Cs(CS_ARCH_ARM, CS_MODE_THUMB | CS_MODE_LITTLE_ENDIAN); md.detail = False
code = img.read(start, length)
insns = list(md.disasm(code, start))
# pass 1: record pc-relative literal loads  reg -> literal value
pend = {}
out = []
for ins in insns:
    ann = ''
    op = ins.op_str
    if '[pc' in op and ins.mnemonic.startswith('ldr'):
        reg = op.split(',')[0].strip()
        try:
            imm = op.split('#')[-1].rstrip(']')
            imm = int(imm, 16) if imm.startswith('0x') else int(imm)
        except ValueError:
            imm = None
        if imm is not None:
            la = ((ins.address + 4) & ~3) + imm
            w = img.u32(la)
            pend[reg] = w
            s = img.cstr(w) if w else None
            if s is not None: ann = f'  ; "{s}"'
            elif w is not None and (w & ~1) in img.by_addr: ann = f'  ; <{img.by_addr[w & ~1]}>'
            elif w is not None: ann = f'  ; =0x{w:08x}'
    elif ins.mnemonic in ('mov', 'movs') and ',' in op and not op.split(',')[1].strip().startswith('#'):
        dst, src = (x.strip() for x in op.split(',')[:2])
        if src in pend:
            pend[dst] = pend[src]
        else:
            pend.pop(dst, None)
    elif ins.mnemonic == 'add' and op.endswith(', pc'):
        reg = op.split(',')[0].strip()
        if reg in pend and pend[reg] is not None:
            tgt = ins.address + 4 + pend[reg]
            s = img.cstr(tgt)
            nm = img.by_addr.get(tgt & ~1)
            if s is not None: ann = f'  ; STR@0x{tgt:08x} "{s}"'
            elif nm: ann = f'  ; <{nm}>@0x{tgt:08x}'
            else: ann = f'  ; ->0x{tgt:08x}'
            pend.pop(reg, None)
    tgt = ''
    if ins.mnemonic.startswith(('bl', 'b.', 'bx')) or ins.mnemonic == 'b':
        try:
            t = int(ins.op_str.replace('#', ''), 0) & ~1
            if t in img.by_addr: tgt = f'   ; <{img.by_addr[t]}>'
        except (ValueError, TypeError): pass
    out.append(f'0x{ins.address:08x}:  {ins.mnemonic:<8} {ins.op_str}{tgt}{ann}')
print('\n'.join(out))
