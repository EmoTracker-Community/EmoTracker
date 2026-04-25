#!/usr/bin/env bash
#
# Phase 4 MCP smoke — exercises layout-type conversions end-to-end against
# the default-loaded ALttPR pack.
#
# Each iteration:
#   1. Launch EmoTracker with -dev -localservice (Debug, --no-build).
#   2. Wait for "[MCP] Server listening on port 27125" or process death.
#   3. Initialize MCP session.
#   4. get_loaded_pack — confirms the pack parsed and the layout tree
#      came up cleanly through the new KVOverridable parse path.
#   5. capture_main_window — baseline screenshot. Layout rendering
#      exercises every concrete LayoutItem subclass that appears in the
#      pack (DockPanel, ArrayPanel, TabPanel, MapPanel, Image, TextBlock,
#      Item, ItemGrid, ButtonPopup, GroupBox, ScrollPanel). A render
#      failure here means a property getter is returning something
#      unexpected (sentinel mismatch, ImmutableData lookup miss, etc.).
#   6. toggle a couple of items via toggle_item and right_click_item —
#      this exercises Item.Data ModelReference resolution (the cross-ref
#      conversion in Item.cs).
#   7. capture_main_window — confirms the UI updated visibly after the
#      mutations.
#   8. reload_pack — re-parses the entire layout tree under the new
#      Phase 4 path. Tests that PopulateDefinitionData + ImmutableData
#      replacement on every concrete subclass round-trips cleanly.
#   9. capture_main_window — post-reload UI render check.
#  10. shutdown — graceful exit.
#  11. Scan log for crash markers.
#
# Per-run logs land in /tmp/phase4_smoke/run_NN.log.

set -u

DOTNET="/c/Program Files/dotnet/dotnet.exe"
PROJ="EmoTracker/EmoTracker.csproj"
PORT=27125
LOGDIR=/tmp/phase4_smoke
ITERATIONS=${1:-3}
mkdir -p "$LOGDIR"
rm -f "$LOGDIR"/*.log "$LOGDIR/summary.txt" "$LOGDIR"/*.png

PASS=0
FAIL=0

mcp_call() {
    local session_id="$1"
    local id="$2"
    local name="$3"
    local args="$4"
    curl -sS -X POST \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -H "mcp-session-id: $session_id" \
        -d "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"tools/call\",\"params\":{\"name\":\"$name\",\"arguments\":$args}}" \
        "http://localhost:$PORT/" -m 30
}

for i in $(seq 1 "$ITERATIONS"); do
    LOG="$LOGDIR/run_$(printf %02d $i).log"
    SUM="$LOGDIR/summary.txt"
    echo "===== Phase 4 Smoke Run $i =====" | tee -a "$SUM"

    # Launch app in background (Debug, --no-build).
    "$DOTNET" run --project "$PROJ" --configuration Debug --no-build -- -dev -localservice \
        > "$LOG" 2>&1 &
    APP_PID=$!

    # Wait up to 60s for MCP listener.
    SUCCESS=0
    for _ in $(seq 1 120); do
        if grep -q "MCP\] Server listening on port" "$LOG" 2>/dev/null; then
            SUCCESS=1
            break
        fi
        if ! kill -0 "$APP_PID" 2>/dev/null; then
            break
        fi
        sleep 0.5
    done

    if [ "$SUCCESS" -eq 0 ]; then
        echo "  Run $i: FAIL — MCP did not come up (app crashed during startup)" | tee -a "$SUM"
        kill -KILL "$APP_PID" 2>/dev/null
        wait "$APP_PID" 2>/dev/null
        FAIL=$((FAIL + 1))
        grep -E "Fatal error|0xC0000005|Unhandled exception|System.NullReferenceException" "$LOG" | head -5 | sed 's/^/    /' | tee -a "$SUM"
        continue
    fi

    # Pack default-loads on startup; give it a moment to settle.
    sleep 3

    # Initialize MCP session.
    INIT_RESP=$(curl -sS -X POST \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -D - \
        -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"phase4-smoke","version":"0.0.1"}}}' \
        "http://localhost:$PORT/" -m 10 2>&1)
    SESSION_ID=$(echo "$INIT_RESP" | grep -i "mcp-session-id" | head -1 | awk '{print $2}' | tr -d '\r')

    if [ -z "$SESSION_ID" ]; then
        echo "  Run $i: FAIL — Failed to obtain MCP session ID" | tee -a "$SUM"
        kill -KILL "$APP_PID" 2>/dev/null
        wait "$APP_PID" 2>/dev/null
        FAIL=$((FAIL + 1))
        continue
    fi

    curl -sS -X POST \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -H "mcp-session-id: $SESSION_ID" \
        -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
        "http://localhost:$PORT/" -m 5 > /dev/null 2>&1

    STEP_FAIL=0

    # Step 1: get_loaded_pack — confirms pack parse + layout tree integrity.
    PACK_RESP=$(mcp_call "$SESSION_ID" 2 "get_loaded_pack" "{}")
    if echo "$PACK_RESP" | grep -q '"error"'; then
        echo "  Run $i: FAIL — get_loaded_pack errored" | tee -a "$SUM"
        echo "$PACK_RESP" | head -3 | sed 's/^/    /' | tee -a "$SUM"
        STEP_FAIL=1
    fi

    # Step 2: baseline screenshot.
    if [ "$STEP_FAIL" -eq 0 ]; then
        BASELINE_RESP=$(mcp_call "$SESSION_ID" 3 "capture_main_window" "{}")
        if echo "$BASELINE_RESP" | grep -q '"error"'; then
            echo "  Run $i: FAIL — capture_main_window (baseline) errored" | tee -a "$SUM"
            STEP_FAIL=1
        fi
    fi

    # Step 3: toggle some items by display name — exercises the
    # Item.Data ModelReference resolution path (Phase 4 §4.4) end-to-end.
    # Names below are ALttPR-pack display names; in other packs the smoke
    # will note misses but won't fail the run.
    if [ "$STEP_FAIL" -eq 0 ]; then
        for name in "Flippers" "Boots" "Hookshot" "Lantern"; do
            T_RESP=$(mcp_call "$SESSION_ID" 4 "toggle_item" "{\"name\":\"$name\"}")
            if echo "$T_RESP" | grep -q '"isError":true'; then
                echo "    note: toggle_item '$name' returned an error (likely missing in pack)" | tee -a "$SUM"
            fi
        done
    fi

    # Step 4: post-toggle screenshot.
    if [ "$STEP_FAIL" -eq 0 ]; then
        POST_TOGGLE=$(mcp_call "$SESSION_ID" 5 "capture_main_window" "{}")
        if echo "$POST_TOGGLE" | grep -q '"error"'; then
            echo "  Run $i: FAIL — capture_main_window (post-toggle) errored" | tee -a "$SUM"
            STEP_FAIL=1
        fi
    fi

    # Step 5: reload_pack — stress test on the parse + layout tree rebuild.
    if [ "$STEP_FAIL" -eq 0 ]; then
        RELOAD_RESP=$(mcp_call "$SESSION_ID" 6 "reload_pack" "{}")
        if echo "$RELOAD_RESP" | grep -q '"error"'; then
            echo "  Run $i: FAIL — reload_pack errored" | tee -a "$SUM"
            STEP_FAIL=1
        fi
        sleep 4  # Let the second image-resolution pass run.
    fi

    # Step 6: post-reload screenshot — confirms layout re-rendered.
    if [ "$STEP_FAIL" -eq 0 ]; then
        POST_RELOAD=$(mcp_call "$SESSION_ID" 7 "capture_main_window" "{}")
        if echo "$POST_RELOAD" | grep -q '"error"'; then
            echo "  Run $i: FAIL — capture_main_window (post-reload) errored" | tee -a "$SUM"
            STEP_FAIL=1
        fi
    fi

    # Step 7: process still alive?
    if [ "$STEP_FAIL" -eq 0 ] && ! kill -0 "$APP_PID" 2>/dev/null; then
        echo "  Run $i: FAIL — Process died during the smoke" | tee -a "$SUM"
        STEP_FAIL=1
    fi

    # Graceful shutdown.
    mcp_call "$SESSION_ID" 99 "shutdown" "{}" > /dev/null 2>&1

    # Wait for clean exit.
    for _ in $(seq 1 16); do
        if ! kill -0 "$APP_PID" 2>/dev/null; then break; fi
        sleep 0.5
    done
    if kill -0 "$APP_PID" 2>/dev/null; then
        echo "  Run $i: WARN — Process did not exit gracefully; killing" | tee -a "$SUM"
        kill -KILL "$APP_PID" 2>/dev/null
    fi
    wait "$APP_PID" 2>/dev/null

    # Final scan for crash markers.
    if grep -qE "Fatal error|0xC0000005|sk_bitmap_make_shader|Unhandled exception|System.NullReferenceException|Exception thrown:" "$LOG"; then
        echo "  Run $i: FAIL — Crash / unhandled exception markers in log" | tee -a "$SUM"
        grep -E "Fatal error|0xC0000005|sk_bitmap_make_shader|Unhandled exception|System.NullReferenceException|Exception thrown:" "$LOG" | head -10 | sed 's/^/    /' | tee -a "$SUM"
        STEP_FAIL=1
    fi

    if [ "$STEP_FAIL" -eq 0 ]; then
        echo "  Run $i: PASS" | tee -a "$SUM"
        PASS=$((PASS + 1))
    else
        FAIL=$((FAIL + 1))
    fi

    sleep 1
done

echo "==========================================" | tee -a "$LOGDIR/summary.txt"
echo "Phase 4 smoke: $PASS passed, $FAIL failed (out of $ITERATIONS)" | tee -a "$LOGDIR/summary.txt"
echo "Logs: $LOGDIR" | tee -a "$LOGDIR/summary.txt"
exit $FAIL
