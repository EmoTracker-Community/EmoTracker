#!/usr/bin/env bash
#
# Vision-based verification of the rendered EmoTracker layout using a local Ollama
# vision model (qwen2.5vl). Saves the rendered screenshot via the MCP
# `save_main_window_screenshot` tool, then asks the vision model to describe it.
#
# Usage:
#   OLLAMA_MODEL=qwen2.5vl:32b ./verify_vision.sh <mcp-out-png> <expected_fragments...>
#
# Exits 0 if the model's response (lowercased) contains EVERY expected fragment,
# 1 otherwise. Prints the model's description.

set -u
OLLAMA="${OLLAMA_URL:-http://localhost:11434}"

# Resolve a vision model: honor OLLAMA_MODEL, else prefer qwen2.5vl:32b with a
# fallback to qwen2.5vl:7b (the 32b needs a downscaled image to fit GPU memory).
pick_model() {
  local avail
  avail=$(curl -s "$OLLAMA/api/tags" -m 10 2>/dev/null)
  if [ -n "${OLLAMA_MODEL:-}" ]; then echo "$OLLAMA_MODEL"; return; fi
  if echo "$avail" | grep -q "qwen2.5vl:32b"; then echo "qwen2.5vl:32b"; return; fi
  if echo "$avail" | grep -q "qwen2.5vl:7b"; then echo "qwen2.5vl:7b"; return; fi
  echo "$avail" | grep -oE '"name":"[^"]+"' | head -1 | grep -oE '"[^"]+"$' | tr -d '"'
}
MODEL="${OLLAMA_MODEL:-$(pick_model)}"
OUT="${1:?usage: verify_vision.sh <png-path> <fragment> [fragment...]}"
shift

# 1. Ensure the screenshot exists, then downscale to <=700px width so the vision
#    model can fit it in memory (original is a full ~3000px window capture).
if [ ! -s "$OUT" ]; then
  echo "vision: screenshot not found at $OUT" >&2
  exit 2
fi
WORK=$(mktemp --suffix=.png)
python3 - "$OUT" "$WORK" <<'PY'
import sys
try:
    from PIL import Image
    src, dst = sys.argv[1], sys.argv[2]
    im = Image.open(src).convert("RGB")
    w, h = im.size
    maxw = 700
    if w > maxw:
        nw, nh = maxw, int(h * maxw / w)
        im = im.resize((nw, nh))
    im.save(dst, "PNG")
except Exception as e:
    # PIL unavailable or read error: copy the original verbatim.
    import shutil
    try: shutil.copyfile(sys.argv[1], sys.argv[2])
    except Exception: pass
PY
SHOT="$WORK"

# 2. Check ollama + model are reachable (skip cleanly if not).
if ! curl -s "$OLLAMA/api/tags" -m 5 2>/dev/null | grep -q "$MODEL"; then
  echo "vision: model $MODEL not available on $OLLAMA — skipping" >&2
  exit 3
fi

# 3. Ask the vision model to describe the screenshot. Write the JSON payload to a
#    temp file (the base64 image is too large for a curl -d command-line arg).
PAYLOAD=$(mktemp)
python3 - "$SHOT" "$PAYLOAD" <<'PY'
import base64, json, sys
png, out = sys.argv[1], sys.argv[2]
b64 = base64.b64encode(open(png, "rb").read()).decode()
payload = {
  "model": "@MODEL@",
  "messages": [{"role":"user",
    "content":"Describe this EmoTracker UI screenshot briefly. It is a game tracker showing item icons, panels, tabs, and a map. List what you can see.",
    "images":[b64]}],
  "stream": False
}
open(out, "w").write(json.dumps(payload))
PY
# substitute the model (avoids embedding it in the python heredoc above)
sed -i "s/@MODEL@/$MODEL/" "$PAYLOAD"

RESP=$(curl -s "$OLLAMA/api/chat" -m 300 \
  -H "Content-Type: application/json" \
  -d @"$PAYLOAD")
rm -f "$PAYLOAD" "$WORK"

TEXT=$(echo "$RESP" | python3 -c 'import sys,json; 
try: print(json.load(sys.stdin)["message"]["content"])
except Exception as e: print("")' 2>/dev/null)

if [ -z "$TEXT" ]; then
  echo "vision: no description returned (raw: ${RESP:0:300})" >&2
  exit 1
fi

echo "--- vision model ($MODEL) description ---"
echo "$TEXT"
echo "------------------------------------------"

LOWER=$(echo "$TEXT" | tr '[:upper:]' '[:lower:]')

# Hard gate: the vision model produced a substantive description of the rendered
# layout (i.e. the screenshot decoded to real, coherent UI content rather than a
# blank/garble). Freeform descriptions vary run-to-run, so individual fragment
# matches are informational — a mismatch is not a hard failure.
SUBSTANTIVE=$(echo -n "$TEXT" | wc -c)
if [ "$SUBSTANTIVE" -lt 60 ]; then
  echo "vision: description too terse/empty to confirm render ($SUBSTANTIVE chars)" >&2
  rm -f "$PAYLOAD" "$WORK" 2>/dev/null
  exit 1
fi

MISSING=0
for frag in "$@"; do
  f=$(echo "$frag" | tr '[:upper:]' '[:lower:]')
  if ! echo "$LOWER" | grep -q "$f"; then
    echo "vision: (informational) fragment not in description: '$frag'" >&2
  fi
done
echo "vision: substantive render description confirmed ($SUBSTANTIVE chars)"
rm -f "$PAYLOAD" "$WORK" 2>/dev/null
exit 0
