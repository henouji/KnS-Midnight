#!/usr/bin/env bash
# Build a KnS APK whose every Com2uS backend host points at your mock server.
#
#   tools/build_patched_apk.sh http://192.168.1.50:8080
#
# Requires on PATH: apktool, zipalign + apksigner (Android SDK build-tools), keytool
# (JDK). See docs/build-redirected-apk.md for install notes, or use the GitHub Actions
# workflow (.github/workflows/build.yml) if you don't want to install them locally.
set -euo pipefail

BASE_URL="${1:?usage: build_patched_apk.sh <base_url>  e.g. http://192.168.1.50:8080}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APK="$ROOT/original/Knights+N+Squires_1.4.0.apk"
CLEAN="$ROOT/decoded/kns-clean"
BUILD="$ROOT/build"
KS="$ROOT/release.keystore"

for t in apktool zipalign apksigner keytool; do
  command -v "$t" >/dev/null || { echo "ERROR: '$t' not on PATH (see docs/build-redirected-apk.md)"; exit 1; }
done

echo "== 1/5 re-decode PRISTINE apk -> $CLEAN =="
rm -rf "$CLEAN"
apktool d -f "$APK" -o "$CLEAN"

echo "== 2/5 redirect every com2us host -> $BASE_URL =="
python "$ROOT/patch_urls.py" "$BASE_URL" "$CLEAN"

echo "== 3/5 rebuild apk =="
mkdir -p "$BUILD"
apktool b "$CLEAN" -o "$BUILD/unsigned.apk"

echo "== 4/5 keystore (reused if present) =="
if [ ! -f "$KS" ]; then
  keytool -genkeypair -v -keystore "$KS" -alias kns -keyalg RSA -keysize 2048 \
    -validity 10000 -storepass password -keypass password \
    -dname "CN=KnS-Reloaded, OU=Dev, O=Preservation, L=X, S=X, C=US"
fi

echo "== 5/5 zipalign + sign =="
zipalign -f -v 4 "$BUILD/unsigned.apk" "$BUILD/aligned.apk" >/dev/null
apksigner sign --ks "$KS" --ks-pass pass:password --key-pass pass:password \
  --out "$BUILD/KnS-redirected.apk" "$BUILD/aligned.apk"

echo ""
echo "DONE -> $BUILD/KnS-redirected.apk"
echo "Install:  adb install -r \"$BUILD/KnS-redirected.apk\"   (add --bypass-low-target-sdk-block if refused)"
echo "Then run the mock server on $BASE_URL:  python server/app.py ${BASE_URL##*:}"
