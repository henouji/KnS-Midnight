#!/usr/bin/env python3
"""Neuter native anti-tamper functions in libgame.so that crash on modern Android.

Several Com2uS `Protection::*` checks call system()/posix_spawn to look for hacking
tools; on Android 14+ that path SIGSEGVs (system() for shell commands is blocked),
killing the game right after login in `nativeStartClet`. We overwrite each target's
prologue with Thumb `movs r0,#0 ; bx lr` (return 0 = "clean"), so the check is a no-op.

Usage: python tools/patch_native.py <decoded_dir_or_libgame.so> [extra_symbol ...] [--no-friend-picker]

--no-friend-picker: enter stages (and Try Again) without the "select a friend" popup.
"""
import sys
from pathlib import Path

from elftools.elf.elffile import ELFFile

# Thumb: movs r0, #0 (0x2000) ; bx lr (0x4770)  -> return 0 immediately.
RET0_THUMB = bytes([0x00, 0x20, 0x70, 0x47])

DEFAULT_TARGETS = [
    "_ZN10Protection19isRunningMemoryHackEv",  # confirmed crash (system())
    "_ZN10Protection8isRootedEv",              # root check (also shells out)
    "_ZN10Protection6verifyEv",                # signature/integrity verify
    # Signing-cert check, called from NetworkEvent during the post-login protocol
    # burst: strcmp(Java GetSHA1(), "EA:DD:CD:E7:...") and exit(0) on mismatch. We
    # re-sign with a debug key, so this always mismatched -> a clean exit(0) that
    # looked like the "lobby crash". Stub returns 0 (the success value) without ever
    # calling GetSHA1 or exit. This is the game's own exit, not a system-lib throw;
    # the intermittent libc++abi mutex line was teardown noise during exit().
    "jCheckSHA1",
]

# operator delete / nothrow delete. Each is `push{r4,lr}; cmp r0,#0; beq end; free(r0); end`.
# On Android 11+ the game frees 8-aligned pointers that Scudo (16-aligned chunks) aborts
# on. We rewrite `cmp r0,#0` -> `lsls r4,r0,#28` (Z set iff low 4 bits are 0) and flip
# `beq`->`bne`, so free() runs only for 16-aligned pointers; misaligned ones are leaked
# instead of crashing. r4 is caller-saved here (pushed/popped), so clobbering it is safe.
ALIGN_DELETE_TARGETS = ["_ZdlPv", "_ZdlPvRKSt9nothrow_t"]
_CMP_R0_0 = bytes([0x00, 0x28])       # cmp r0, #0
_LSLS_R4_R0_28 = bytes([0x04, 0x07])  # lsls r4, r0, #28
_BEQ = bytes([0x01, 0xd0])            # beq .+2 (over the free call)
_BNE = bytes([0x01, 0xd1])            # bne .+2

# PLT stubs to neuter outright. `Protection::nativeFinish(int)` spawns
# `Protection::threadExitApplication`, which does sleep(n) -> kill(getpid(), SIGKILL):
# a self-SIGKILL that leaves no crash log, no tombstone and no Java exception, so it
# looks like a clean exit. `kill` has exactly one call site in the whole library, so
# turning its PLT entry into `mov r0,#0 ; bx lr` disables every anti-tamper self-kill
# regardless of which check fired. PLT stubs are ARM-mode, hence ARM encodings.
PLT_RET0_TARGETS = ["kill"]
_ARM_MOV_R0_0 = bytes.fromhex("0000a0e3")  # mov r0, #0
_ARM_BX_LR = bytes.fromhex("1eff2fe1")     # bx lr
_ARM_NOP = bytes.fromhex("0000a0e1")       # mov r0, r0

# --no-friend-picker (opt-in, libgame.so 1.4.0): skip the "select a friend" (SelectHelper)
# popup before a stage. Once the stage's stamina / bag checks pass, both tap handlers either
# request the helper list (protocol 118 {type: 7}) or open the popup (blDrawSelectHelper = 1);
# the popup's Enter button (touch id 1001) then sends protocol 71 with friend_user_no 0 while
# no helper is selected (i32SelectedHelperIndex = -1, set by both scenes' Init). Each patch is
# one Thumb `b` from that decision straight to the popup's Enter code; r4 (GOT base), r5 (0x14c8)
# and r6 (0x1aec) mean the same at both ends and the Enter code reloads everything else.
#   (address, original 2 bytes, branch target, what)
NO_FRIEND_PICKER = [
    (0x7368a, bytes.fromhex("834b"), 0x73d96,
     "Scene_PointerReleaseEvent_SC_LOBBY (Explore map): Enter -> helper popup Enter"),
    (0x6bcb4, bytes.fromhex("404b"), 0x6b614,
     "Scene_PointerReleaseEvent_SC_EVENT (Event Dungeon): Enter -> helper popup Enter"),
    (0xea09e, bytes.fromhex("5746"), 0xe9c2a,
     "Scene_PointerReleaseEvent_SC_RESULT: Try Again -> helper popup Enter"),
]


def _thumb_b(src: int, dst: int) -> bytes:
    """Thumb T2 unconditional branch `b dst` placed at src (range +-2 KB)."""
    off = dst - (src + 4)
    if not -2048 <= off <= 2046 or off & 1:
        raise ValueError(f"b 0x{dst:x} out of range from 0x{src:x}")
    return (0xE000 | ((off >> 1) & 0x7FF)).to_bytes(2, "little")


def find_lib(arg: Path) -> Path:
    if arg.is_file():
        return arg
    hits = list(arg.rglob("libgame.so"))
    if not hits:
        sys.exit(f"no libgame.so under {arg}")
    return hits[0]


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    args = [a for a in sys.argv[2:] if not a.startswith("--")]
    no_friend_picker = "--no-friend-picker" in sys.argv[2:]
    lib = find_lib(Path(sys.argv[1]))
    targets = DEFAULT_TARGETS + args

    with lib.open("rb") as f:
        elf = ELFFile(f)
        segs = [(s['p_vaddr'], s['p_offset'], s['p_filesz'])
                for s in elf.iter_segments() if s['p_type'] == 'PT_LOAD']
        syms = {}
        for sec in elf.iter_sections():
            if sec.name in ('.dynsym', '.symtab'):
                for s in sec.iter_symbols():
                    if s.name:
                        syms[s.name] = s['st_value']
        # imported-symbol -> PLT stub address. ARM .plt is a 20-byte header followed by
        # 12-byte stubs, one per .rel.plt entry, in relocation order.
        plt_addr = {}
        plt_sec = elf.get_section_by_name('.plt')
        rel_plt = elf.get_section_by_name('.rel.plt')
        dynsym = elf.get_section_by_name('.dynsym')
        if plt_sec is not None and rel_plt is not None and dynsym is not None:
            for i, r in enumerate(rel_plt.iter_relocations()):
                nm = dynsym.get_symbol(r['r_info_sym']).name
                if nm:
                    plt_addr[nm] = plt_sec['sh_addr'] + 20 + i * 12

    def vaddr_to_off(vaddr):
        for v, o, fsz in segs:
            if v <= vaddr < v + fsz:
                return o + (vaddr - v)
        return None

    data = bytearray(lib.read_bytes())
    patched = 0
    for name in targets:
        if name not in syms:
            print(f"[skip] {name} not found")
            continue
        va = syms[name] & ~1          # clear Thumb bit
        off = vaddr_to_off(va)
        if off is None:
            print(f"[skip] {name} @0x{va:x} not in a loadable segment")
            continue
        orig = bytes(data[off:off + 4])
        data[off:off + 4] = RET0_THUMB
        print(f"[patch] {name} @0x{va:08x} (off 0x{off:x}): {orig.hex()} -> {RET0_THUMB.hex()}")
        patched += 1

    # Scudo-safe delete: skip free() of misaligned pointers (see ALIGN_DELETE_TARGETS).
    for name in ALIGN_DELETE_TARGETS:
        if name not in syms:
            print(f"[skip] {name} not found")
            continue
        va = syms[name] & ~1
        off = vaddr_to_off(va)
        if off is None:
            continue
        # layout: +0 push{r4,lr}; +2 cmp r0,#0; +4 beq end
        if bytes(data[off + 2:off + 4]) == _CMP_R0_0 and bytes(data[off + 4:off + 6]) == _BEQ:
            data[off + 2:off + 4] = _LSLS_R4_R0_28
            data[off + 4:off + 6] = _BNE
            patched += 1
            print(f"[patch] {name} @0x{va:08x}: skip free() of misaligned (non-16) pointers")
        else:
            print(f"[skip] {name} @0x{va:08x}: unexpected prologue "
                  f"{bytes(data[off+2:off+6]).hex()}")

    # Anti-tamper self-kill: make kill() a no-op (see PLT_RET0_TARGETS).
    for name in PLT_RET0_TARGETS:
        va = plt_addr.get(name)
        if va is None:
            print(f"[skip] PLT {name} not found")
            continue
        off = vaddr_to_off(va)
        if off is None:
            continue
        orig = bytes(data[off:off + 12])
        data[off:off + 12] = _ARM_MOV_R0_0 + _ARM_BX_LR + _ARM_NOP
        print(f"[patch] PLT {name}() @0x{va:08x} (off 0x{off:x}): {orig.hex()} -> no-op")
        patched += 1

    if no_friend_picker:
        for va, orig, dst, what in NO_FRIEND_PICKER:
            off = vaddr_to_off(va)
            new = _thumb_b(va, dst)
            cur = bytes(data[off:off + 2])
            if cur == new:
                print(f"[skip] {what}: already patched")
            elif cur != orig:
                print(f"[skip] {what} @0x{va:08x}: expected {orig.hex()}, found {cur.hex()} (not 1.4.0?)")
            else:
                data[off:off + 2] = new
                patched += 1
                print(f"[patch] {what} @0x{va:08x}: {orig.hex()} -> {new.hex()} (b 0x{dst:x})")

    if patched:
        lib.write_bytes(bytes(data))
    print(f"[done] patched {patched} function(s) in {lib.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
