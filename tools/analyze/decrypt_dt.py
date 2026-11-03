#!/usr/bin/env python3
"""Decrypt / inspect the game's master-data tables (assets/common/game_res/dt_*).

File format (solved 2026-10-22, session 10; C2S_Encryption_Decode@0x179d0c mode 2 as called by
InitHeroGameData@0x10d78c):
  - `*.json.jpg`  -> plaintext JSON, `.jpg` is only a disguise. Dumped as-is.
  - `*.dat.jpg`   -> 12-byte header + AES-128-CBC payload (PKCS7):
        [0:4]  uint32 BE  total file size
        [4:8]  0x00900000 (flag 0x0090 = encrypted)
        [8:12] 0x00000000
        [12:]  AES-128-CBC ciphertext, key DT_KEY, IV DT_IV
    The decrypted bytes are: BE size, flag 0x00a0 (LZMA), 6 more bytes, a pad byte, then a
    standard .lzma ("alone") stream. Decompressed, the file is a *package*:
        [6:8]    BE u16 table count n
        [(i+3)*4] BE i32 offset of table i
        table i  at n*4 + 0x5a + offset: BE u16 row count, BE u16 row size, 2 bytes, then
                 the rows (little-endian ints)
    Verified byte-for-byte against the stage table the running game holds in memory.

DT_KEY is the string the client passes to the decoder (libgame .rodata 0x1cb3a4) and DT_IV
the 16 constant bytes at libgame file offset 0x1d25a4.

    python tools/analyze/decrypt_dt.py <dt_file> [--out file]   decrypt + decompress
    python tools/analyze/decrypt_dt.py <dt_file> --key-hex <hex> [--mode ecb|cbc]   raw AES only
"""
import argparse
import json
import lzma
import struct
import sys
from pathlib import Path

try:
    from Crypto.Cipher import AES
except ImportError:
    AES = None

HEADER = 12
DT_KEY = b"ImHeroDataEncryp"
DT_IV = bytes.fromhex("de295106d3872f949783942fdc9f01e2")


def parse_header(raw: bytes):
    size_be = int.from_bytes(raw[0:4], "big")
    flag = int.from_bytes(raw[4:8], "little")
    zero = int.from_bytes(raw[8:12], "little")
    return size_be, flag, zero


def decrypt(raw: bytes, key: bytes, mode: str) -> bytes:
    if AES is None:
        sys.exit("pycryptodome required: pip install --user pycryptodome")
    payload = raw[HEADER:]
    payload = payload[: len(payload) // 16 * 16]
    if mode == "ecb":
        return AES.new(key, AES.MODE_ECB).decrypt(payload)
    return AES.new(key, AES.MODE_CBC, iv=b"\x00" * 16).decrypt(payload)


def load_package(raw: bytes) -> bytes:
    """Decrypt and decompress a `*.dat.jpg` file into its package bytes."""
    if AES is None:
        sys.exit("pycryptodome required: pip install --user pycryptodome")
    plain = AES.new(DT_KEY, AES.MODE_CBC, DT_IV).decrypt(raw[HEADER:][: (len(raw) - HEADER) // 16 * 16])
    size = int.from_bytes(plain[:4], "big")
    return lzma.decompress(plain[13:size], format=lzma.FORMAT_ALONE)


def package_tables(pkg: bytes) -> list:
    """[(row_size, [row bytes, ...])] for every table of a decompressed package."""
    n = struct.unpack_from(">H", pkg, 6)[0]
    base = n * 4 + 0x5A
    tables = []
    for i in range(n):
        at = base + struct.unpack_from(">i", pkg, (i + 3) * 4)[0]
        rows, size = struct.unpack_from(">HH", pkg, at)
        tables.append((size, [pkg[at + 6 + r * size: at + 6 + (r + 1) * size]
                              for r in range(rows)]))
    return tables


def _table_at(pkg: bytes, t: int) -> int:
    n = struct.unpack_from(">H", pkg, 6)[0]
    return n * 4 + 0x5A + struct.unpack_from(">i", pkg, (t + 3) * 4)[0]


def load_names(res_dir: Path, text_table: int, lang: str = "en") -> dict:
    """{id: text} of one text table of the language packages (what GetItemName@0xfc0ec and
    GetTextStringInPackage@0xf909c read).

    lg_index_140 table t: BE u16 count, 4 bytes, count BE int32 ids. lg_<lang>_140 table t:
    BE u16 count, 4 bytes, count + 1 BE uint32 string offsets (the last is the blob length),
    then the UTF-8 blob. Name tables: 1 fellows (1xxxxx), 2 equipment and 16xxxxx books,
    3 materials (17xxxxx), 4 etc items (19xxxxx), 24 ids 12xxxxx-15xxxxx.
    """
    ipkg = load_package((Path(res_dir) / "lg_index_140.dat.jpg").read_bytes())
    tpkg = load_package((Path(res_dir) / ("lg_%s_140.dat.jpg" % lang)).read_bytes())
    at = _table_at(ipkg, text_table)
    count = struct.unpack_from(">H", ipkg, at)[0]
    ids = struct.unpack_from(">%di" % count, ipkg, at + 6)
    at = _table_at(tpkg, text_table)
    offsets = struct.unpack_from(">%dI" % (count + 1), tpkg, at + 6)
    blob = at + 6 + 4 * (count + 1)
    return {i: tpkg[blob + offsets[k]:blob + offsets[k + 1]].decode("utf-8")
            for k, i in enumerate(ids) if i}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("file", type=Path)
    ap.add_argument("--key-hex")
    ap.add_argument("--mode", choices=["ecb", "cbc"], default="ecb")
    ap.add_argument("--out", type=Path, help="write decrypted bytes here")
    args = ap.parse_args()

    raw = args.file.read_bytes()

    # Plaintext JSON tables (.json.jpg)
    if raw[:1] in (b"{", b"["):
        text = raw.decode("utf-8", "replace").rstrip("\x00").strip()
        try:
            obj = json.loads(text)
            print(json.dumps(obj, indent=2, ensure_ascii=False)[:4000])
            print(f"\n[ok] plaintext JSON, {len(text)} bytes, top keys: {list(obj)}")
        except json.JSONDecodeError:
            print(text[:2000])
        return 0

    size_be, flag, zero = parse_header(raw)
    print(f"[hdr] file={len(raw)}  size_be={size_be}  flag=0x{flag:x}  zero={zero}  "
          f"payload={len(raw) - HEADER} (mod16={(len(raw) - HEADER) % 16})")

    if not args.key_hex:
        pkg = load_package(raw)
        tables = package_tables(pkg)
        print("[ok] package %d bytes, %d tables: %s"
              % (len(pkg), len(tables), [(len(r), sz) for sz, r in tables]))
        if args.out:
            args.out.write_bytes(pkg)
            print(f"[ok] wrote {len(pkg)} bytes -> {args.out}")
        return 0

    out = decrypt(raw, bytes.fromhex(args.key_hex), args.mode)
    if args.out:
        args.out.write_bytes(out)
        print(f"[ok] wrote {len(out)} bytes -> {args.out}")
    else:
        head = out[:256]
        pr = sum(1 for b in head if 32 <= b < 127 or b in (9, 10, 13)) / len(head)
        print(f"[dec] printable={pr:.2f}  head={head[:80]!r}")
        if pr < 0.6:
            print("[hint] low printable ratio -> wrong key/mode, or payload is LZMA "
                  "(lib has LzmaDecode); try decompressing after decrypt.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
