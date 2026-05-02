# EmoTracker Lua Debugger

VS Code debug adapter for pack-script Lua running inside an
EmoTracker dev build.

## Setup

1. Build & install this extension:
   ```
   cd vscode-extensions/emotracker-lua-debug
   npm install
   npm run compile
   ```
   Then in VS Code: `Developer: Install Extension from Location...`
   and pick this folder. Or package with `vsce package` and install
   the resulting `.vsix`.

2. Start EmoTracker with `-dev`. The DAP server binds to
   `localhost:27126` (override with `EMOTRACKER_DAP_PORT`).

3. Open the pack folder in VS Code, hit `F5`, choose
   **Attach to EmoTracker (Lua)**.

## What works

- Breakpoints in pack-relative `.lua` files (auto-resolved against
  the pack's source root).
- Step over / step in / step out / continue / pause.
- Locals, upvalues, and globals on every paused frame.
- Lazy table expansion in the variables panel.
- REPL eval against the paused frame (Debug Console).
- Break on Lua errors raised through `_safe_call` (toggle the
  "Lua errors" exception breakpoint in the breakpoints panel).
- Multi-state debugging — every `TrackerState`'s interpreter shows
  up as a separate DAP "thread"; pick the one you want to step in
  the call-stack panel.

## Known limitations (v1)

- Conditional breakpoints not yet supported.
- `evaluate` only works while paused.
- Lua coroutines aren't tracked separately.
- Set-variable from the watch panel isn't implemented.
