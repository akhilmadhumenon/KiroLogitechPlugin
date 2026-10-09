#!/usr/bin/env bash
# Install the Kiro keypad relay into ~/.kiro/hooks.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST_DIR="${HOME}/.kiro/hooks"
RELAY_DEST="${DEST_DIR}/kiro-keypad-relay.mjs"
HOOKS_DEST="${DEST_DIR}/kiro-keypad.json"

mkdir -p "$DEST_DIR"
cp "$ROOT/hooks/kiro-keypad-relay.mjs" "$RELAY_DEST"
chmod +x "$RELAY_DEST"

python3 - "$ROOT/hooks/kiro-keypad.json" "$HOOKS_DEST" "$RELAY_DEST" <<'PY'
import json, sys
from pathlib import Path
src, dest, relay = sys.argv[1:]
data = json.loads(Path(src).read_text())
for hook in data.get("hooks", []):
    command = hook.get("action", {}).get("command")
    if command == "__RELAY__":
        hook["action"]["command"] = relay
Path(dest).write_text(json.dumps(data, indent=2) + "\n")
print(f"Installed hooks → {dest}")
PY

echo "Relay → $RELAY_DEST"
echo "Reload Kiro so the new hooks attach to the next session."
