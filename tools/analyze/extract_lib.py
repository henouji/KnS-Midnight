#!/usr/bin/env python3
"""Extract the pristine native lib(s) from the original KnS APK.

Usage:
    python tools/analyze/extract_lib.py <apk> <out_dir> [member ...]

Always work from the pristine APK in original/ -- the decoded/ tree's libgame.so
has already been URL-patched. Extracted files are untrusted data: this script only
copies bytes out, it never executes anything from the APK.
"""
import sys
import zipfile
from pathlib import Path

DEFAULT_MEMBERS = [
    "lib/armeabi/libgame.so",
    "lib/armeabi/libc2scommon.so",
    "lib/armeabi/libCom2uSAndroid.so",
]


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    apk = Path(sys.argv[1])
    out = Path(sys.argv[2])
    members = sys.argv[3:] or DEFAULT_MEMBERS
    out.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(apk) as z:
        names = set(z.namelist())
        for m in members:
            if m not in names:
                print(f"[skip] {m} not in APK")
                continue
            data = z.read(m)
            dest = out / Path(m).name
            dest.write_bytes(data)
            print(f"[ok] {m} -> {dest} ({len(data):,} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
