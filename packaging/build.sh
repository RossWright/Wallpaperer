#!/usr/bin/env bash
# Build a Velopack .deb and AppImage for Wallpaperer. This does not install
# either package and it does not start the app.
#
# Usage, from the repository root:
#   ./packaging/build.sh
#
# Outputs:
#   dist/wallpaperer_<version>_amd64.deb
#   dist/Wallpaperer-<version>-x86_64.AppImage

set -euo pipefail

VERSION="1.0.0"
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

chmod -R a+rX "$PUBLISH"
chmod 755 "$PUBLISH/Wallpaperer"

if ! command -v vpk >/dev/null 2>&1; then
  echo "Installing the Velopack vpk tool..."
  dotnet tool update -g vpk
  export PATH="$PATH:$HOME/.dotnet/tools"
fi
if ! command -v vpk >/dev/null 2>&1; then
  echo "error: vpk was not found after install. Add ~/.dotnet/tools to PATH and run this again." >&2
  exit 1
fi

echo "Packing a Velopack AppImage..."
VELOPACK_DIR="$STAGE/velopack"
mkdir -p "$VELOPACK_DIR"
vpk pack \
  --packId Wallpaperer \
  --packVersion "$VERSION" \
  --packDir "$PUBLISH" \
  --mainExe Wallpaperer \
  --channel linux \
  --runtime linux-x64 \
  --icon "$ICON_SRC/256x256/apps/wallpaperer.png" \
  --packTitle Wallpaperer \
  --packAuthors "Pross Co." \
  --categories "Graphics;" \
  --outputDir "$VELOPACK_DIR"

APPIMAGE_BUILT="$(find "$VELOPACK_DIR" -name '*.AppImage' -print -quit)"
if [[ -z "$APPIMAGE_BUILT" ]]; then
  echo "error: vpk did not produce an AppImage in $VELOPACK_DIR" >&2
  exit 1
fi

echo "Building wallpaperer_${VERSION}_${DEB_ARCH}.deb..."
DEB_ROOT="$STAGE/deb"
mkdir -p "$DEB_ROOT/opt/Wallpaperer" "$DEB_ROOT/usr/bin" "$DEB_ROOT/usr/share/applications" "$DEB_ROOT/DEBIAN"
install -m 755 "$APPIMAGE_BUILT" "$DEB_ROOT/opt/Wallpaperer/Wallpaperer.AppImage"
ln -s /opt/Wallpaperer/Wallpaperer.AppImage "$DEB_ROOT/usr/bin/wallpaperer"
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
Recommends: libfuse2t64 | libfuse2
Description: Arrange pictures across monitors
 Wallpaperer composes one wallpaper across your monitors and applies it
 on the GNOME desktop. Updates are applied in the app.
EOF

cat > "$DEB_ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
  chmod 777 /opt/Wallpaperer 2>/dev/null || true
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
find "$DEB_ROOT" -type d -exec chmod 755 {} +
chmod 755 "$DEB_ROOT/opt/Wallpaperer/Wallpaperer.AppImage"
# Velopack replaces the AppImage in place. The directory has to be writable
# by the user who clicks Update available.
chmod 777 "$DEB_ROOT/opt/Wallpaperer"

mkdir -p "$DIST"
DEB_PATH="$DIST/wallpaperer_${VERSION}_${DEB_ARCH}.deb"
rm -f "$DEB_PATH"
dpkg-deb --root-owner-group --build "$DEB_ROOT" "$DEB_PATH" >/dev/null

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$DEB_ROOT/usr/share/applications/wallpaperer.desktop"
fi

APPIMAGE_PATH="$DIST/Wallpaperer-${VERSION}-${APPIMAGE_ARCH}.AppImage"
cp -f "$APPIMAGE_BUILT" "$APPIMAGE_PATH"
chmod 755 "$APPIMAGE_PATH"
find "$VELOPACK_DIR" -name 'releases.*.json' -exec cp -f {} "$DIST"/ \;
find "$VELOPACK_DIR" -name '*.nupkg' -exec cp -f {} "$DIST"/ \;

# Stable names are what the website downloads from the latest GitHub Release.
cp -f "$DEB_PATH" "$DIST/wallpaperer_amd64.deb"
cp -f "$APPIMAGE_PATH" "$DIST/Wallpaperer-x86_64.AppImage"

echo
echo "Built:"
echo "  $DEB_PATH"
echo "  $APPIMAGE_PATH"
echo "  $DIST/wallpaperer_amd64.deb"
echo "  $DIST/Wallpaperer-x86_64.AppImage"
find "$DIST" -maxdepth 1 \( -name 'releases.*.json' -o -name '*.nupkg' \) -print
echo
echo "Install the package:"
echo "  sudo apt install $DEB_PATH"
echo
echo "Or run the AppImage directly:"
echo "  $APPIMAGE_PATH"
echo
echo "Attach the deb, AppImage, releases.linux.json, and nupkg to a GitHub Release"
echo "so an installed copy can show Update available."
echo
echo "Neither file was installed, and the app was not started."
