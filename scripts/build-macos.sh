#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.9}"
BUILD_NUMBER="${VERSION##*.}"
if ! [[ "$BUILD_NUMBER" =~ ^[0-9]+$ ]]; then
  BUILD_NUMBER="1"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
ICON_SRC="$REPO_ROOT/src/VGList.Desktop/Assets/Plana/plana-mark.png"
PLIST_TEMPLATE="$REPO_ROOT/installer/macos/Info.plist.template"
FIRST_RUN_SRC="$REPO_ROOT/installer/macos/FirstRun.command"
README_SRC="$REPO_ROOT/installer/macos/README-macOS.txt"

mkdir -p "$DIST_DIR"

build_icon() {
  local icns_out="$1"
  local iconset_dir
  iconset_dir="$(mktemp -d)/Plana.iconset"
  mkdir -p "$iconset_dir"

  if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
    sips -z 16 16     "$ICON_SRC" --out "$iconset_dir/icon_16x16.png" >/dev/null
    sips -z 32 32     "$ICON_SRC" --out "$iconset_dir/icon_16x16@2x.png" >/dev/null
    sips -z 32 32     "$ICON_SRC" --out "$iconset_dir/icon_32x32.png" >/dev/null
    sips -z 64 64     "$ICON_SRC" --out "$iconset_dir/icon_32x32@2x.png" >/dev/null
    sips -z 128 128   "$ICON_SRC" --out "$iconset_dir/icon_128x128.png" >/dev/null
    sips -z 256 256   "$ICON_SRC" --out "$iconset_dir/icon_128x128@2x.png" >/dev/null
    sips -z 256 256   "$ICON_SRC" --out "$iconset_dir/icon_256x256.png" >/dev/null
    sips -z 512 512   "$ICON_SRC" --out "$iconset_dir/icon_256x256@2x.png" >/dev/null
    sips -z 512 512   "$ICON_SRC" --out "$iconset_dir/icon_512x512.png" >/dev/null
    sips -z 1024 1024 "$ICON_SRC" --out "$iconset_dir/icon_512x512@2x.png" >/dev/null
    iconutil -c icns "$iconset_dir" -o "$icns_out"
  else
    cp "$ICON_SRC" "$icns_out"
  fi
  rm -rf "$(dirname "$iconset_dir")"
}

package_arch() {
  local arch="$1"
  local rid="osx-${arch}"
  local bundle_folder="Plana-mac-${arch}"
  local stage_root="$DIST_DIR/$bundle_folder"
  local app_dir="$stage_root/Plana.app"
  local contents_dir="$app_dir/Contents"
  local macos_dir="$contents_dir/MacOS"
  local res_dir="$contents_dir/Resources"
  local zip_path="$DIST_DIR/Plana-mac-${arch}-${VERSION}.zip"

  echo "==> Building Plana for ${rid} (v${VERSION})..."
  rm -rf "$stage_root" "$zip_path"
  mkdir -p "$macos_dir" "$res_dir"

  dotnet publish "$REPO_ROOT/src/VGList.Desktop/VGList.Desktop.csproj" \
    -c Release \
    -f net10.0 \
    -r "$rid" \
    --self-contained true \
    -o "$macos_dir" \
    -p:Version="$VERSION" \
    -p:DebugType=None \
    -p:DebugSymbols=false

  dotnet publish "$REPO_ROOT/src/VGList.Mcp/VGList.Mcp.csproj" \
    -c Release \
    -f net10.0 \
    -r "$rid" \
    --self-contained true \
    -o "$macos_dir" \
    -p:Version="$VERSION" \
    -p:DebugType=None \
    -p:DebugSymbols=false

  find "$macos_dir" -name '*.pdb' -delete

  sed -e "s/__VERSION__/${VERSION}/g" -e "s/__BUILD_NUMBER__/${BUILD_NUMBER}/g" \
    "$PLIST_TEMPLATE" > "$contents_dir/Info.plist"

  build_icon "$res_dir/Plana.icns"

  cp "$FIRST_RUN_SRC" "$stage_root/首次运行.command"
  cp "$README_SRC" "$stage_root/安装说明.txt"
  chmod +x "$stage_root/首次运行.command"
  chmod +x "$macos_dir/VGList" "$macos_dir/VGList.Mcp"
  find "$macos_dir" -name '*.dylib' -exec chmod +x {} +

  if command -v codesign >/dev/null 2>&1; then
    find "$contents_dir" -type f -print0 | while IFS= read -r -d '' file; do
      if file "$file" | grep -q "Mach-O"; then
        codesign --force --sign - "$file" >/dev/null 2>&1 || true
      fi
    done
    codesign --force --deep --sign - "$app_dir" >/dev/null 2>&1 || true
  fi

  if command -v ditto >/dev/null 2>&1; then
    ditto -c -k --sequesterRsrc --keepParent "$stage_root" "$zip_path"
  else
    (cd "$DIST_DIR" && zip -r -y "$(basename "$zip_path")" "$bundle_folder")
  fi

  echo "==> Created: $zip_path"
}

package_arch "arm64"
package_arch "x64"
