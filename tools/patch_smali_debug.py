#!/usr/bin/env python3
"""Smali-side patches: native-command trace, quit suppression, file-path cache.

These live here rather than in a scratch directory because the decoded tree they are
applied to sits in %TEMP% and the patches are expensive to re-derive.

Usage: python tools/patch_smali_debug.py <decoded_dir_or_smali_dir>

Every patch is idempotent -- already-applied ones are reported and skipped, so this
can be re-run against a tree that is partly patched.

NOTE: apktool writes smali with CRLF line endings. A patcher that normalises to LF
silently matches nothing, which looks exactly like "the pattern changed".
"""
import io
import os
import sys

PKG_DIR = "com/com2us/imhero/normal/freefull/google/global/android/common"

_EOL = {}


def rd(path):
    raw = io.open(path, "rb").read()
    _EOL[path] = "\r\n" if b"\r\n" in raw else "\n"
    return raw.decode("utf-8").replace("\r\n", "\n")


def wr(path, text):
    io.open(path, "wb").write(text.replace("\n", _EOL[path]).encode("utf-8"))


def apply(path, old, new, name, marker):
    """Replace `old` with `new` once; skip if `marker` is already present."""
    s = rd(path)
    if marker in s:
        print(f"[skip]  {name}: already applied")
        return 0
    if s.count(old) != 1:
        print(f"[FAIL]  {name}: pattern found {s.count(old)}x, expected 1")
        return -1
    wr(path, s.replace(old, new))
    print(f"[patch] {name}")
    return 1


# --- 1. trace every native->Java command -------------------------------------
# `d.a(Ld$b;)V` is the single dispatcher that drains the native command queue into
# the Java handlers, so one log line here shows the whole native->Java conversation.
# `d$b` is obfuscated to single letters a..t (ordinals 0..19); name() recovers them.
TRACE_OLD = """.method public static a(Ld$b;)V
    .locals 2

    sput-object p0, Ld;->b:Ld$b;
"""
TRACE_NEW = """.method public static a(Ld$b;)V
    .locals 4

    new-instance v3, Ljava/lang/StringBuilder;

    invoke-direct {v3}, Ljava/lang/StringBuilder;-><init>()V

    const-string v2, "cmd ordinal="

    invoke-virtual {v3, v2}, Ljava/lang/StringBuilder;->append(Ljava/lang/String;)Ljava/lang/StringBuilder;

    invoke-virtual {p0}, Ld$b;->ordinal()I

    move-result v2

    invoke-virtual {v3, v2}, Ljava/lang/StringBuilder;->append(I)Ljava/lang/StringBuilder;

    const-string v2, " name="

    invoke-virtual {v3, v2}, Ljava/lang/StringBuilder;->append(Ljava/lang/String;)Ljava/lang/StringBuilder;

    invoke-virtual {p0}, Ld$b;->name()Ljava/lang/String;

    move-result-object v2

    invoke-virtual {v3, v2}, Ljava/lang/StringBuilder;->append(Ljava/lang/String;)Ljava/lang/StringBuilder;

    invoke-virtual {v3}, Ljava/lang/StringBuilder;->toString()Ljava/lang/String;

    move-result-object v2

    const-string v3, "KNSPROBE"

    invoke-static {v3, v2}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;)I

    sput-object p0, Ld;->b:Ld$b;
"""

# --- 2. suppress the engine quit ---------------------------------------------
# Native command 18 (`d$b->s`) -> activity.finish() + a thread that System.exit(0)s.
# Kept suppressed (and logged with a stack) so that if it ever DOES fire we see who
# asked. As of 2026-10-21 it never fires -- the real killer was a native SIGKILL.
QUIT_OLD = """.method public a()V
    .locals 1

    invoke-static {}, Lcom/com2us/wrapper/kernel/CWrapperKernel;->q()Landroid/app/Activity;

    move-result-object v0

    invoke-virtual {v0}, Landroid/app/Activity;->finish()V

    new-instance v0, Lcom/com2us/wrapper/kernel/CWrapperKernel$b$1;

    invoke-direct {v0, p0}, Lcom/com2us/wrapper/kernel/CWrapperKernel$b$1;-><init>(Lcom/com2us/wrapper/kernel/CWrapperKernel$b;)V

    invoke-virtual {v0}, Lcom/com2us/wrapper/kernel/CWrapperKernel$b$1;->start()V

    return-void
.end method"""
QUIT_NEW = """.method public a()V
    .locals 3

    const-string v0, "KNSPROBE"

    const-string v1, "QUIT command 18 suppressed; dumping stack"

    new-instance v2, Ljava/lang/Exception;

    invoke-direct {v2, v1}, Ljava/lang/Exception;-><init>(Ljava/lang/String;)V

    invoke-static {v0, v1, v2}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;Ljava/lang/Throwable;)I

    return-void
.end method"""

# --- 3. cache GetFilePath ------------------------------------------------------
# GetFilePath() calls getExternalFilesDir(null) on EVERY file operation. That call
# enumerates every mounted volume, so on a device with an SD card the framework tries
# to create the app dir on the SD card too and logs
#   ContextImpl: Failed to ensure /storage/<uuid>/Android/data/<pkg>/files (code -30)
# thousands of times during the addition-data extraction -- a binder round-trip plus a
# thrown exception each time. The returned path is the primary (internal) volume
# either way, so caching it keeps the exact same directory and stops touching the SD
# card at all. Static object fields default to null, so no <clinit> change is needed.
PATH_OLD = """.method public static GetFilePath()Ljava/lang/String;
    .locals 6

    .prologue
    .line 203
    sget-object v3, Lcom/com2us/imhero/normal/freefull/google/global/android/common/CUserDefined;->activity:Landroid/app/Activity;"""
PATH_NEW = """.method public static GetFilePath()Ljava/lang/String;
    .locals 6

    .prologue
    sget-object v3, Lcom/com2us/imhero/normal/freefull/google/global/android/common/CUserDefined;->knsCachedPath:Ljava/lang/String;

    if-eqz v3, :cond_kns_uncached

    return-object v3

    :cond_kns_uncached
    .line 203
    sget-object v3, Lcom/com2us/imhero/normal/freefull/google/global/android/common/CUserDefined;->activity:Landroid/app/Activity;"""

PATH_STORE_OLD = """    invoke-virtual {v3, v4}, Ljava/io/PrintStream;->println(Ljava/lang/String;)V

    .line 219
    return-object v1
.end method"""
PATH_STORE_NEW = """    invoke-virtual {v3, v4}, Ljava/io/PrintStream;->println(Ljava/lang/String;)V

    sput-object v1, Lcom/com2us/imhero/normal/freefull/google/global/android/common/CUserDefined;->knsCachedPath:Ljava/lang/String;

    .line 219
    return-object v1
.end method"""

FIELD_ANCHOR = "# direct methods"
FIELD_DECL = """.field private static knsCachedPath:Ljava/lang/String;


# direct methods"""


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    root = sys.argv[1]
    smali = root if os.path.basename(root) == "smali" else os.path.join(root, "smali")
    if not os.path.isdir(smali):
        sys.exit(f"no smali dir under {root}")

    rc = 0
    rc |= min(0, apply(os.path.join(smali, "d.smali"),
                       TRACE_OLD, TRACE_NEW, "d.a(): native-command trace", "KNSPROBE"))
    rc |= min(0, apply(os.path.join(smali, "com/com2us/wrapper/kernel/CWrapperKernel$b.smali"),
                       QUIT_OLD, QUIT_NEW, "CWrapperKernel$b.a(): quit suppressed", "KNSPROBE"))

    cu = os.path.join(smali, PKG_DIR, "CUserDefined.smali")
    if "knsCachedPath" in rd(cu):
        print("[skip]  CUserDefined.GetFilePath(): already cached")
    else:
        rc |= min(0, apply(cu, FIELD_ANCHOR, FIELD_DECL, "CUserDefined: knsCachedPath field",
                           "knsCachedPath"))
        rc |= min(0, apply(cu, PATH_OLD, PATH_NEW, "GetFilePath(): return cache",
                           "cond_kns_uncached"))
        rc |= min(0, apply(cu, PATH_STORE_OLD, PATH_STORE_NEW, "GetFilePath(): store cache",
                           "sput-object v1, Lcom/com2us/imhero"))
    return 0 if rc == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
