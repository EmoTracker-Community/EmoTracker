#!/usr/bin/env bash
# Zips the smoke test packs into the app's installed-packs directory so they can
# be loaded via MCP `load_pack`. Usage: install_smoke_pack.sh
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$ROOT/EmoTracker.Smoke/TestPacks"
PACKS="${EMOTRACKER_PACKS_DIR:-$HOME/Documents/EmoTracker/dev/packs}"
mkdir -p "$PACKS"

for pack in "$SRC"/*; do
  [ -d "$pack" ] || continue
  name="$(basename "$pack")"
  # package_uid may differ from folder name (currently folder == uid)
  out="$PACKS/$name.zip"
  rm -f "$out"
  (cd "$SRC" && zip -qr "$out" "$name")
  echo "Installed smoke pack: $out"
done
