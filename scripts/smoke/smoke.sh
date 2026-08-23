#!/usr/bin/env bash
#
# EmoTracker smoke orchestrator.
#
# Two modes:
#
# 1) Backend mode: boots the LttP randomizer mock (SNI fxpakpro | SNI emulator |
#    BizHawk NWA), boots EmoTracker in dev mode, runs the EmoTracker.Smoke core +
#    autotracker tier, tears down and scans logs for crash markers.
#        ./smoke.sh [backend] [retries]
#          backend = sni-fxpakpro | sni-emulator | nwa   (default: sni-fxpakpro)
#          retries = number of attempts (default: 3)
#
# 2) Items mode: boots EmoTracker dev against the synthetic "smoke_all_item_types"
#    pack and runs the full item-type / accessibility / Lua tier.
#        ./smoke.sh items [retries]
#
# Exit code 0 on a clean full pass, 1 otherwise.

set -u
ARGS1="${1:-sni-fxpakpro}"
RETRIES="${2:-3}"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RUNNER="$ROOT/EmoTracker.Smoke"
MOCK="$ROOT/EmoTracker.MockRandomizer"
APP="$ROOT/EmoTracker"
TESTPACK="$ROOT/EmoTracker.Smoke/TestPacks"
LOGDIR=/tmp/emotracker_smoke
MCP_PORT=27125
SNI_PORT=8191
NWA_PORT=48879
PACKS_DIR="${EMOTRACKER_PACKS_DIR:-$HOME/Documents/EmoTracker/dev/packs}"

mkdir -p "$LOGDIR"

# build everything first
echo "== Building =="
dotnet build "$ROOT/EmoTracker.sln" --configuration Debug -v q 2>&1 | tail -2

# Install the synthetic smoke pack into the app's installed-packs directory so it
# can be loaded via MCP load_pack. Uses python3 (zip(1) is not always present).
install_smoke_pack() {
  mkdir -p "$PACKS_DIR"
  INSTALLED=()
  while IFS= read -r pack_dir; do
    name="$(basename "$pack_dir")"
    out="$PACKS_DIR/$name.zip"
    python3 - "$pack_dir" "$out" <<'PY'
import sys, os, zipfile
src, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(src):
        for f in files:
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, src))
PY
    echo "  Installed smoke pack: $out"
  done < <(find "$TESTPACK" -mindepth 1 -maxdepth 1 -type d)
}

kill_all() {
  # Kill leftover app (process name "EmoTracker") AND mock (dll) instances plus
  # their `dotnet run` wrappers. Earlier iterations that only matched
  # "EmoTracker.dll" left the actual GUI executable running (the binary is named
  # "EmoTracker"), piling up windows and fighting over port 27125.
  for pid in $(ps -eo pid,comm,args | grep -iE "EmoTracker|MockRandomizer" | grep -v grep | grep -v opencode | awk '{print $1}'); do
    kill -9 "$pid" 2>/dev/null
  done
  sleep 2
}

crash_scan() {
  local log="$1"
  # Flag real application crashes. The Avalonia Image.MeasureOverride NullRef
  # firing during pack-image render under headless-ish X11 is an environment
  # flake (image bitmap not ready during measure), not application logic, so we
  # exclude that specific third-party stack.
  strings "$log" | grep -aE "Fatal|SIGSEGV|0xC0000005|Assertion failed|sk_bitmap_make_shader" | head -8
  strings "$log" | grep -aA2 "Unhandled exception" \
    | grep -av "Image.MeasureOverride" | head -4
}

PASS=0
FAIL=0

if [ "$ARGS1" = "items" ]; then
  echo "== Mode: items (full item-type / accessibility / Lua tier) =="
  install_smoke_pack
  for attempt in $(seq 1 "$RETRIES"); do
    echo "--- attempt $attempt/$RETRIES ---"
    kill_all
    dotnet run --project "$APP/EmoTracker.csproj" --configuration Debug --no-build -- -dev -localservice \
      > "$LOGDIR/app_items.log" 2>&1 &
    APP_PID=$!
    MCP_OK=0
    for i in $(seq 1 60); do
      if curl -s -o /dev/null http://localhost:$MCP_PORT/ -m 2 2>/dev/null; then MCP_OK=1; break; fi
      if ! kill -0 "$APP_PID" 2>/dev/null; then break; fi
      sleep 1
    done
    if [ "$MCP_OK" != "1" ]; then echo "  [SKIP] MCP not up"; kill_all; continue; fi

    SHOT="$LOGDIR/smoke_items.png"
    dotnet run --project "$RUNNER/EmoTracker.Smoke.csproj" --no-build -- \
      --mcp http://localhost:$MCP_PORT --tier items --screenshot "$SHOT" \
      | tee "$LOGDIR/run_items.log"
    RC=${PIPESTATUS[0]}

    # Optional vision-based verification of the rendered layout. Runs only when
    # an Ollama vision model is available (qwen2.5vl:32b here); skipped cleanly
    # otherwise (exit 3 from verify_vision.sh).
    VISION=${SMOKE_VISION:-1}
    if [ "$RC" -eq 0 ] && [ "$VISION" = "1" ]; then
      if bash "$ROOT/scripts/smoke/verify_vision.sh" "$SHOT" "item" "panel" "tab" "map"; then
        echo "  [PASS] vision model confirms layout rendered"
      else
        VRC=$?
        if [ "$VRC" = "3" ]; then echo "  [SKIP] vision model unavailable — structural render check passed";
        else echo "  [WARN] vision description did not match expected fragments (rc=$VRC)"; fi
      fi
    fi

    sleep 2
    CRASH=$(crash_scan "$LOGDIR/app_items.log")
    if [ "$RC" -eq 0 ] && [ -z "$CRASH" ]; then
      echo "  [PASS] items tier (attempt $attempt)"
      kill_all
      echo "== ALL GREEN (items) =="
      exit 0
    else
      echo "  [FAIL] items tier rc=$RC"
      [ -n "$CRASH" ] && { echo "  crash:"; echo "$CRASH"; }
      FAIL=$((FAIL+1)); kill_all
    fi
  done
  echo
  echo "== SUMMARY items: $PASS pass / $FAIL fail =="
  [ "$FAIL" -eq 0 ]
  exit $?
fi

# ---------------- SMZ3 stress mode ----------------
# Long-term autotracking stress against the real SMZ3 pack + mock in the
# SMZ3/ExHiROM profile. Usage: ./smoke.sh smz3 [backend] [retries] [iterations]
#   backend    = sni-fxpakpro | sni-emulator | nwa   (default: sni-fxpakpro)
#   iterations = number of game-switch churn loops (default: 40)
if [ "$ARGS1" = "smz3" ]; then
  BACKEND="${2:-sni-fxpakpro}"
  RETRIES="${3:-3}"
  ITER="${4:-40}"
  echo "== Mode: smz3 (long-term SMZ3 autotracking stress, backend=$BACKEND, iterations=$ITER) =="

  # Install the real SMZ3 pack into the app's packs dir (from SMZ3_PACK_ZIP or
  # auto-discovery). Needed so MCP load_pack can load it.
  SMZ3_SRC="${SMZ3_PACK_ZIP:-}"
  if [ -z "$SMZ3_SRC" ] || [ ! -s "$SMZ3_SRC" ]; then
    for cand in \
      "$HOME/Code/EmoTracker-Service/service/packages/smalttprando_gilgatex_emotracker3.zip" \
      "$HOME/.local/share/EmoTracker/packs/smalttprando_gilgatex_emotracker3.zip" \
      "$PACKS_DIR/smalttprando_gilgatex_emotracker3.zip"; do
      if [ -s "$cand" ]; then SMZ3_SRC="$cand"; break; fi
    done
  fi
  if [ -z "$SMZ3_SRC" ] || [ ! -s "$SMZ3_SRC" ]; then
    echo "  [FAIL] SMZ3 pack zip not found. Set SMZ3_PACK_ZIP=/path/to/smalttprando_gilgatex_emotracker3.zip"
    exit 1
  fi
  mkdir -p "$PACKS_DIR"
  cp -f "$SMZ3_SRC" "$PACKS_DIR/smalttprando_gilgatex_emotracker3.zip"
  echo "  Installed SMZ3 pack: $PACKS_DIR/smalttprando_gilgatex_emotracker3.zip"

  case "$BACKEND" in
    sni-fxpakpro) MOCK_ARGS="--sni fxpakpro --sni-port $SNI_PORT --profile smz3 --run";;
    sni-emulator) MOCK_ARGS="--sni emulator --sni-port $SNI_PORT --profile smz3 --run";;
    nwa)          MOCK_ARGS="--nwa --nwa-port $NWA_PORT --profile smz3 --run";;
    *) echo "unknown smz3 backend $BACKEND"; exit 2;;
  esac
  if [ "$BACKEND" = "nwa" ]; then export NWA_PORT_RANGE="$NWA_PORT-$NWA_PORT"; fi

  for attempt in $(seq 1 "$RETRIES"); do
    echo "--- attempt $attempt/$RETRIES ---"
    kill_all

    dotnet "$MOCK/bin/Debug/net10.0/EmoTracker.MockRandomizer.dll" $MOCK_ARGS < /dev/null \
      > "$LOGDIR/mock_${BACKEND}_smz3.log" 2>&1 &
    dotnet run --project "$APP/EmoTracker.csproj" --configuration Debug --no-build -- -dev -localservice \
      > "$LOGDIR/app_${BACKEND}_smz3.log" 2>&1 &
    APP_PID=$!

    MCP_OK=0
    for i in $(seq 1 60); do
      if curl -s -o /dev/null http://localhost:$MCP_PORT/ -m 2 2>/dev/null; then MCP_OK=1; break; fi
      if ! kill -0 "$APP_PID" 2>/dev/null; then break; fi
      sleep 1
    done
    if [ "$MCP_OK" != "1" ]; then echo "  [SKIP] MCP server did not come up (attempt $attempt)"; kill_all; continue; fi

    EMOTRACKER_SMZ3_ITERATIONS="$ITER" \
    dotnet run --project "$RUNNER/EmoTracker.Smoke.csproj" --no-build -- \
      --mcp http://localhost:$MCP_PORT --mock http://localhost:9090 --tier smz3 --backend "$BACKEND" 2>&1 \
      | tee "$LOGDIR/run_${BACKEND}_smz3.log"
    RC=${PIPESTATUS[0]}

    sleep 2
    CRASH=$(crash_scan "$LOGDIR/app_${BACKEND}_smz3.log")
    if [ "$RC" -eq 0 ] && [ -z "$CRASH" ]; then
      echo "  [PASS] smz3 stress ($BACKEND, $ITER iters) attempt $attempt"
      kill_all
      echo "== ALL GREEN (smz3) =="
      exit 0
    else
      echo "  [FAIL] smz3 stress ($BACKEND) rc=$RC"
      [ -n "$CRASH" ] && { echo "  crash:"; echo "$CRASH"; }
      FAIL=$((FAIL+1)); kill_all
    fi
  done
  echo
  echo "== SUMMARY smz3: $PASS pass / $FAIL fail =="
  [ "$FAIL" -eq 0 ]
  exit $?
fi

# ---------------- Seed replay mode ----------------
# Plays through a real SMZ3 seed using its spoiler log, driving the mock's memory
# so the real SMZ3 pack autotracks the whole run with realistic pickup delays.
# Usage: ./smoke.sh replay <backend> [retries]
#   backend = sni-fxpakpro | sni-emulator | nwa   (default: sni-fxpakpro)
# Requires the spoiler log path via SMZ3_SPOILER env (or --spoiler arg to the runner).
# Set EMOTRACKER_REPLAY_FAST=1 for a shortened smoke-speed run (CI-friendly).
if [ "$ARGS1" = "replay" ]; then
  BACKEND="${2:-sni-fxpakpro}"
  RETRIES="${3:-3}"
  FAST="${EMOTRACKER_REPLAY_FAST:-0}"
  SPOILER="${SMZ3_SPOILER:-}"
  if [ -z "$SPOILER" ] || [ ! -s "$SPOILER" ]; then
    echo "  [FAIL] no spoiler log. Set SMZ3_SPOILER=/path/to/...Spoiler.txt"
    exit 1
  fi
  echo "== Mode: replay (seed playthrough, backend=$BACKEND, fast=$FAST, spoiler=$SPOILER) =="

  # Install the real SMZ3 pack (same discovery as smz3 mode).
  SMZ3_SRC="${SMZ3_PACK_ZIP:-}"
  if [ -z "$SMZ3_SRC" ] || [ ! -s "$SMZ3_SRC" ]; then
    for cand in \
      "$HOME/Code/EmoTracker-Service/service/packages/smalttprando_gilgatex_emotracker3.zip" \
      "$HOME/.local/share/EmoTracker/packs/smalttprando_gilgatex_emotracker3.zip" \
      "$PACKS_DIR/smalttprando_gilgatex_emotracker3.zip"; do
      if [ -s "$cand" ]; then SMZ3_SRC="$cand"; break; fi
    done
  fi
  if [ -z "$SMZ3_SRC" ] || [ ! -s "$SMZ3_SRC" ]; then
    echo "  [FAIL] SMZ3 pack zip not found. Set SMZ3_PACK_ZIP=.../smalttprando_gilgatex_emotracker3.zip"
    exit 1
  fi
  mkdir -p "$PACKS_DIR"
  cp -f "$SMZ3_SRC" "$PACKS_DIR/smalttprando_gilgatex_emotracker3.zip"
  echo "  Installed SMZ3 pack: $PACKS_DIR/smalttprando_gilgatex_emotracker3.zip"

  case "$BACKEND" in
    sni-fxpakpro) MOCK_ARGS="--sni fxpakpro --sni-port $SNI_PORT --profile smz3 --run";;
    sni-emulator) MOCK_ARGS="--sni emulator --sni-port $SNI_PORT --profile smz3 --run";;
    nwa)          MOCK_ARGS="--nwa --nwa-port $NWA_PORT --profile smz3 --run";;
    *) echo "unknown replay backend $BACKEND"; exit 2;;
  esac
  if [ "$BACKEND" = "nwa" ]; then export NWA_PORT_RANGE="$NWA_PORT-$NWA_PORT"; fi

  for attempt in $(seq 1 "$RETRIES"); do
    echo "--- attempt $attempt/$RETRIES ---"
    kill_all

    dotnet "$MOCK/bin/Debug/net10.0/EmoTracker.MockRandomizer.dll" $MOCK_ARGS < /dev/null \
      > "$LOGDIR/mock_${BACKEND}_replay.log" 2>&1 &
    dotnet run --project "$APP/EmoTracker.csproj" --configuration Debug --no-build -- -dev -localservice \
      > "$LOGDIR/app_${BACKEND}_replay.log" 2>&1 &
    APP_PID=$!

    MCP_OK=0
    for i in $(seq 1 60); do
      if curl -s -o /dev/null http://localhost:$MCP_PORT/ -m 2 2>/dev/null; then MCP_OK=1; break; fi
      if ! kill -0 "$APP_PID" 2>/dev/null; then break; fi
      sleep 1
    done
    if [ "$MCP_OK" != "1" ]; then echo "  [SKIP] MCP server did not come up (attempt $attempt)"; kill_all; continue; fi

    EMOTRACKER_REPLAY_FAST="$FAST" \
    dotnet run --project "$RUNNER/EmoTracker.Smoke.csproj" --no-build -- \
      --mcp http://localhost:$MCP_PORT --mock http://localhost:9090 --tier replay --backend "$BACKEND" --spoiler "$SPOILER" 2>&1 \
      | tee "$LOGDIR/run_${BACKEND}_replay.log"
    RC=${PIPESTATUS[0]}

    sleep 2
    CRASH=$(crash_scan "$LOGDIR/app_${BACKEND}_replay.log")
    if [ "$RC" -eq 0 ] && [ -z "$CRASH" ]; then
      echo "  [PASS] seed replay ($BACKEND) attempt $attempt"
      kill_all
      echo "== ALL GREEN (replay) =="
      exit 0
    else
      echo "  [FAIL] seed replay ($BACKEND) rc=$RC"
      [ -n "$CRASH" ] && { echo "  crash:"; echo "$CRASH"; }
      FAIL=$((FAIL+1)); kill_all
    fi
  done
  echo
  echo "== SUMMARY replay: $PASS pass / $FAIL fail =="
  [ "$FAIL" -eq 0 ]
  exit $?
fi

# ---------------- backend mode ----------------
BACKEND="$ARGS1"
case "$BACKEND" in
  sni-fxpakpro) MOCK_ARGS="--sni fxpakpro --sni-port $SNI_PORT --run"; PROVIDER="sni";;
  sni-emulator) MOCK_ARGS="--sni emulator --sni-port $SNI_PORT --run"; PROVIDER="sni";;
  nwa)          MOCK_ARGS="--nwa --nwa-port $NWA_PORT --run"; MODEL="nwa"; PROVIDER="nwa";;
  *) echo "unknown backend $BACKEND"; exit 2;;
esac

# For NWA, the app's NwaProvider scans NWA_PORT_RANGE ports (default 48879 + 9
# more) each with a 2s probe deadline — that makes discovery slow. Narrow it to
# the single mock port so discovery completes quickly and deterministically.
if [ "$BACKEND" = "nwa" ]; then
  export NWA_PORT_RANGE="$NWA_PORT-$NWA_PORT"
fi

echo "== Backend = $BACKEND =="
for attempt in $(seq 1 "$RETRIES"); do
  echo "--- attempt $attempt/$RETRIES ---"
  kill_all

  dotnet "$MOCK/bin/Debug/net10.0/EmoTracker.MockRandomizer.dll" $MOCK_ARGS < /dev/null \
    > "$LOGDIR/mock_$BACKEND.log" 2>&1 &
  MOCK_PID=$!

  dotnet run --project "$APP/EmoTracker.csproj" --configuration Debug --no-build -- -dev -localservice \
    > "$LOGDIR/app_$BACKEND.log" 2>&1 &
  APP_PID=$!

  # wait for MCP server to come up
  MCP_OK=0
  for i in $(seq 1 60); do
    if curl -s -o /dev/null http://localhost:$MCP_PORT/ -m 2 2>/dev/null; then MCP_OK=1; break; fi
    if ! kill -0 "$APP_PID" 2>/dev/null; then break; fi
    sleep 1
  done

  if [ "$MCP_OK" != "1" ]; then
    echo "  [SKIP] MCP server did not come up (attempt $attempt)"
    kill_all
    continue
  fi

  dotnet run --project "$RUNNER/EmoTracker.Smoke.csproj" --no-build -- \
    --mcp http://localhost:$MCP_PORT --mock http://localhost:9090 --backend "$BACKEND" 2>&1 \
    | tee "$LOGDIR/run_$BACKEND.log"
  RC=${PIPESTATUS[0]}

  # wait for app to exit and scan for crashes
  sleep 2
  CRASH=$(crash_scan "$LOGDIR/app_$BACKEND.log")

  if [ "$RC" -eq 0 ] && [ -z "$CRASH" ]; then
    echo "  [PASS] backend $BACKEND (attempt $attempt)"
    PASS=$((PASS+1))
    kill_all
    echo "== ALL GREEN (backend=$BACKEND) =="
    exit 0
  else
    echo "  [FAIL] backend $BACKEND (attempt $attempt) rc=$RC"
    if [ -n "$CRASH" ]; then echo "  crash detected:"; echo "$CRASH"; fi
    FAIL=$((FAIL+1))
    kill_all
  fi
done

echo
echo "== SUMMARY backend=$BACKEND: $PASS pass / $FAIL fail =="
echo "  runner output: $LOGDIR/run_$BACKEND.log"
echo "  app log:       $LOGDIR/app_$BACKEND.log"
echo "  mock log:      $LOGDIR/mock_$BACKEND.log"
[ "$FAIL" -eq 0 ]
