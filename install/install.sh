#!/usr/bin/env sh
# Pumex installer — downloads the latest release, installs pumex and
# pumex-daemon to ~/.pumex/bin/, adds to PATH, and registers the daemon service.
#
# Usage:   curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.sh | sh
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

echo "Downloading $asset..."
if command -v curl >/dev/null 2>&1; then
    curl -fSL --progress-bar -o "$tmp/$asset" "$url"
elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$tmp/$asset" "$url"
else
    echo "error: curl or wget is required" >&2; exit 1
fi

PLIST_LINUX="$HOME/.config/systemd/user/pumex.service"
PLIST_MAC="$HOME/Library/LaunchAgents/com.pumex.daemon.plist"

# ---- Stop service before replacing binaries (avoids "text busy" / locked file) ----
SERVICE_STOPPED=0
if [ "$os" = "linux" ]; then
    if systemctl --user is-active --quiet pumex 2>/dev/null; then
        echo "Stopping pumex service..."
        systemctl --user stop pumex
        SERVICE_STOPPED=1
    fi
elif [ "$os" = "osx" ]; then
    if [ -f "$PLIST_MAC" ] && launchctl list com.pumex.daemon >/dev/null 2>&1; then
        echo "Stopping pumex service..."
        launchctl unload "$PLIST_MAC"
        SERVICE_STOPPED=1
    fi
fi

tar -xzf "$tmp/$asset" -C "$BIN_DIR"
chmod +x "$BIN_DIR/pumex" "$BIN_DIR/pumex-daemon"
echo "Installed to $BIN_DIR"

# ---- Add to PATH (permanent, deduplicated) ----
add_to_path() {
    [ -f "$1" ] || return 0
    grep -qF "$BIN_DIR" "$1" && return 0
    printf '\nexport PATH="%s:$PATH"\n' "$BIN_DIR" >> "$1"
    echo "Added $BIN_DIR to PATH in $1"
}

add_to_path "$HOME/.profile"
add_to_path "$HOME/.bashrc"
add_to_path "$HOME/.zshrc"
export PATH="$BIN_DIR:$PATH"

# ---- Restart or first-install the daemon service ----
if [ "$SERVICE_STOPPED" = "1" ]; then
    echo "Restarting pumex service..."
    if [ "$os" = "linux" ]; then
        systemctl --user start pumex
    elif [ "$os" = "osx" ]; then
        launchctl load "$PLIST_MAC"
    fi
elif { [ "$os" = "linux" ] && [ ! -f "$PLIST_LINUX" ]; } || \
     { [ "$os" = "osx" ]   && [ ! -f "$PLIST_MAC" ]; }; then
    if "$BIN_DIR/pumex" daemon install; then
        : # daemon reported its own success
    else
        echo ""
        echo "Could not register daemon service (systemd/launchd not available?)."
        echo "Run manually when ready:  pumex daemon install"
    fi
fi
