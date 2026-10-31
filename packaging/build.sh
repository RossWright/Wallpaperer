#!/usr/bin/env bash
# Build a .deb and an AppImage for Wallpaperer. This does not install either
# package and it does not start the app.
#
# Usage, from the repository root:
#   ./packaging/build.sh
#
# Outputs:
#   dist/wallpaperer_<version>_amd64.deb
#   dist/Wallpaperer-<version>-x86_64.AppImage

set -euo pipefail

VERSION="0.1.0"
DEB_ARCH="amd64"
APPIMAGE_ARCH="x86_64"

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
  echo "error: package build publishes linux-x64 and must run on that machine (found $(uname -s) $(uname -m))." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet was not found on PATH. Install the .NET 10 SDK and run this again." >&2
  exit 1
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "error: dpkg-deb was not found. Install dpkg and run this again." >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Wallpaperer.csproj"
DESKTOP_TEMPLATE="$ROOT/packaging/wallpaperer.desktop"
ICON_SRC="$ROOT/packaging/icons/hicolor"
DIST="$ROOT/dist"
PUBLISH="$(mktemp -d)"
STAGE="$(mktemp -d)"

cleanup() {
  rm -rf "$PUBLISH" "$STAGE"
}
trap cleanup EXIT

if [[ ! -f "$PROJECT" || ! -f "$DESKTOP_TEMPLATE" ]]; then
  echo "error: packaging files are missing next to $ROOT" >&2
  exit 1
fi

echo "Publishing Wallpaperer (linux-x64, self-contained)..."
dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --nologo \
  -p:PublishTrimmed=false \
  -p:PublishSingleFile=false \
  -o "$PUBLISH"

if [[ ! -x "$PUBLISH/Wallpaperer" || ! -f "$PUBLISH/libcoreclr.so" ]]; then
  echo "error: publish did not produce a self-contained Wallpaperer executable." >&2
  exit 1
fi

install_icons() {
  local destination="$1"
  install -D -m 644 "$ICON_SRC/scalable/apps/wallpaperer.svg" \
    "$destination/scalable/apps/wallpaperer.svg"
  local size
  for size in 48x48 64x64 128x128 256x256; do
    install -D -m 644 "$ICON_SRC/$size/apps/wallpaperer.png" \
      "$destination/$size/apps/wallpaperer.png"
  done
}

echo "Building wallpaperer_${VERSION}_${DEB_ARCH}.deb..."
DEB_ROOT="$STAGE/deb"
APP_DIR="$DEB_ROOT/usr/lib/wallpaperer"
mkdir -p "$APP_DIR" "$DEB_ROOT/usr/bin" "$DEB_ROOT/usr/share/applications" "$DEB_ROOT/DEBIAN"
cp -a "$PUBLISH"/. "$APP_DIR"/
chmod -R a+rX "$APP_DIR"
chmod 755 "$APP_DIR/Wallpaperer"
ln -s ../lib/wallpaperer/Wallpaperer "$DEB_ROOT/usr/bin/wallpaperer"
install -m 644 "$DESKTOP_TEMPLATE" "$DEB_ROOT/usr/share/applications/wallpaperer.desktop"
install_icons "$DEB_ROOT/usr/share/icons/hicolor"

INSTALLED_SIZE="$(du -sk "$DEB_ROOT" | cut -f1)"
cat > "$DEB_ROOT/DEBIAN/control" <<EOF
Package: wallpaperer
Version: ${VERSION}
Section: graphics
Priority: optional
Architecture: ${DEB_ARCH}
Maintainer: Pross Co. <RossWright@users.noreply.github.com>
Homepage: https://github.com/RossWright/Wallpaperer
Installed-Size: ${INSTALLED_SIZE}
Depends: libc6 (>= 2.35), libgcc-s1, libstdc++6, libfontconfig1, libssl3t64 | libssl3, libx11-6, libxext6, libxfixes3, libxi6, libxrandr2, libxcursor1, libice6, libsm6, libgl1, libegl1, libgtk-3-0t64 | libgtk-3-0, libglib2.0-0t64 | libglib2.0-0
Description: Arrange pictures across monitors
 Wallpaperer composes one wallpaper across your monitors and applies it
 on the GNOME desktop.
EOF

cat > "$DEB_ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q /usr/share/icons/hicolor || true
  fi
fi
EOF

cat > "$DEB_ROOT/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q /usr/share/icons/hicolor || true
  fi
fi
EOF
chmod 755 "$DEB_ROOT/DEBIAN/postinst" "$DEB_ROOT/DEBIAN/postrm"

mkdir -p "$DIST"
DEB_PATH="$DIST/wallpaperer_${VERSION}_${DEB_ARCH}.deb"
rm -f "$DEB_PATH"
dpkg-deb --root-owner-group --build "$DEB_ROOT" "$DEB_PATH" >/dev/null

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$DEB_ROOT/usr/share/applications/wallpaperer.desktop"
fi

echo "Building Wallpaperer-${VERSION}-${APPIMAGE_ARCH}.AppImage..."
APPDIR="$STAGE/Wallpaperer.AppDir"
mkdir -p "$APPDIR/usr/lib/wallpaperer" "$APPDIR/usr/bin" "$APPDIR/usr/share/applications"
cp -a "$PUBLISH"/. "$APPDIR/usr/lib/wallpaperer"/
chmod -R a+rX "$APPDIR"
chmod 755 "$APPDIR/usr/lib/wallpaperer/Wallpaperer"
ln -s ../lib/wallpaperer/Wallpaperer "$APPDIR/usr/bin/wallpaperer"
install -m 644 "$DESKTOP_TEMPLATE" "$APPDIR/wallpaperer.desktop"
install -m 644 "$DESKTOP_TEMPLATE" "$APPDIR/usr/share/applications/wallpaperer.desktop"
install -m 644 "$ICON_SRC/256x256/apps/wallpaperer.png" "$APPDIR/wallpaperer.png"
install -m 644 "$ICON_SRC/256x256/apps/wallpaperer.png" "$APPDIR/.DirIcon"
install_icons "$APPDIR/usr/share/icons/hicolor"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
set -e
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/wallpaperer/Wallpaperer" "$@"
EOF
chmod 755 "$APPDIR/AppRun"

TOOL_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/wallpaperer"
TOOL="$TOOL_DIR/appimagetool-${APPIMAGE_ARCH}.AppImage"
if [[ ! -x "$TOOL" ]]; then
  mkdir -p "$TOOL_DIR"
  curl -fsSL -o "$TOOL" \
    "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${APPIMAGE_ARCH}.AppImage"
  chmod 755 "$TOOL"
fi

APPIMAGE_PATH="$DIST/Wallpaperer-${VERSION}-${APPIMAGE_ARCH}.AppImage"
rm -f "$APPIMAGE_PATH"
ARCH="$APPIMAGE_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" "$APPDIR" "$APPIMAGE_PATH" >/dev/null
chmod 755 "$APPIMAGE_PATH"

# Stable names are what the website downloads from the latest GitHub Release.
cp -f "$DEB_PATH" "$DIST/wallpaperer_amd64.deb"
cp -f "$APPIMAGE_PATH" "$DIST/Wallpaperer-x86_64.AppImage"

echo
echo "Built:"
echo "  $DEB_PATH"
echo "  $APPIMAGE_PATH"
echo "  $DIST/wallpaperer_amd64.deb"
echo "  $DIST/Wallpaperer-x86_64.AppImage"
echo
echo "Install the package:"
echo "  sudo apt install $DEB_PATH"
echo
echo "Or run the AppImage directly:"
echo "  $APPIMAGE_PATH"
echo
echo "Neither file was installed, and the app was not started."
