# EmoTracker Built-in MCP Server

EmoTracker ships a built-in [Model Context Protocol](https://modelcontextprotocol.io)
(MCP) server that you can use while the app is running to drive and inspect the
tracker programmatically. This is primarily a **developer/debug** feature used
to exercise the tracker without manual UI interaction (and is how the project
performs functional/smoke testing).

## Overview

- **Location:** `EmoTracker/Extensions/McpServer/`
- **Owned by:** `McpServerExtension` (`McpServerExtension.cs`), an app-scoped
  `IApplicationExtension` auto-discovered and started by `ExtensionManager`
  via `ApplicationModel.Initialize()`.
- **Transport:** Streamable HTTP (not stdio, not SSE) served by ASP.NET Core /
  Kestrel.
- **Endpoint:** `http://localhost:27125/` (localhost only — no auth/token).
- **Capabilities:** tools only — **no** custom resources or prompts are
  registered.
- **Tool count:** 57 tools (49 core + 8 auto-tracker control, plus 3 optional
  helpers compiled only with `DEBUG_PHASE7_INSPECT`).

## Building

The server is compiled **only in Debug builds** — it is stripped from Release.

`EmoTracker.csproj` (lines 13–25 and 61–62):

```xml
<!-- MCP dev server: only included in Debug builds -->
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <Compile Remove="Extensions\McpServer\**\*" Condition="'$(Configuration)' != 'Debug'" />
  <AvaloniaXaml Remove="Extensions\McpServer\**\*" Condition="'$(Configuration)' != 'Debug'" />
  <AvaloniaResource Remove="Extensions\McpServer\**\*" Condition="'$(Configuration)' != 'Debug'" />
</ItemGroup>
```

```xml
<PackageReference Include="ModelContextProtocol" Version="1.2.0" Condition="'$(Configuration)' == 'Debug'" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.2.0" Condition="'$(Configuration)' == 'Debug'" />
```

The build condition is `Configuration == Debug`; there is no additional
`#if DEBUG` guard inside the code (the runtime gate is the `-dev` flag below).

## Running

The server only starts when the app is launched with `-dev`:

```csharp
public void Start(IApplicationContext app)
{
    if (!UserDirectory.IsDevMode)   // true when "-dev" is on the command line
        return;
    _ = StartServerAsync();
}
```

```bash
dotnet run --project EmoTracker/EmoTracker.csproj --configuration Debug -- -dev -localservice
```

- Port `27125` is the default; override with the `EMOTRACKER_MCP_PORT` env var if
  it is taken.
- `-localservice` is an inert marker used by the dev scripts / launch settings;
  only `-dev` matters for the MCP server.
- The flags are already baked into `Properties/launchSettings.json`, so a plain
  VS / VS Code Debug launch enables the server too.

**Confirmation it started** (watch the **console or the log file**, not the
in-app developer terminal — `[MCP]` lines are filtered out of that sink):

```
[MCP] Server listening on port 27125
```

Failure logs `[MCP] Failed to start server`. Logs go to Serilog sinks: a file
`emotracker_log.txt` under `UserDirectory.LogPath`
(`%LOCALAPPDATA%\EmoTracker\dev\logs` in dev mode) plus the console.

## Protocol handshake

Streamable HTTP, JSON-RPC 2.0, protocol version `2024-11-05`. Sessions are keyed
by the `Mcp-Session-Id` response header.

1. **initialize** — capture the `mcp-session-id` header from the response:

```bash
curl -sS -X POST -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" -D - \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.1"}}}' \
  "http://localhost:27125/" -m 10
```

2. **notifications/initialized** — fire once with the session id:

```bash
curl -sS -X POST -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "mcp-session-id: $SESSION_ID" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' "http://localhost:27125/" -m 5
```

3. **tools/call** — echo the session id on every call:

```bash
curl -sS -X POST -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "mcp-session-id: $SESSION_ID" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"toggle_item","arguments":{"name":"Flippers"}}}' \
  "http://localhost:27125/" -m 30
```

Parse the JSON from the response. Note some tools return `{"error": ...}` or
`isError: true` when the pack isn't loaded or the referenced item isn't present —
treat per-tool errors as expected for that tool rather than a server failure.

## Client configuration

- **VS Code:** `.vscode/mcp.json` points the built-in MCP client at
  `http://localhost:27125/`:

  ```json
  { "servers": { "emotracker-mcp": { "type": "http", "url": "http://localhost:27125/" } } }
  ```

- **Bash examples:** `.claude/smoke_loop.sh` and `.claude/phase4_smoke.sh`
  contain reusable `mcp_call()` handshake/helper patterns.

## Tools

All schemas are generated from C# method parameters (`[Description]` becomes the
tool/parameter description; no-default parameters are required).

### Package
| Tool | Description | Params |
|------|-------------|--------|
| `get_pack_files` | List all files in the currently loaded pack | — |
| `get_pack_file_content` | Read a text file from the pack (Lua/JSON) | `path` (req) |
| `reload_pack` | Reload the current pack (equiv. F5) | — |

### Application control
| Tool | Description | Params |
|------|-------------|--------|
| `list_packs` | List available packs | — |
| `load_pack` | Load a pack by ID | `uniqueId` (req), `variant` (opt) |
| `toggle_item` | Toggle an item (simulate left-click) | `name` (req) |
| `right_click_item` | Right-click an item | `name` (req) |
| `reset_tracker` | Reset tracker state | — |
| `clear_location` | Clear a location (right-click its map square) | `name` (req) |
| `create_fork_tab` | Fork active state into a new tab (Phase 7) | — |
| `switch_to_tab` | Switch to a tab by state id | `stateId` (req, Guid) |
| `load_new_pack_via_state_manager` | Simulate the "Load New Pack" popup | `uniqueId` (req), `variant` (opt) |
| `list_tabs` | List open tabs on all windows | — |
| `shutdown` | Normal app shutdown | — |
| `undo` | Undo last action (Ctrl+Z) | — |

### Pack data
| Tool | Description | Params |
|------|-------------|--------|
| `get_loaded_pack` | Info about the loaded pack | — |
| `list_items` | List items with state | `filter` (opt) |
| `list_locations` | List locations with accessibility | `filter` (opt) |
| `get_item_by_name` | Find item by exact name | `name` (req) |
| `find_item_by_code` | Find item by internal code (e.g. `bow`) | `code` (req) |
| `get_item_details` | Extended item details | `name` (req) |
| `set_item_state` | Directly set item state | `name` (req), `value` (req) |
| `batch_toggle_items` | Toggle multiple items in one transaction | `names` (req, comma-separated) |

### Locations
| Tool | Description | Params |
|------|-------------|--------|
| `get_location` | Full location details | `name` (req) |
| `pin_location` / `unpin_location` | Pin/unpin a location | `name` (req) |
| `list_pinned_locations` | List pinned locations | — |
| `clear_section` | Clear a section (decrement chest count) | `locationName`, `sectionName` (req) |
| `unclear_location` | Restore section chest counts | `name` (req) |
| `check_accessibility` | Accessibility breakdown for a location | `name` (req) |
| `list_accessible_locations` | Locations by accessibility level | `level` (opt) |
| `check_code` | Query providers satisfying a code | `code` (req) |

### Settings / window / UI
| Tool | Description | Params |
|------|-------------|--------|
| `get_settings` | All app settings | — |
| `set_setting` | Set a setting (keys include `mapEnabled`, `swapLeftRight`, `ignoreAllLogic`, etc.) | `key` (req), `value` (req, true/false) |
| `get_layout_scale` / `set_layout_scale` | Layout zoom (100–500) | `scale` (req) |
| `open_broadcast_view` / `close_broadcast_view` | Broadcast window | — |
| `open_developer_terminal` | Developer terminal window | — |
| `capture_developer_terminal` | Screenshot terminal as base64 PNG | — |
| `click_at` | Click at pixel coords | `x`, `y` (num), `button` (opt) |
| `send_key` | Send keyboard input | `key`, `modifiers` (opt) |
| `get_window_bounds` | Main window position/size | — |
| `list_ui_elements` | List visible UI elements | `type` (opt) |
| `capture_main_window` / `capture_broadcast_view` | Screenshot as base64 PNG | — |

### Notes / save / extensions / Lua / misc
| Tool | Description | Params |
|------|-------------|--------|
| `add_note`, `get_notes`, `clear_notes` | Location notes | `locationName`, `text` |
| `save_progress`, `load_progress`, `list_save_files` | Save/load state | `path` |
| `list_extensions` | Registered extensions + status | — |
| `get_autotracker_status` | Auto-tracker connection status / providers / devices | — |
| `get_console_log` | Recent log lines | `lastN` (opt, default 50) |
| `execute_lua` | Run Lua in the pack's environment | `code` (req) |
| `get_lua_global` | Read a Lua global | `name` (req) |
| `push_notification` | UI notification | `type`, `markdown`, `timeout` |
| `get_image_queue_status` | Async image queue status | — |

### Auto-tracker control (headless driving — added for smoke testing)
| Tool | Description | Params |
|------|-------------|--------|
| `autotracker_select_provider` | Select provider by UID (`sni`/`nwa`) + refresh devices | `uid` (req) |
| `autotracker_select_device` | Select default device by id/display name | `deviceId` (req), `providerUid` (opt) |
| `autotracker_set_option` | Set provider option (SNI `address_space`, `memory_mapping`) | `key`, `value` (req) |
| `autotracker_start` | Start auto-tracking on selected provider/device | — |
| `autotracker_stop` | Stop auto-tracking | — |
| `autotracker_get` | Rich status: providers, devices, options, segments/timers, connection | — |
| `autotracker_wait_connected` | Wait until Connected (predicate, no sleeps) | `timeoutMs` (opt) |
| `autotracker_wait_item` | Wait until an item reaches an active state | `name`, `itemActive` (req), `timeoutMs` (opt) |

## Diagnostics

- Look for `[MCP] Server listening on port 27125` in the console or log file.
- `McpStatusIndicator` (a small status-bar dot) turns green when the server is
  listening and shows "connected" while a client is active (60 s TTL).
- No auth; bound to localhost only. `EMOTRACKER_MCP_PORT` is the only
  configuration.
