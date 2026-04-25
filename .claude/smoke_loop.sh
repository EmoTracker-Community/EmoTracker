#!/usr/bin/env bash
#
# Phase 3 MCP smoke loop — 10 iterations.
#
# Each iteration:
#   1. Launches EmoTracker with -dev -localservice (Debug build, --no-build).
#   2. Waits for "[MCP] Server listening on port 27125" in the log
#      (or aborts if the process dies first → crash signal).
#   3. Initializes an MCP session.
#   4. Calls reload_pack — this fires ClearImageCache while the image worker
#      is still chewing through the just-loaded pack, deliberately re-opening
#      the TOCTOU window the crash exploited.
#   5. Waits a few seconds for the second image-resolution pass.
#   6. Calls shutdown via MCP.
#   7. Waits for the process to exit and scans the log for crash markers
#      (Fatal error, 0xC0000005, sk_bitmap_make_shader, Unhandled exception).
#
# Per-iteration logs land in /tmp/smoke_run_NN.log so we can inspect failures.

set -u

DOTNET="/c/Program Files/dotnet/dotnet.exe"
PROJ="EmoTracker/EmoTracker.csproj"
PORT=27125
LOGDIR=/tmp/phase3_smoke
mkdir -p "$LOGDIR"
rm -f "$LOGDIR"/*.log "$LOGDIR/summary.txt"

PASS=0
FAIL=0

for i in $(seq 1 10); do
    LOG="$LOGDIR/run_$(printf %02d $i).log"
    echo "===== Run $i =====" | tee -a "$LOGDIR/summary.txt"

    # Launch app in background.
    "$DOTNET" run --project "$PROJ" --configuration Debug --no-build -- -dev -localservice \
        > "$LOG" 2>&1 &
    APP_PID=$!

    # Wait up to 60s for MCP listener (or process death).
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
        echo "  Run $i: ❌ MCP did not come up (app may have crashed during startup)" | tee -a "$LOGDIR/summary.txt"
        kill -KILL "$APP_PID" 2>/dev/null
        wait "$APP_PID" 2>/dev/null
        FAIL=$((FAIL + 1))
        # Pull crash markers from the log
        grep -E "Fatal error|0xC0000005|sk_bitmap_make_shader|Unhandled exception" "$LOG" | head -5 | sed 's/^/    /' | tee -a "$LOGDIR/summary.txt"
        continue
    fi

    # Give the image worker a moment to start chewing.
    sleep 2

    # Initialize MCP session, capture the session ID from the response headers.
    INIT_RESP=$(curl -sS -X POST \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -D - \
        -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.1"}}}' \
        "http://localhost:$PORT/" -m 10 2>&1)
    SESSION_ID=$(echo "$INIT_RESP" | grep -i "mcp-session-id" | head -1 | awk '{print $2}' | tr -d '\r')

    if [ -z "$SESSION_ID" ]; then
        echo "  Run $i: ❌ Failed to obtain MCP session ID" | tee -a "$LOGDIR/summary.txt"
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

    # Trigger reload_pack — this is the "clear the image cache while the
    # worker is mid-resolution" stress test. If the race window were still
    # open, this would AV inside Skia.
    curl -sS -X POST \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -H "mcp-session-id: $SESSION_ID" \
        -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"reload_pack","arguments":{}}}' \
        "http://localhost:$PORT/" -m 30 > /dev/null 2>&1

    # Let the second image-resolution pass run.
    sleep 5

    # Process still alive?
    if ! kill -0 "$APP_PID" 2>/dev/null; then
        echo "  Run $i: ❌ Process died during reload_pack / image resolution" | tee -a "$LOGDIR/summary.txt"
        wait "$APP_PID" 2>/dev/null
        FAIL=$((FAIL + 1))
        grep -E "Fatal error|0xC0000005|sk_bitmap_make_shader|Unhandled exception" "$LOG" | head -5 | sed 's/^/    /' | tee -a "$LOGDIR/summary.txt"
        continue
    fi

    # Graceful shutdown.
    curl -sS -X POST \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -H "mcp-session-id: $SESSION_ID" \
        -d '{"jsonrpc":"2.0","id":99,"method":"tools/call","params":{"name":"shutdown","arguments":{}}}' \
        "http://localhost:$PORT/" -m 5 > /dev/null 2>&1

    # Wait for clean exit (up to 8s).
    for _ in $(seq 1 16); do
        if ! kill -0 "$APP_PID" 2>/dev/null; then break; fi
        sleep 0.5
    done
    if kill -0 "$APP_PID" 2>/dev/null; then
        echo "  Run $i: ⚠️  Process did not exit; killing" | tee -a "$LOGDIR/summary.txt"
        kill -KILL "$APP_PID" 2>/dev/null
    fi
    wait "$APP_PID" 2>/dev/null

    # Final scan of the log for crash markers.
    if grep -qE "Fatal error|0xC0000005|sk_bitmap_make_shader|Unhandled exception" "$LOG"; then
        echo "  Run $i: ❌ Crash markers found in log" | tee -a "$LOGDIR/summary.txt"
        grep -E "Fatal error|0xC0000005|sk_bitmap_make_shader|Unhandled exception" "$LOG" | head -5 | sed 's/^/    /' | tee -a "$LOGDIR/summary.txt"
        FAIL=$((FAIL + 1))
    else
        echo "  Run $i: ✅ Clean" | tee -a "$LOGDIR/summary.txt"
        PASS=$((PASS + 1))
    fi

    # Tiny inter-run pause so port 27125 fully releases.
    sleep 1
done

echo "==========================================" | tee -a "$LOGDIR/summary.txt"
echo "Phase 3 smoke loop: $PASS passed, $FAIL failed (out of 10)" | tee -a "$LOGDIR/summary.txt"
echo "Logs: $LOGDIR" | tee -a "$LOGDIR/summary.txt"
exit $FAIL
