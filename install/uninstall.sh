#!/usr/bin/env sh
# Pumex uninstaller — removes the service registration and binaries.
# Data in ~/.pumex/ is kept unless PUMEX_PURGE=1.
#
# Usage:   curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/main/uninstall.sh | sh
# Purge:   PUMEX_PURGE=1 curl -fsSL ... | sh

set -eu

BIN_DIR="${PUMEX_BIN_DIR:-$HOME/.pumex/bin}"
DATA_DIR="$HOME/.pumex"
PURGE="${PUMEX_PURGE:-0}"

# ---- 1. Daemon service ----
case "$(uname -s)" in
    Darwin*)
        PLIST="$HOME/Library/LaunchAgents/com.pumex.daemon.plist"
        if [ -f "$PLIST" ]; then
            echo "Unloading launchd agent..."
            launchctl unload "$PLIST" 2>/dev/null || true
            rm -f "$PLIST"
            echo "  Removed $PLIST"
        fi
        ;;
    *)
        UNIT="$HOME/.config/systemd/user/pumex.service"
        if [ -f "$UNIT" ]; then
            echo "Disabling systemd user service..."
            systemctl --user disable --now pumex 2>/dev/null || true
            rm -f "$UNIT"
            systemctl --user daemon-reload 2>/dev/null || true
            echo "  Removed $UNIT"
        fi
        ;;
esac

# ---- 2. Binaries ----
for bin in pumex pumex-daemon; do
    path="$BIN_DIR/$bin"
    if [ -f "$path" ]; then
        rm -f "$path"
        echo "  Removed $path"
    fi
done

# Remove bin dir if now empty
if [ -d "$BIN_DIR" ] && [ -z "$(ls -A "$BIN_DIR" 2>/dev/null)" ]; then
    rmdir "$BIN_DIR"
fi

# ---- 3. Data directory ----
if [ "$PURGE" = "1" ]; then
    if [ -d "$DATA_DIR" ]; then
        rm -rf "$DATA_DIR"
        echo "  Removed $DATA_DIR"
    fi
fi

echo ""
echo "Pumex uninstalled."

if [ "$PURGE" != "1" ] && [ -d "$DATA_DIR" ]; then
    echo ""
    echo "Data directory kept at: $DATA_DIR"
    echo "Remove it manually if you no longer need the index:"
    echo "  rm -rf '$DATA_DIR'"
fi
