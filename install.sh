#!/usr/bin/env sh
# Pumex installer — downloads the latest release for this platform and drops
# `pumex` + `pumex-daemon` into ~/.pumex/bin/.
#
# Usage:   curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/main/install.sh | sh
# Pinning: PUMEX_VERSION=v0.2.0 sh install.sh

set -eu

REPO="${PUMEX_REPO:-dbarwikowski/pumex}"
VERSION="${PUMEX_VERSION:-latest}"
BIN_DIR="${PUMEX_BIN_DIR:-$HOME/.pumex/bin}"

# ---- Detect platform ----
case "$(uname -s)" in
    Linux*)  os=linux ;;
    Darwin*) os=osx ;;
    *) echo "error: unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac

case "$(uname -m)" in
    x86_64|amd64)  arch=x64 ;;
    arm64|aarch64) arch=arm64 ;;
    *) echo "error: unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

rid="${os}-${arch}"
asset="pumex-${rid}.tar.gz"

if [ "$VERSION" = "latest" ]; then
    url="https://github.com/${REPO}/releases/latest/download/${asset}"
else
    url="https://github.com/${REPO}/releases/download/${VERSION}/${asset}"
fi

# ---- Download + extract ----
mkdir -p "$BIN_DIR"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "Downloading $asset from $url..."
if command -v curl >/dev/null 2>&1; then
    curl -fSL --progress-bar -o "$tmp/$asset" "$url"
elif command -v wget >/dev/null 2>&1; then
    wget -q --show-progress -O "$tmp/$asset" "$url"
else
    echo "error: need curl or wget on PATH" >&2; exit 1
fi

tar -xzf "$tmp/$asset" -C "$BIN_DIR"
chmod +x "$BIN_DIR/pumex" "$BIN_DIR/pumex-daemon"

# ---- Hints ----
cat <<EOF

Installed:
  $BIN_DIR/pumex
  $BIN_DIR/pumex-daemon

Add to PATH (current shell):
  export PATH="$BIN_DIR:\$PATH"

Then install the daemon as a service:
  pumex daemon install
EOF
