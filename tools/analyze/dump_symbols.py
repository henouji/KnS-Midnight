#!/usr/bin/env python3
"""Dump ELF symbols (addr, size, type, bind) from a native lib, with filtering.

Usage:
    python tools/analyze/dump_symbols.py <lib.so> [name-substr ...]

With no filter it prints a summary + all FUNC symbols. With substrings it prints
only matching symbols (case-insensitive). Handy for locating crypto/network funcs:
    python tools/analyze/dump_symbols.py libgame.so Encrypt Decrypt Sign Signature
"""
import sys
from pathlib import Path

from elftools.elf.elffile import ELFFile
from elftools.elf.sections import SymbolTableSection


def iter_symbols(elf):
    for sec in elf.iter_sections():
        if not isinstance(sec, SymbolTableSection):
            continue
        for sym in sec.iter_symbols():
            yield sec.name, sym


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    lib = Path(sys.argv[1])
    filters = [s.lower() for s in sys.argv[2:]]

    with lib.open("rb") as f:
        elf = ELFFile(f)
        print(f"# {lib.name}: {elf.get_machine_arch()}  "
              f"class={elf.elfclass}  endian={'little' if elf.little_endian else 'big'}")
        rows = []
        tables = {}
        for tbl, sym in iter_symbols(elf):
            tables[tbl] = tables.get(tbl, 0) + 1
            info = sym['st_info']
            stype, bind = info['type'], info['bind']
            name = sym.name or ""
            if filters and not any(x in name.lower() for x in filters):
                continue
            if not filters and stype != 'STT_FUNC':
                continue
            rows.append((sym['st_value'], sym['st_size'], stype, bind, tbl, name))
        print(f"# symbol tables: {tables}")
        print(f"# {'addr':>10}  {'size':>7}  {'type':<10} {'bind':<11} {'tbl':<9} name")
        for addr, size, stype, bind, tbl, name in sorted(rows):
            print(f"  0x{addr:08x}  {size:>7}  {stype:<10} {bind:<11} {tbl:<9} {name}")
        print(f"# {len(rows)} symbols shown")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
