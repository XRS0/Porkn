#!/usr/bin/env bash
set -euo pipefail

APP_NAME="porkn"
BUNDLE_ID="app.porkn.desktop"
MIN_SYSTEM_VERSION="14.0"
SING_BOX_VERSION="1.13.11"
APP_VERSION="${APP_VERSION:-${GITHUB_REF_NAME:-0.1.0}}"
APP_VERSION="${APP_VERSION#v}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RELEASE_DIR="$ROOT_DIR/release"
DEPS_DIR="$ROOT_DIR/.build/deps"
SOURCE_RESOURCES="$ROOT_DIR/apps/macos/Sources/porkn/Resources"

if [[ -z "${DEVELOPER_DIR:-}" && -d "/Applications/Xcode.app/Contents/Developer" ]]; then
  export DEVELOPER_DIR="/Applications/Xcode.app/Contents/Developer"
fi

mkdir -p "$RELEASE_DIR" "$DEPS_DIR"
rm -rf "$RELEASE_DIR"/*.app "$RELEASE_DIR"/*.zip "$RELEASE_DIR"/SHA256SUMS.txt

ensure_sing_box_amd64() {
  local archive="$DEPS_DIR/sing-box-$SING_BOX_VERSION-darwin-amd64.tar.gz"
  local dir="$DEPS_DIR/sing-box-$SING_BOX_VERSION-darwin-amd64"
  local binary="$dir/sing-box"

  if [[ ! -x "$binary" ]]; then
    rm -rf "$dir" "$DEPS_DIR/sing-box-amd64"
    if [[ ! -f "$archive" ]]; then
      curl -fL --retry 3 --connect-timeout 10 --max-time 120 \
        -o "$archive" \
        "https://github.com/SagerNet/sing-box/releases/download/v$SING_BOX_VERSION/sing-box-$SING_BOX_VERSION-darwin-amd64.tar.gz" || {
        echo "ERROR: failed to download amd64 sing-box. Check network/DNS or pre-seed $archive" >&2
        return 1
      }
    fi
    mkdir -p "$dir"
    tar -xzf "$archive" -C "$dir" --strip-components=1
    if [[ ! -x "$binary" ]]; then
      echo "ERROR: amd64 sing-box binary missing after extracting $archive" >&2
      return 1
    fi
    chmod +x "$binary"
  fi

  printf '%s' "$binary"
}

stage_app() {
  local arch="$1"
  local sing_box_binary="$2"
  local build_bin_path
  local app_bundle="$RELEASE_DIR/$APP_NAME-macos-$arch.app"
  local app_contents="$app_bundle/Contents"
  local app_macos="$app_contents/MacOS"
  local app_resources="$app_contents/Resources"
  local app_binary="$app_macos/$APP_NAME"
  local info_plist="$app_contents/Info.plist"
  local zip_path="$RELEASE_DIR/$APP_NAME-macos-$arch.zip"

  echo "==> Building $APP_NAME for $arch"
  swift build -c release --arch "$arch"
  build_bin_path="$(swift build -c release --arch "$arch" --show-bin-path)/$APP_NAME"

  rm -rf "$app_bundle"
  mkdir -p "$app_macos" "$app_resources"
  cp "$build_bin_path" "$app_binary"
  chmod +x "$app_binary"
  cp -R "$SOURCE_RESOURCES/." "$app_resources/"
  chmod -R u+w "$app_resources"
  rm -f "$app_resources/bin/sing-box"
  cp "$sing_box_binary" "$app_resources/bin/sing-box"
  chmod +x "$app_resources/bin/sing-box"

  cat >"$info_plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$APP_VERSION</string>
  <key>CFBundleVersion</key>
  <string>$APP_VERSION</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MIN_SYSTEM_VERSION</string>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST

  codesign --force --deep --sign - "$app_bundle" >/dev/null
  codesign --verify --deep --strict "$app_bundle"
  ditto -c -k --keepParent "$app_bundle" "$zip_path"
  shasum -a 256 "$zip_path" >> "$RELEASE_DIR/SHA256SUMS.txt"
}

cd "$ROOT_DIR"
ARM_SING_BOX="$SOURCE_RESOURCES/bin/sing-box"
AMD_SING_BOX="$(ensure_sing_box_amd64)"

stage_app "arm64" "$ARM_SING_BOX"
stage_app "x86_64" "$AMD_SING_BOX"

echo "Release artifacts:"
ls -lh "$RELEASE_DIR"/*.zip "$RELEASE_DIR/SHA256SUMS.txt"
