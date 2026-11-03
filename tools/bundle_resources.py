#!/usr/bin/env python3
"""Bundle the 1.4.0 addition resources INTO the APK as assets.

Why: on modern Android the client's download->extract->store pipeline is broken
(extraction leaves <files>/1.4.0/ EMPTY and texture.store 0 bytes), so the lobby
fopen("<files>/1.4.0/<name>") returns NULL -> fseek(NULL) -> SIGABRT.

Fix: the native resource loader C2S_NBCMng_Util_OpenResourceFile falls back to an
APK asset when <files>/1.4.0/<name> is missing:
    OpenResourceFile -> jGetFileDescriptor -> jGetFileDescriptorFromAsset(name)
    -> CResource.getFullPathName(name, ASSETS) -> AssetManager.openFd(path)
The asset path scheme (same one the 1347 base assets already use) is:
    assets/common/game_res/<name>.jpg   -- STORED, uncompressed
The ".jpg" suffix keeps aapt/apktool from compressing it (apktool.yml must list
`jpg` under doNotCompress), which AssetManager.openFd() requires.

So: copy every addition-only file into <tree>/assets/common/game_res/<name>.jpg.
With those present, a wiped/empty /1.4.0/ no longer crashes -- the game reads each
resource straight from the APK. Survives `pm clear`, needs no adb push.

Skipped: files that already ship as base assets (e.g. the 48 dt_* data tables).
Their /1.4.0/ form carries a +108-byte download trailer; the base asset is the
clean form, so we must NOT overwrite it.

Idempotent: re-running only copies missing/size-mismatched files.

Usage:
    python tools/bundle_resources.py <decoded_tree> [archive_1.4.0_dir]

Default archive dir: original/<pkg>/files/1.4.0  (pkg auto-detected from the tree).
After running: apktool b <tree> -> sign -> install. Expect a ~356MB APK.
"""
import os, sys, shutil

ASSET_SUBDIR = os.path.join("assets", "common", "game_res")
DEFAULT_PKG = "com.com2us.imhero.normal.freefull.google.global.android.common"


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    tree = os.path.abspath(sys.argv[1])
    if not os.path.isdir(tree):
        sys.exit(f"decoded tree not found: {tree}")

    # locate the 1.4.0 archive (the correct resident resources)
    if len(sys.argv) >= 3:
        arch = os.path.abspath(sys.argv[2])
    else:
        proj = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        arch = os.path.join(proj, "original", DEFAULT_PKG, "files", "1.4.0")
    if not os.path.isdir(arch):
        sys.exit(f"archive dir not found: {arch}")

    assetdir = os.path.join(tree, ASSET_SUBDIR)
    os.makedirs(assetdir, exist_ok=True)

    # sanity: warn if apktool.yml won't store .jpg uncompressed (openFd needs stored)
    yml = os.path.join(tree, "apktool.yml")
    if os.path.exists(yml):
        with open(yml, encoding="utf-8", errors="replace") as f:
            body = f.read()
        if "jpg" not in body.split("doNotCompress:", 1)[-1][:200]:
            print("WARNING: apktool.yml doNotCompress does not list 'jpg' -- assets may "
                  "be compressed and AssetManager.openFd() will fail. Add '- jpg'.")

    existing_logical = {a[:-4] for a in os.listdir(assetdir) if a.endswith(".jpg")}
    archive = set(os.listdir(arch))
    addition_only = sorted(archive - existing_logical)

    print(f"archive files      : {len(archive)}")
    print(f"already base-assets: {len(existing_logical & archive)} (left untouched)")
    print(f"addition-only      : {len(addition_only)}")

    copied = skipped = 0
    total = 0
    for name in addition_only:
        src = os.path.join(arch, name)
        if not os.path.isfile(src):
            continue
        dst = os.path.join(assetdir, name + ".jpg")
        if os.path.exists(dst) and os.path.getsize(dst) == os.path.getsize(src):
            skipped += 1
            continue
        shutil.copy2(src, dst)
        copied += 1
        total += os.path.getsize(src)

    print(f"copied={copied} skipped(up-to-date)={skipped} bytes={total/1e6:.1f}MB")
    print(f"game_res asset count now: {len(os.listdir(assetdir))}")


if __name__ == "__main__":
    main()
