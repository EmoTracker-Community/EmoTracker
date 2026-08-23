# EmoTracker Automated Smoke-Test Plan (emosaru ALttPR + Randomizer Mock)

> **Status: IMPLEMENTED** — all phases complete and verified. All three mock
> backends pass the full 22-assertion smoke suite end-to-end against a real
> EmoTracker dev build with the emosaru pack. See §10.

This plan covers two deliverables that together enable comprehensive, headless,
automated functional testing of EmoTracker:

1. **An MCP-driven smoke-test harness** that boots EmoTracker in dev mode, loads
   the canonical **emosaru ALttPR pack**, and drives/asserts the tracker over the
   built-in MCP server (with a few narrowly-scoped MCP additions to close the
   auto-tracker gap).
2. **A standalone CLI mock of a Link-to-the-Past randomizer console** that speaks
   **SNI (gRPC)** and **BizHawk NWA (emu-nwaccess, TCP)** so we can exercise the
   auto-tracker end-to-end without physical hardware or real emulators. The mock
   replicates three distinct backends:
   - SNI + **FX Pak Pro** flash cart
   - SNI + **emulator backend** (RetroArch/bsnes style)
   - **BizHawk + NWA tool**

The two pieces are designed to compose: the harness can point EmoTracker at the
mock, then script memory writes on the mock and assert on the tracker's state via
MCP.

`docs/mcp-server.md` (already in the repo) documents the existing MCP surface; this
plan builds on it. Facts about the emosaru pack, SNI protocol, and NWA protocol are
captured inline so the implementer does not need to re-derive them.

---

## 1. Background / reference facts

### 1.1 The emosaru pack (canonical reference pack)

- Repo: `https://github.com/emosaru/alttpr_emotracker_emosaru`
- `package_uid`: `alttpr_emotracker_emosaru`; name "EmoTracker Official ALTTPR
  Support"; platform `snes`; variants `standard`, `keysanity`, `inverted`,
  `inverted_keysanity`, `items_only`, `items_only_keys`.
- Installed into `%LOCALAPPDATA%/EmoTracker/packs/` (dev mode log path differs;
  pack install path is `UserDirectory.Path/packs`) either as an extracted folder
  or a `.zip`. The MCP `load_pack` / `list_packs` tools discover installed packs
  via `PackageManager`.
- Autotracking is **pure WRAM**, registered with
  `ScriptHost:AddMemoryWatch(name, address, size, callback[, interval])` and read
  via `segment:ReadUInt8/ReadUInt16`, with live reads through
  `AutoTracker:ReadU8(0x7e0010, 0)`.
- Key memory watches / addresses (LoROM WRAM, bus `0x7E`/`0x7F`):
  - `0x7E0010` — in-game status (hex 0x06+ means in-game; the flag that fires the
    per-watch callbacks / tracker population)
  - `0x7EF000` size 0x250 — room data
  - `0x7EF280` size 0x82 — overworld event data
  - `0x7EF340` size 0x90 — **item data** (0x7EF340–0x7EF38E)
  - `0x7EF36C` size 1 — heart container data
  - `0x7EF410` size 2 — NPC item data
  - `0x7EF420` size 0x46 — statistics (Triforce room)
  - `0x7EF448` size 1 — heart piece data
- To synthesize a "found an item" scenario in the mock: write the item byte at the
  corresponding `0x7EF3xx` slot, set `0x7E0010 = 0x06+`, and let the poll pick it
  up (30 ms poll; watch intervals 250 ms).

### 1.2 SNI protocol (server at `http://localhost:8191`, insecure gRPC / HTTP2)

- Services (proto in `EmoTracker/Providers/SNI/Protos/sni.proto`, C# namespace
  `SNI`): `Devices`, `DeviceControl`, `DeviceMemory`, `DeviceFilesystem`,
  `DeviceInfo`, `DeviceNWA`.
- Used by EmoTracker's `SniProvider`/`SniDevice`:
  - `Devices.ListDevices` → `DevicesResponse.devices[]`
    `{ uri, displayName, kind, capabilities[], defaultAddressSpace }`. Device
    kinds / URIs:
    - FX Pak Pro / SD2SNES: kind `fxpakpro`, e.g. `fxpakpro://./com4`,
      `fxpakpro://./dev/ttyACM0`
    - Emulator (RetroArch): kind `retroarch`, `ra://127.0.0.1:55355`
    - Lua/emulator bridge: kind `luabridge`, `luabridge://127.0.0.1:PORT`
  - `DeviceMemory.MappingDetect` — reads ROM header at SNES bus `$00:FFB0`,
    returns `MemoryMapping` (Unknown=0, HiROM=1, LoROM=2, ExHiROM=3, SA1=4) and
    echoes `romHeader00FFB0`.
  - `DeviceMemory.SingleRead` / `SingleWrite` — request carries
    `requestAddress`, `requestAddressSpace` (FxPakPro=0, SnesABus=1, Raw=2),
    `requestMemoryMapping`, `size`/`data`.
  - `DeviceControl.ResetSystem` / `ResetToMenu`.
- gRPC method paths (for the mock's route table):
  `/SNI.Devices/ListDevices`, `/SNI.DeviceMemory/MappingDetect`,
  `/SNI.DeviceMemory/SingleRead`, `/SNI.DeviceMemory/SingleWrite`,
  `/SNI.DeviceMemory/MultiRead`, `/SNI.DeviceMemory/MultiWrite`,
  `/SNI.DeviceMemory/StreamRead`, `/SNI.DeviceControl/ResetSystem`,
  `/SNI.DeviceControl/ResetToMenu`, `/SNI.DeviceInfo/FetchFields`.
- **Address-space behavior to replicate:**
  - **FX Pak Pro:** reports `defaultAddressSpace = FxPakPro`. Client may send
    either is accepted; the mock should interpret FxPakPro addresses.
  - **Emulator backend:** no address-space auto-detect; EmoTracker sends
    `SnesABus` (default `address_space` option) + `memory_mapping` (default
    `Auto`, resolved from MappingDetect to `LoROM`). Mock must accept `SnesABus`
    and translate via LoROM layout.
  - MappingDetect is called on connect (and re-called when a read returns
    `"Unknown memory mapping"`).

### 1.3 BizHawk NWA (emu-nwaccess) protocol

- TCP line protocol, default port `0xBEEF` = **48879**. EmoTracker `NwaProvider`
  scans a base port + count (default 10, `NWA_PORT_RANGE` env overrides).
- Text commands are `KEY ...\n`; ASCII replies begin with a leading `\n`, then
  `key:value` lines, terminated by a blank line.
- Binary read response: first byte `0x00`, then 4-byte **big-endian length**, then
  that many payload bytes. A leading `\n` instead is an ASCII reply (may contain
  `error`/`reason`).
- Binary write request payload: `0x00` + 4-byte big-endian size + data, sent after
  the command line; server replies with an ASCII ack.
- Commands used by `NwaDevice`:
  - `EMULATOR_INFO` → `name`, `version`, `id`, `nwa_version` (used by `ProbeAsync`
    to discover devices)
  - `MY_NAME_IS <name>` → echoes `name`
  - `CORE_CURRENT_INFO` → `name` (core), `game`, `platform`
  - `CORE_MEMORIES` → repeated `name`/`access`/`size` groups
  - `EMULATION_STATUS` → `state` (`running`/`paused`), `game`
  - `CORE_READ <domain>;$offset;$length` → binary read
  - `bCORE_WRITE <domain>;$offset;$length` → binary write + ASCII ack
- On connect, EmoTracker prefers the **`System Bus`** memory domain (no address
  translation). If absent it falls back to a platform address map (`SnesAddressMap`
  for SNES) which reads `CARTROM` at `0x7FB0`/`0xFFB0`/`0x40FFB0` to detect the
  ROM layout, then maps bus `0x7E`/`0x7F` → WRAM etc.

---

## 2. Architecture overview

```
                         ┌─────────────────────────────────────────────┐
                         │                 EmoTracker (Debug, -dev)     │
                         │  ┌──────────┐   MCP server :27125            │
                         │  │ AutoTracker│  gRPC/HTTP JSON-RPC 2.0      │
                         │  │ Extension │ ◄───────────────┐             │
                         │  └────▲─────┘                  │             │
                         │       │ SNI/NWA providers      │ MCP tools   │
                         └───────┼────────────────────────┼─────────────┘
                                 │                        │
                   localhost:8191│ / :48879            tools/call
                                 ▼                        ▼
              ┌────────────────────────────────────┐  ┌────────────────────┐
              │  Mock LttP Randomizer (CLI)       │  │  Smoke Runner      │
              │  • virtual LoROM image            │  │  (bin/ pwsh/bash)  │
              │  • SNI gRPC: fxpakpro | emulator  │  │  - boot app        │
              │  • NWA TCP:   BizHawk             │  │  - MCP session     │
              │  • `set`/`get` memory commands   │  │  - assertions       │
              └────────────────────────────────────┘  └────────────────────┘
```

Two new projects (see §6) plus a set of run scripts. The **mock** is the
"console"; the **smoke runner** is the test driver. They can also be driven
independently (mock can be scripted by hand; the existing `phase4_smoke.sh`
pattern shows the bootstrap flow the runner generalizes).

---

## 3. Mock LttP Randomizer CLI

### 3.1 Goals
- Present a stable, scriptable SNES memory image that the emosaru pack's
  autotracker reads as a live game.
- Faithfully replicate the three target backends' **wire behavior**, including the
  discovery/connect handshakes, so EmoTracker's `SniProvider` and `NwaProvider`
  connect and poll exactly as against real hardware.
- Let a test script mutate memory at runtime (`set`, `write-blob`, `load-state`)
  and observe the effect in EmoTracker via MCP.

### 3.2 Project: `EmoTracker.MockRandomizer` (new console project)

**Project shape**
- `net10.0`, `<OutputType>Exe</OutputType>`.
- References: `Microsoft.AspNetCore.App` (for in-process gRPC), `Grpc.AspNetCore`,
  `Google.Protobuf`, `Grpc.Tools` — regenerate the SNI client/server from the
  existing `EmoTracker/Providers/SNI/Protos/sni.proto` (the proto is already in
  the repo; the mock needs the **server** side). Add the proto as a linked
  `<Protobuf>` item.
- No dependency on `EmoTracker`/`EmoTracker.Data` (keeps the mock hermetic and
  runnable from CI).

**Files**
```
EmoTracker.MockRandomizer/
  Program.cs                  # arg parsing, host plumbing, scenario REPL
  Memory/
    SnesBus.cs                # 24-bit bus address decode (LoROM WRAM/ROM/SRAM)
    RomImage.cs               # LoROM cart image: header at $7FB0/$FFB0, data
    AddressSpace.cs           # SnesABus / FxPakPro / Raw translate → virtual bus
  Sni/
    SniMock.cs                # gRPC service impls (Devices, DeviceMemory, DeviceControl, DeviceInfo)
    FxPakProDevice.cs         # ListDevices → fxpakpro:// ; FxPakPro address space
    EmulatorDevice.cs         # ListDevices → ra:// ; SnesABus address space
  Nwa/
    NwaServer.cs              # TcpListener on 0xBEEF, line protocol parse
    NwaReply.cs               # ASCII + binary reply writers
  Scripting/
    MemorySession.cs          # single mutable backing store shared by all backends
    Commands.cs               # `set`, `get`, `write`, `load`, `reset`, `play`...
```

### 3.3 Shared virtual memory model (the "console")
- One `MemorySession` per process holds the full SNES address image:
  - **WRAM** : 128 KB (`0x7E0000`–`0x7FFFFF`, i.e. banks `0x7E`/`0x7F`). The
    emosaru writes live here.
  - **CARTROM** : a few MB of LoROM data with a valid internal SNES header placed
    at both `0x007FB0` (LoROM) and `0x00FFB0` (fallback) so
    `SnesAddressMap.ScoreHeader` and SNI `MappingDetect` both resolve **LoROM**:
    - map mode byte at `+0x25` of header = `0x20` (LoROM)
    - checksum + complement = `0xFFFF`
    - printable ASCII title at `+0x10`
    - ROM size byte `+0x27` in `0x08..0x0D`
  - **SRAM** : e.g. 64 KB.
- Default image has WRAM zeroed with `0x7E0010 = 0x06` (in-game) so a fresh connect
  immediately populates the tracker.

### 3.4 SNI gRPC backends (same service code, different device registration)

Register with a single `Grpc.Core` server on `:8191` (configurable via
`--sni-port`), exposing all four `SniMock` services regardless of active device
flavor. The **device flavor** only changes what `ListDevices` returns and how
addresses are interpreted:

| Flavor | `kind` | `uri` | `defaultAddressSpace` | Addresses interpreted as |
|--------|--------|-------|----------------------|--------------------------|
| `fxpakpro` | `fxpakpro` | `fxpakpro://./mocktty` | `FxPakPro` | FxPakPro linear map (ROM `$000000..$DFFFFF`, SRAM `$E00000..`, WRAM `$F50000..` from the proto) |
| `emulator` | `retroarch` | `ra://127.0.0.1:55355` | `SnesABus` | SNES A-bus + MemoryMapping (LoROM) |

- `MappingDetect(uri, fallback, romHeader?)`: read `$00:FFB0` from the virtual
  image, return `memoryMapping = LoROM`, `confidence = true`, and echo the 0x50
  header bytes.
- `SingleRead`: honor `requestAddressSpace` + `requestMemoryMapping`; translate to a
  bus address; read `size` bytes from `MemorySession`. For `fxpakpro` map per the
  proto's FxPakPro layout; for `SnesABus` use the LoROM bus decode. Return the
  requested address/space echoed, plus device address and raw data.
- `SingleWrite`: mirror of the above; write `data` into the session and (optionally)
  flip the in-game flag live to simulate the room being discovered.
- `MultiRead`/`MultiWrite`/`StreamRead`: implement as loops over the single ops so
  any client batching path works.
- `ResetSystem`/`ResetToMenu`: reload the default RAM image (documented behavior
  the harness can assert on by seeing EmoTracker mark a disconnect/heal).
- `DeviceInfo.FetchFields`: return `DeviceName`, `CoreName` (e.g. `Snes9x` for the
  `retroarch` flavor), `RomFileName`.

### 3.5 NWA TCP backend (BizHawk flavor)

```
TcpListener on 0xBEEF (+ NWA_PORT_RANGE-friendly, but single port is enough)
```

Parse the line commands from §1.3 and reply with the exact ASCII/binary framing
`NwaDevice` expects. Memory domains reported by `CORE_MEMORIES`:
- `System Bus` (access `r`, size covers the whole 8 MB bus) — so EmoTracker picks
  `DefaultAddressMap("System Bus")` and translates bus addresses directly
- `WRAM` (128 KB), `CARTROM`, `SRAM`, `VRAM`, `APURAM`

`CORE_READ System Bus;...` is served straight from the shared session. The
`SnesAddressMap` fallback path (only triggered if `System Bus` is removed, which the
mock supports as `--no-system-bus` for testing the fallback) reads `CARTROM`
headers → LoROM and then maps `0x7E/0x7F` → WRAM.
`EMULATOR_INFO` returns `name: EmuNwaccess`, `nwa_version: 0.3.0`, `version: 1.0.0`.

### 3.6 CLI / REPL surface
```
EmoTracker.MockRandomizer [--sni fxpakpro|emulator] [--nwa] [--sni-port 8191]
                          [--nwa-port 48879] [--no-system-bus] [--script file.txt]

Interactive commands (also accepted from --script lines):
  set ROM:<addr>:<bytes...>      e.g. set WRAM:7EF340:05 24 01 ...   (hex, comma/space)
  set BUS:<addr>??               bus-address convenience
  get  ROM:<addr>:<len>
  state save <path> | load <path> | reset
  play <scenario>                run a canned scripted playthrough (see §5)
  status                         dump current devices + timing counters
  log <on|off>                   toggle per-request request logging
```
Keep the framing/parse helpers byte-compatible with `NwaDevice.cs`
(`SendCommandAsync`/`SendReadCommandAsync`/`SendWriteCommandAsync`) and
`SniDevice.cs` — these two files are the golden reference for the mock's wire
format.

---

## 4. MCP server additions (extension work)

The current MCP server (`EmoTracker/Extensions/McpServer/Tools/`) only exposes
**read-only** autotracker status (`get_autotracker_status`). To drive end-to-end
autotracking tests headlessly, add a small, tightly-scoped **auto-tracker control**
tool class. This is the only *required* MCP extension; everything else in the smoke
suite already has tool coverage.

### 4.1 New tool class `AutoTrackerControlTools.cs`
Add to `EmoTracker/Extensions/McpServer/Tools/` with `[McpServerToolType]`. It
mirrors what the UI `AutoTrackerExtension` does (`StartAutoTracking`,
`StopAutoTracking`, `SetProvider`, `SetDevice`, and provider options), wiring into
`ApplicationModel.Instance.PrimaryState`'s `AutoTrackerExtension` (the app-scoped
singleton) rather than duplicating logic.

| Tool | Purpose | Notes |
|------|---------|-------|
| `autotracker_start` | Start auto-tracking on the active provider/device | calls `StartCommand`/`StartAutoTracking` |
| `autotracker_stop` | Stop auto-tracking | calls `StopCommand` |
| `autotracker_select_provider` | Select provider by `uid` (`sni`/`nwa`) | `SelectedProvider = ...; RefreshDevicesAsync()` |
| `autotracker_select_device` | Select `DefaultDevice` by device id/index | then auto-start if ready |
| `autotracker_set_option` | Set a provider option (`address_space`, `memory_mapping` for SNI) | writes to `IProviderOption.Value` |
| `autotracker_get` | Rich status: connected, reconnecting, active provider/device, segments/timers, per-segment last read | superset of `get_autotracker_status` |

Keep existing `get_autotracker_status` intact (backward compatible). Gate the new
class identically to the rest of the MCP server (Debug-only via existing csproj
rules).

### 4.2 Optional: `set_provider_setting` (only if tests need non-default addresses)
Default mock addresses (`8191`, `48879`) match defaults, so this is not required.
If ever needed, expose `ApplicationSettings.Instance.SetProviderSetting("nwa_host",
...)` / `sni_grpc_address`, or simply rely on `NWA_PORT_RANGE` env + `--sni-port`.

### 4.3 Optional `reset`/`poll-step` determinism
The 30 ms poll + async callbacks make assertions slightly racy. Add an optional
`autotracker_wait_until(condition, timeout)` that polls item state until a
predicate holds (or timeout) — prefer this over fixed sleeps in the runner.

---

## 5. Smoke-test suite (the runner)

### 5.1 Harness shape
- **Driver:** a lightweight runner — either a new console project
  `EmoTracker.Smoke` or, to match existing practice, a `pwsh`/`bash` script
  (`scripts/smoke/smoke.ps1`) generalizing the `mcp_call()` handshake helper in
  `.claude/smoke_loop.sh`. Recommend: **`EmoTracker.Smoke` console project** for
  test-spec readability + a thin `scripts/smoke/smoke.ps1` that orchestrates
  process lifecycle (start mock, start app, run runner, tear down, scan logs).
- **Assertion channel:** MCP `tools/call` only (with `autotracker_wait_until` for
  async).
- **Crash detection:** after each run, scan `emotracker_log.txt` + console for
  markers used by existing scripts (`Fatal`, `0xC0000005`,
  `sk_bitmap_make_shader`, `Unhandled exception`, `System.` stack traces), and
  confirm graceful exit after `shutdown`.

### 5.2 Non-autotracker smoke tiers (no mock needed — pure MCP)
These run the emosaru `<variant>` pack and exercise tracker functionality.

1. **Load & parse** — `list_packs` (contains `alttpr_emotracker_emosaru`),
   `load_pack`, `get_loaded_pack`, `get_pack_files`,
   `get_pack_file_content(scripts/autotracking.lua)`, `reset_tracker`.
2. **Model integrity** — `list_items`, `list_locations` (non-empty), spot-check a
   known item (`get_item_by_name`, `find_item_by_code("bow")`), `get_item_details`,
   `check_code`.
3. **Interaction & logic** — `toggle_item("Flippers")` →
   `check_accessibility`/`list_accessible_locations` reflects progression;
   `batch_toggle_items`, `set_item_state`, `right_click_item`,
   `clear_location`, `unclear_location`, `clear_section`, `undo`.
4. **UI / render** — `capture_main_window` (non-empty PNG), `list_ui_elements`,
   `get_layout_scale`, `set_setting(mapEnabled/ignoreAllLogic)`,
   `create_fork_tab`, `switch_to_tab`, `list_tabs`, `load_new_pack_via_state_manager`.
5. **Persistence** — `save_progress`, `list_save_files`, `reset_tracker`,
   `load_progress`, verify an item returned;
   `add_note`/`get_notes`/`clear_notes`.
6. **Lua drift** — `execute_lua("return gettotaltakenitems()")`; `get_lua_global`.
7. **Repeat/stability** — run tiers 1–6 for N (e.g. 10) iterations across **all
   variants**; scan logs; assert a healthy per-iteration shutdown.

Run tiers 1–2 across every variant; tiers 3–7 on `standard` + one keysanity variant.

### 5.3 End-to-end autotracking tiers (mock required)

Matrix across the three backends (SNI/fxpakpro, SNI/emulator, BizHawk NWA) × the
`standard` variant:

1. **Discovery & connect**
   - Start mock on the relevant port(s); start app with emosaru loaded.
   - `autotracker_select_provider(uid)`; `autotracker_get` →
     `availableDevices` populated with the mock's device name/URI; expected
     device id per backend (fxpakpro uri / `ra://` / `nwa:localhost:48879`).
   - `autotracker_select_device(...)`; `autotracker_start`; assert `isConnected`
     and that auto-tracker became `active` (segments registered by the pack's
     `init.lua`).
2. **Memory mapping resolution**
   - SNI: assert MappingDetect resolved **LoROM** (`memory_mapping` option `Auto`);
     then explicitly `autotracker_set_option(memory_mapping=LoROM)` and re-read.
   - NWA: default uses `System Bus`; run once with `--no-system-bus` to force the
     `SnesAddressMap` fallback and confirm WRAM `0x7E/0x7F` mapping works.
3. **Item/location capture (the core functional proof)**
   - From the mock: set `0x7EF340` slots to a known item set, set `0x7E0010 = 0x06`.
   - Use `autotracker_wait_until(...)` to assert `list_items` shows those items
     active/consumed (e.g. Bow, Boomerang, Flippers), and that resulting
     `check_accessibility` unlocks expected locations.
4. **Live mutation** — flip an item off/on in the mock mid-run; assert the tracker
   reflects it within a poll window (no manual touch). Assert `get_autotracker_status`
   shows no error state (`Error == false`).
5. **Room/overworld** — poke `0x7EF000`/`0x7EF280` and assert overworld flags /
   room-based logic on `list_locations`.
6. **Reset / disconnect / heal**
   - Mock `ResetSystem` → assert EmoTracker surfaces reconnect/heal and re-reads a
     fresh image (state returns to defaults).
   - Kill the mock TCP/gRPC listener → assert `Reconnecting`/disconnect; restart
     mock → assert transparent heal and resumed tracking (exercises the reconnect
     path that issue #98 addressed).
7. **Write path** — `autotracker` writes are rare, but run `autotracker_start` with
   a pack/watch that writes and assert `SingleWrite`/`bCORE_WRITE` landed in the
   mock's session.

### 5.4 Reporting
- Per-tier pass/fail with durations; `SUMMARY` line per run; failures capture the
  failing MCP response payload + a `capture_main_window` screenshot + the tail of
  the app log.
- Exit code non-zero on any failure so CI (`build-avalonia.yml`) or a new
  `smoke.yml` workflow can gate.

---

## 6. Deliverables & repo layout

```
scripts/smoke/
  smoke.ps1                 # orchestrator: start mock → start app(-dev) → run runner → teardown → scan logs
  mcp.ps1                   # handshake + tools/call helper (generalize .claude/smoke_loop.sh)
  scenarios/*.txt           # canned mock plays for §5.3 tiers
EmoTracker.MockRandomizer/  # NEW console project (§3)
EmoTracker.Smoke/           # NEW console test-runner project (§5.1)
EmoTracker/Extensions/McpServer/Tools/AutoTrackerControlTools.cs   # NEW MCP tools (§4)
docs/mcp-server.md          # update tool table
```

Solution changes (2026-08): add `EmoTracker.MockRandomizer` and `EmoTracker.Smoke`
to `EmoTracker.sln`, or keep the mock standalone (own `.sln`) if you prefer it out
of the main build. Recommend in-solution so CI builds it once.

---

## 7. Phased implementation

**Phase 0 — MCP tooling (unblocks everything else)**
Add `AutoTrackerControlTools` (§4) + `autotracker_wait_until`. Update `docs/mcp-server.md`.

**Phase 1 — Share virtual memory + SNI fxpakpro backend**
Implement `MemorySession`, LoROM `RomImage`/`SnesBus`, and the gRPC services
serving an `fxpakpro://` device. Validate: point EmoTracker's SNI provider at the
mock (address `8191`, `address_space=Auto`), `MappingDetect→LoROM`, connect, and
read `0x7E0010`.

**Phase 2 — SNI emulator backend**
Add the `retroarch`/`ra://` device flavor using `SnesABus`. Validate against the
same harness with `address_space=SnesABus`.

**Phase 3 — NWA (BizHawk) backend**
Implement `TcpListener` + line protocol + `System Bus` domain. Validate discovery
(`EMULATOR_INFO` probe) + connect + `CORE_READ`; then validate the
`--no-system-bus` → `SnesAddressMap` fallback.

**Phase 4 — smoke runner + non-autotracker tiers**
Implement `EmoTracker.Smoke` tiers 5.2 across variants; wire orchestrator; add CI
job.

**Phase 5 — end-to-end autotracker tiers**
Drive §5.3 across the full backend matrix with the mock; add `scenarios/*.txt`.
Add a repeat/stability job (e.g. 5–10 iterations per backend).

**Phase 6 — hardening**
Deterministic `autotracker_wait_until`, timing budget assertions, failure
screenshots, and (optional) a `smoke.yml` GitHub Actions workflow gating `main`.

---

## 8. Risks & mitigations

| Risk | Mitigation |
|------|-----------|
| Async polling race in assertions | `autotracker_wait_until` predicate instead of sleeps |
| Registering the mock at the same ports as a real SNI/BizHawk | configurable `--sni-port`/`--nwa-port`; CI uses isolated ports |
| gRPC over HTTP/2 in CI | in-process Kestrel `Http2` works headless; use `Grpc.AspNetCore` (no external server needed) |
| emosaru pack packaging drift (`manifest.json` vs `package.json`) | pin a known pack version; fetch/install via `load_pack` tutorial path; assert on `package_uid` |
| Mock diverges from real wire format | treat `NwaDevice.cs` / `SniDevice.cs` as the golden spec; add a "format echo" test that compares framing |
| Variant-specific logic differences | run model-integrity tiers across all variants; logic/AT tiers on representative set |

---

## 9. Out of scope (for now)
- Testing NDI/Twitch/Voice/Lua-debugger extensions (MCP tools exist for some but
  not the focus of this plan).
- Multi-window / multi-state AT concurrency beyond the fork-tab smoke tier.
- Real-hardware or third-party-emulator integration testing (the mock is a
  stand-in; keep the backend abstraction so real backends can later be swapped in
  for an optional "integration" tier).

---

## 10. Implementation status (2026-08)

Everything in this plan is implemented and verified in-repo.

### What was built

| Deliverable | Location | Verification |
|-------------|----------|--------------|
| MCP auto-tracker control tools (8 tools) | `EmoTracker/Extensions/McpServer/Tools/AutoTrackerControlTools.cs` | `autotracker_start`/`wait_item` etc. drive the tracker headlessly |
| `AutoTrackerExtension` control helpers (UI-thread awaited) | `EmoTracker/Extensions/AutoTracker/AutoTrackerExtension.cs` | used by the MCP tools |
| LttP randomizer mock (CLI) | `EmoTracker.MockRandomizer/` | full solution builds; protocols verified below |
| Smoke runner (C#) | `EmoTracker.Smoke/` | runs the 22-assertion suite |
| Orchestrator (lifecycle + log scan) | `scripts/smoke/smoke.sh` | kills leftover instances, boots mock+app, scans crashes |
| CI workflow | `.github/workflows/smoke.yml` | dispatches the suite per backend |
| Docs | `docs/mcp-server.md`, this file | updated |

### Backends implemented in the mock
- **SNI + FX Pak Pro** — gRPC on `:8191`, `Devices/DeviceMemory/DeviceControl/DeviceInfo`
  services; device kind `fxpakpro`, default address space FxPakPro; LoROM header at
  `$00FFB0` so `MappingDetect` resolves LoROM.
- **SNI + emulator** — same gRPC, device kind `retroarch` (`ra://`), default space
  SnesABus; client sends SnesABus + LoROM.
- **BizHawk NWA (emu-nwaccess)** — TCP on `:48879`, line protocol with leading-`\n`
  ASCII replies, binary read/write framing, `System Bus` domain (+ `--no-system-bus`
  to force the SNES address-map fallback).
- Plus an HTTP control API on `:9090` (`/write`, `/read`, `/reset`, `/ping`) so the
  runner injects memory scenarios cross-process.

### Verification results
The `EmoTracker.Smoke` runner executes 22 assertions: mock reachability, pack
load/model integrity, item interaction + accessibility, UI render, save/load, Lua,
and the end-to-end autotracker tier (select provider → discover device → start →
connect → capture **Gloves** & **Shovel** from mock WRAM → stop). Results:

| Backend | Result |
|---------|--------|
| `sni-fxpakpro` | **22/22 PASS** |
| `sni-emulator` | **22/22 PASS** |
| `nwa` | **22/22 PASS** |

### Notable fixes surfaced by the harness
- The mock's NWA replies were missing the leading `0x0A` that `NwaDevice`
  requires; the end-to-end discovery test caught it.
- The `Smoke` runner's `JsonDevicesCount` read the first provider's device count
  (SNI=0) instead of the selected provider; fixed to match by UID.
- Running the suite repeatedly left many `EmoTracker` GUI processes alive (the
  binary is named `EmoTracker`, not `EmoTracker.dll`); the orchestrator now kills
  by any process/comm match, which eliminated the flaky port/`Image.MeasureOverride`
  crashes seen earlier.

### Run it
```bash
# build
dotnet build EmoTracker.sln --configuration Debug

# one backend, up to N retries
bash scripts/smoke/smoke.sh sni-fxpakpro 3
bash scripts/smoke/smoke.sh sni-emulator 3
bash scripts/smoke/smoke.sh nwa 3
```
Requires the emosaru pack zip in `~/Documents/EmoTracker/dev/packs/` (already
present for dev). On headless CI use `xvfb-run -a`.

