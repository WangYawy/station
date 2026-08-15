#!/usr/bin/env bash
#
# Publishes the desktop app for Linux and builds a .deb package with dpkg-deb.
#
# Usage:
#   bash scripts/package-linux.sh [VERSION] [RID] [OUT_DIR]
#     VERSION  3-part numeric version, default 0.1.0
#     RID      linux-x64 (amd64) or linux-arm64 (arm64), default linux-x64
#     OUT_DIR  output directory, default publish/packages
#
# Env:
#   SKIP_PUBLISH=1  reuse existing publish/desktop/<rid> output (dev/testing only)
#
# Output:
#   publish/packages/station-desktop_<version>_<arch>.deb
#
set -euo pipefail

VERSION="${1:-0.1.0}"
RID="${2:-linux-x64}"
OUT_DIR="${3:-publish/packages}"

if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Version must be a 3-part numeric version (e.g. 0.1.0), got: $VERSION" >&2
    exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Station.Desktop/Station.Desktop.UI/Station.Desktop.UI.csproj"
PUBLISH_DIR="$ROOT/publish/desktop/$RID"
OUT_DIR_ABS="$ROOT/$OUT_DIR"

ARCH="amd64"
if [ "$RID" = "linux-arm64" ]; then
    ARCH="arm64"
fi

if [ "${SKIP_PUBLISH:-0}" != "1" ]; then
    echo "==> Publishing $RID"
    dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=none \
        -p:DebugSymbols=false \
        -o "$PUBLISH_DIR"
fi

STAGE="$OUT_DIR_ABS/stage-$RID-$VERSION"
rm -rf "$STAGE"
mkdir -p "$STAGE/DEBIAN" \
    "$STAGE/usr/lib/station-desktop" \
    "$STAGE/usr/bin" \
    "$STAGE/usr/share/applications"

cat > "$STAGE/DEBIAN/control" <<EOF
Package: station-desktop
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: Station Dev Team <dev@station.local>
Description: Audio/video collection station desktop client
EOF

cat > "$STAGE/DEBIAN/conffiles" <<EOF
/usr/lib/station-desktop/appsettings.json
EOF

# Copy publish output, excluding runtime data and debug symbols.
(
    cd "$PUBLISH_DIR"
    find . -type f ! -name '*.pdb' ! -name 'station.db' -print | while IFS= read -r f; do
        mkdir -p "$STAGE/usr/lib/station-desktop/$(dirname "$f")"
        cp -p "$f" "$STAGE/usr/lib/station-desktop/$f"
    done
)

chmod +x "$STAGE/usr/lib/station-desktop/Station.Desktop.UI"
ln -s /usr/lib/station-desktop/Station.Desktop.UI "$STAGE/usr/bin/station-desktop"

cat > "$STAGE/usr/share/applications/station-desktop.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Station Desktop
Name[zh_CN]=采集站桌面端
Comment=Audio/video collection station desktop client
Exec=/usr/bin/station-desktop
Path=/usr/lib/station-desktop
Terminal=false
Categories=Utility;
EOF

mkdir -p "$OUT_DIR_ABS"
DEB="$OUT_DIR_ABS/station-desktop_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "$STAGE" "$DEB"
rm -rf "$STAGE"

echo "DEB: $DEB"
