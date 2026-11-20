#!/bin/bash
set -euo pipefail

RID="$1"
if [ -z "$RID" ]; then
    echo "Usage: $0 <runtime-identifier>"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENTITLEMENTS="${SCRIPT_DIR}/Entitlements.plist"

PUB_RAW="./publish_raw/${RID}"
PUB_DIR="./publish/${RID}"
APP_NAME="KingsValley2.app"
APP_DIR="${PUB_DIR}/${APP_NAME}"
MACOS_DIR="${APP_DIR}/Contents/MacOS"
RES_DIR="${APP_DIR}/Contents/Resources"

# Ad-hoc by default so forks and local builds still produce a launchable bundle.
# CI sets MACOS_SIGN_IDENTITY to a "Developer ID Application: ..." identity.
SIGN_IDENTITY="${MACOS_SIGN_IDENTITY:--}"

echo "Packaging macOS bundle for ${RID} into ${APP_DIR}..."

mkdir -p "${MACOS_DIR}"
mkdir -p "${RES_DIR}"

if [ -d "${PUB_RAW}" ]; then
    cp -R "${PUB_RAW}/"* "${MACOS_DIR}/"
    rm -rf "${PUB_RAW}"
elif [ -d "${PUB_DIR}" ] && [ ! -f "${MACOS_DIR}/KingsValley2" ]; then
    mkdir -p ./tmp_mac_pub
    cp -R "${PUB_DIR}/"* ./tmp_mac_pub/
    rm -rf "${PUB_DIR}"
    mkdir -p "${MACOS_DIR}"
    mkdir -p "${RES_DIR}"
    cp -R ./tmp_mac_pub/* "${MACOS_DIR}/"
    rm -rf ./tmp_mac_pub
fi

# The publish output can contain an empty KingsValley2.app folder. A nested
# bundle without Info.plist makes `codesign --deep` fail with
# "bundle format unrecognized, invalid, or unsuitable".
find "${MACOS_DIR}" -mindepth 1 -maxdepth 1 -type d -name '*.app' -exec rm -rf {} +

# MGCB leaves Content/obj/<pipeline>/... intermediates in the publish output.
# They are build artifacts, not game content, and codesign --deep trips on them.
find "${MACOS_DIR}/Content" -type d -name obj -prune -exec rm -rf {} + 2>/dev/null || true

chmod +x "${MACOS_DIR}/KingsValley2"

cp tools/macos/Info.plist "${APP_DIR}/Contents/Info.plist"

ICON_SRC="src/KingsValley.Desktop/Icon.png"
if [ -f "$ICON_SRC" ]; then
    echo "Generating AppIcon.icns from ${ICON_SRC}..."
    mkdir -p AppIcon.iconset
    sips -z 16 16     "$ICON_SRC" --out AppIcon.iconset/icon_16x16.png
    sips -z 32 32     "$ICON_SRC" --out AppIcon.iconset/icon_16x16@2x.png
    sips -z 32 32     "$ICON_SRC" --out AppIcon.iconset/icon_32x32.png
    sips -z 64 64     "$ICON_SRC" --out AppIcon.iconset/icon_32x32@2x.png
    sips -z 128 128   "$ICON_SRC" --out AppIcon.iconset/icon_128x128.png
    sips -z 256 256   "$ICON_SRC" --out AppIcon.iconset/icon_128x128@2x.png
    sips -z 256 256   "$ICON_SRC" --out AppIcon.iconset/icon_256x256.png
    sips -z 512 512   "$ICON_SRC" --out AppIcon.iconset/icon_256x256@2x.png
    sips -z 512 512   "$ICON_SRC" --out AppIcon.iconset/icon_512x512.png
    sips -z 1024 1024 "$ICON_SRC" --out AppIcon.iconset/icon_512x512@2x.png
    iconutil -c icns AppIcon.iconset -o "${RES_DIR}/AppIcon.icns"
    rm -rf AppIcon.iconset
fi

# The Dock reads CFBundleIconFile from this resource. SDL can override the Dock image with
# the window icon from Icon.bmp, so Program.cs re-applies this named resource at runtime.
if [ ! -f "${RES_DIR}/AppIcon.icns" ]; then
    echo "Warning: ${RES_DIR}/AppIcon.icns is missing; the macOS Dock icon may be blank." >&2
fi

# Remove quarantine/provenance BEFORE signing so the resource seal is built
# from clean inputs. Without this, a fresh download is killed by Gatekeeper.
xattr -cr "${APP_DIR}" 2>/dev/null || true

echo "Signing ${APP_NAME} with: ${SIGN_IDENTITY}"
if [ "${SIGN_IDENTITY}" = "-" ]; then
    # --deep is required: the publish output contains unsigned DLLs/dylibs.
    codesign --force --deep --sign - "${APP_DIR}"
else
    # .NET (non-NativeAOT) needs the JIT entitlement with the hardened runtime.
    # Without it the notarized app can pass validation and still crash on launch.
    # Retry up to 3 times in case Apple's timestamp authority has an intermittent timeout.
    SIGNED=0
    for attempt in 1 2 3; do
        if codesign --force --deep --options runtime --timestamp \
            --entitlements "${ENTITLEMENTS}" \
            --sign "${SIGN_IDENTITY}" "${APP_DIR}"; then
            SIGNED=1
            break
        fi
        echo "codesign attempt $attempt failed, retrying in 5 seconds..."
        sleep 5
    done
    if [ "$SIGNED" -ne 1 ]; then
        echo "codesign failed after 3 attempts."
        exit 1
    fi
fi

echo "Verifying signature..."
codesign --verify --deep --strict --verbose=2 "${APP_DIR}"

echo "macOS App Bundle packaging complete."
