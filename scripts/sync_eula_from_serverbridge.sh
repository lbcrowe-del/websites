#!/usr/bin/env bash
# Copies the real EULA source of truth (desktop/legal/eula/ServerBridge-EULA.md,
# what the desktop app displays at first run) into this repo's synced copy, then
# regenerates server-bridge/eula.html from it.
#
# Run this whenever the EULA changes in desktop, before committing here.
# Assumes desktop and websites are sibling directories (the normal local layout).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SOURCE="$REPO_ROOT/../desktop/legal/eula/ServerBridge-EULA.md"
DEST="$REPO_ROOT/server-bridge/legal-source/ServerBridge-EULA.md"

if [ ! -f "$SOURCE" ]; then
  echo "ERROR: $SOURCE not found. Is desktop checked out as a sibling of websites?" >&2
  exit 1
fi

cp "$SOURCE" "$DEST"
echo "Copied $SOURCE -> $DEST"

python3 "$SCRIPT_DIR/generate_eula.py"
