# Augmenting the EmoTracker Core Smoke Test (item-type + interaction matrix)

Status: **Plan** (not yet implemented). Companion to `docs/smoke-test-plan.md`,,
which described the harness infrastructure (MCP autotracker control, the LttP
randomizer mock, and a 22-assertion runner that currently exercises only a small
slice of the non-autotracker surface — a single `toggle_item`, a single
`check_accessibility`, screenshot, save/load, Lua).

This plan expands the **non-autotracker core tier** into an exhaustive,
per-type functional matrix. Minimum requirement: **functional tests for every
concrete `ItemBase` subclass** (10 total), plus an expanded locations/maps/
accessibility interaction suite.

---

## 1. Scope & goals

### 1.1 Concrete item types to cover (all 10)

| # | C# type | Pack tag | In emosaru? | Observable state today (MCP) |
|---|---------|----------|-------------|------------------------------|
| 1 | `ToggleItem` | `toggle` | ✅ (30) | `active`, `loop` |
| 2 | `ConsumableItem` | `consumable` | ✅ (16) | `acquiredCount`, `consumedCount`, `availableCount`, `min/maxCount`, `countIncrement` |
| 3 | `ProgressiveItem` | `progressive` | ✅ (6) | `currentStage`, `loop` |
| 4 | `ProgressiveToggleItem` | `progressive_toggle` | ✅ (50) | **none** (only common fields) |
| 5 | `CompositeToggleItem` | `composite_toggle` | ✅ (1: Boomerangs) | **none** (inspect sibling items `blue_boomerang`/`red_boomerang`) |
| 6 | `SectionChestsProxyItem` | `sectionchests` | ✅ (13) | **none** (inspect the proxied `Section` via locations) |
| 7 | `StaticItem` | `static` | ✅ (15) | **none** (no state; assert `type` + immutable) |
| 8 | `ToggleBadgedItem` | `toggle_badged` | ❌ | **none** |
| 9 | `BlankItem` | `blank` | ❌ | **none** (no-op by design) |
| 10 | `LuaItem` | `lua` | ❌ | **none** (opaque Lua state; `[DisallowCreationFromTag]`) |

The emosaru ALttPR pack inherently covers **7 of 10**. The three that require a
synthetic source — `toggle_badged`, `blank`, and `lua` — are handled by a
dedicated **all-item-types test pack** (§4). This preserves real-world coverage
(from the canonical pack) *and* guarantees 100% type coverage.

### 1.2 Interaction dimensions to test (beyond "click it")
- **Left/right click semantics** (per type — toggle vs. advance vs. increment vs. no-op vs. delegating to children).
- **Loop / swap-actions** behavior (non-loop toggles clamp; loop wraps; `SwapActions` inverts click/badge).
- **Code provisioning** (`CanProvideCode`/`ProvidesCode`) and `AdvanceToCode` — assert accessible-location transitions.
- **Consumer/provider linkage** (consuming `ToggleItem` ↔ `ConsumableItem`; `Consume`/`Release` filtering and no-op cases).
- **Composite / proxy delegation** (clicks mutate referenced siblings/sections).
- **Persistence fidelity** (save→reset→load preserves each type's state).
- **Undo/redo** across each mutation type.

---

## 2. Recommended MCP extensions (prerequisite for observable assertions)

Today `toggle_item`/`right_click_item`/`get_item_details` only surface state for
`ToggleItem`, `ConsumableItem`, `ProgressiveItem`. To assert behavior for the
other seven types we need narrow additions. These are additions only — existing
tools/responses stay byte-compatible.

### 2.1 Extend `PackDataTools.SerializeItemDetails` + `GetItemByName`/`ListItems`
Add per-type state fields so tests can assert on the result of `get_item_details`:
- `ProgressiveToggleItem` → `active` (bool), `currentStage` (uint), `stageCount`, `swapActions`.
- `ToggleBadgedItem` → `active` (bool), `baseItem` (name), `badgeText`.
- `CompositeToggleItem` → `leftActive`, `rightActive`, `left`, `right` (resolved names) — resolved through its two `ModelReference<ToggleItem>` children.
- `SectionChestsProxyItem` → `count`, `chestCount`, `available` (resolved section).
- `StaticItem` / `BlankItem` → `capturable`, `codeCount` (indicates they carry/seed codes; no mutable state).

### 2.2 Extend `set_item_state` to cover every mutating type
Today only toggle/progressive/consumable. Add `itemType`-aware handling for:
- `ProgressiveToggleItem` — optional `active`/`stage` (JSON object or `active|stage` string).
- `ToggleBadgedItem` — boolean `active`.
- `CompositeToggleItem` — set both children by name.
- `ConsumableItem` — also allow setting `consumedCount` (to hit `Consume`/`Release` branches deterministically).

### 2.3 New small tools (cheap, high value for exhaustive tests)
- `get_item_codes` — return `GetAllProvidedCodes()` / `CanProvideCode` truth table for an item, so tests can assert static vs. dynamic code indexing.
- `get_item_version`/`get_settings` already exist — reuse.
- `advance_item_to_code` — wrap `AdvanceToCode(code)` (currently only reachable via Lua or internal calls).
- `get_section` — a single-tool view of a section (chestCount, availableChestCount, accessibility) so `sectionchests` tests don't depend on full `list_locations`.
- `get_map_info` / `get_maps` — enumerate maps/groups for the maps tier.

### 2.4 Lua-diagnostics for `LuaItem`
Reuse existing `execute_lua` + `get_lua_global`; no new transport needed. LuaItem
state is asserted by having the test pack's `init.lua` expose counters as Lua
globals and on the item via `get_lua_global`.

> Design note: keep the per-type serializer switch in one helper
> (`SerializeItemDetails`) so `find_item_by_code`, `get_item_details`, and the
> click tools all stay consistent.

---

## 3. Core interaction matrix (runs against emosaru — 7 types + locations)

Add a `CoreItemInteractionTier` to `EmoTracker.Smoke`. Each row resets tracker
state, performs a scripted interaction, and asserts on `get_item_details` /
`list_items` / `check_accessibility`. Also wrap **every** test in an undo/redo
pair where applicable.

### 3.1 ToggleItem (`<name>`, e.g. `Lamp`) — non-loop
- L: `active == false` → `true`; second L stays `true` (non-loop clamps).
- R: `active == true` → `false`.
- Code gate: with `Lamp` active, `check_accessibility` for a lamp-keyed location (`Castle Secret Entrance/Uncle`? use a light-readable check) goes accessible.
- Undo restores prior state.

### 3.2 ToggleItem — loop (find/create a `loop:true` toggle)
- L then L oscillates `true ⇄ false`; R also toggles. (If emosaru has no loop toggle, defer to the all-types test pack §4.)

### 3.3 ConsumableItem (`Palace of Darkness Small Key`, max 6)
- L increments `acquiredCount` (1→2→…), clamps at `maxCount`.
- R decrements, clamps at `minCount` (0).
- `availableCount = acquired - consumed`; set a `consumed` value and assert badge/`availableCount`.
- Code gate: N keys unlocks a `check_accessibility` transition; consuming reduces available.

### 3.4 Consuming ToggleItem ↔ Consumable (linkage)
- Pick a toggle with a `"consume"` code (or add one in the test pack). With `AvailableCount == 0`:
  - `toggle.Active = true` is **filtered** (stays false).
- After acquiring a consumable: enabling the toggle `Consume()`s one (available decreases); disabling `Release()`s it.
- Assert both the toggle's `active` and the consumable's `consumedCount`/`availableCount`.

### 3.5 ProgressiveItem (`Sword`, `Gloves`, `Mail`)
- With an enabled disabled-stage (default `allow_disabled=true`): `L` → `currentStage` 1→2→3…; `R` → back down; clamp at top/bottom; wrap only if `Loop`.
- Assert `check_accessibility` gates driven by stage (e.g. `Sword` stage for `check_accessibility` on a sword-required location).
- `AdvanceToCode("silvers")` jumps to the stage that provides it.

### 3.6 ProgressiveToggleItem (`Eastern Palace`, `Bombs`, `Bow`)
- L/R advance `currentStage` (mod `stageCount`); the `(active, stage)` pair selects the stage.
- Assert `active` + `currentStage` from `get_item_details`.
- `AdvanceToCode("crystal")` → `active=true` and stage holding that code (incl. `secondary_codes`).

### 3.7 CompositeToggleItem (`Boomerangs` ⇒ `blue_boomerang` + `red_boomerang`)
- L toggles left sibling; R toggles right sibling.
- Assert via `get_item_details("Blue Boomerang")` and `get_item_details("Red Boomerang")` (the composite itself has no own state).
- Assert `check_code("boomerangs")` / `find_item_by_code("blue_boomerang")` reflect children.

### 3.8 SectionChestsProxyItem (`Eastern Palace Chests` ⇒ `@Eastern Palace/Dungeon`)
- `get_section` shows `availableChestCount == chestCount`. L on the proxy decrements available (clears one chest); R restores.
- Assert the proxied section count and the item's `badgeText`.
- Non-manipulable case: with sectionNotAccessible + `AlwaysAllowClearing=false`, L is a no-op.

### 3.9 StaticItem (`Hyrule Castle` label)
- Clicks are no-ops; `capturable=false`; still **provides** its code (`check_code("hc_label")`, `find_item_by_code`).
- Asserting "carried but immutable" — a regression guard against state leakage.

### 3.10 Locations / maps / accessibility (cross-cutting)
- `check_accessibility` before/after item sets; `list_accessible_locations(level)`.
- `clear_location` / `clear_section` / `unclear_location` round-trips; section chest counts.
- `pin_location`/`unpin_location`/`list_pinned_locations`.
- `get_location` section/accessibility detail; `check_code` provider resolution.
- Map tier: `get_maps`, verify map/group enumeration renders (no crash) via `get_map_info`.

### 3.11 Variant matrix
Run the 7-type matrix across at least `standard` and **one keysanity variant**
(`keysanity`) because keysanity changes the small-key consumables and
location→logic wiring. (Optional: `inverted`, `items_only`.)

---

## 4. Full type-matrix test pack (guarantees the missing 3 types)

Because emosaru lacks `toggle_badged`, `blank`, and `lua`, add a tiny synthetic
pack `EmoTracker.Smoke/TestPacks/all_item_types/` (installed into
`UserDirectory.Path/packs` by the smoke orchestrator, and loaded via MCP
`load_pack`). It defines one minimal item per concrete type + a LuaItem with
scripted behavior:

- `toggle_badged` — name `Smoke Badge`, `base_item` code → a base toggle, `codes:"smoke_badge"`.
- `blank` — name `Smoke Blank` (assert no-op clicks, no codes).
- `lua` — created in `init.lua` via the Lua bindings; `OnLeftClickFunc` sets a Lua global `smoke_lua_clicks += 1` and toggles `smoke_lua_on`; `ProvidesCodeFunc` returns 1 when `smoke_lua_on`.
- `loop` toggles, `swap-actions` progressive/consumable, and a **consuming** toggle linking to a consumable (to deterministically hit §3.2/§3.4 without depending on pack definitions).

Runner drives `LuaItem` behavior via `execute_lua` (`Tracker:FindObjectForCode("smoke_lua")...` or a provided `smoke_lua` helper) and asserts via `get_lua_global("smoke_lua_clicks")`/`smoke_lua_on`, plus `find_item_by_code`/`check_code` for code provisioning.

Keeping these in a **separate pack** (not the canonical one) means emosaru remains the real-world reference and we don't pollute it with test artifacts.

---

## 5. Zero-type-coverage gaps & how they're closed

| Type | Source | How tested |
|------|--------|-----------|
| `toggle_badged` | test pack §4 | `get_item_details` `active`; click delegates to base; right-click toggles badge |
| `blank` | test pack §4 | clicks no-op; no codes; `capturable=false` |
| `lua` | test pack §4 | `execute_lua` drives; `get_lua_global` + `check_code` assert |

---

## 6. Proposed runner structure (`EmoTracker.Smoke`)

```
Program.cs                     # arg parsing (--core-only, --items-full, --variant)
Tiers/
  CorePackTier.cs              # load emosaru + variant; §3 matrix (7 types + locations)
  FullTypeTier.cs              # load all_item_types pack; §4 matrix (10 types)
  AccessibilityTier.cs         # §3.10 maps/accessibility/pinning
  PersistenceTier.cs           # per-type save/load round-trip + undo/redo
Assert.cs                      # Json/type-coded assertion helpers (JsonBool, stage, count…)
```

- New flags: `--core-only | --items-full`, `--variant standard|keysanity|...`.
- Keep `--at-only` for the autotracker tier unchanged.
- Exit non-zero on any failure; `smoke.sh` already surfaces logs per backend.

### Assertion helpers to add
`AssertItemActive`, `AssertStage`, `AssertCount`, `JsonProp(name)`, `WaitForItemState`
(reuse `autotracker_wait_item`-style polling but non-autotracker for UI catch-up).

---

## 7. Phased implementation

**Phase A — MCP surface (enables observability)**
§2.1 serializer fields + §2.2 `set_item_state` for the extra 5 mutating types +
§2.3 small tools (`get_item_codes`, `advance_item_to_code`, `get_section`, `get_maps`).
Update `docs/mcp-server.md`.

**Phase B — Core matrix (emosaru 7 types + locations/accessibility)**
Implement `CorePackTier` + `AccessibilityTier` + `PersistenceTier`. Validate
against SNI-fxpakpro mock backend (fastest, already green). Add variant run.

**Phase C — Full type matrix (synthetic pack)**
Author `all_item_types` pack + `FullTypeTier`; wire pack install into `smoke.sh`.

**Phase D — Cross-type regressions**
Undo/redo and save/load fidelity per type; consumer/proxy edge cases; `swap-actions`.

**Phase E — Integration**
Combine tiers; run the whole suite across all three backends; add optional
`--items-full` CI job; document in `docs/mcp-server.md` + `scripts/smoke/README`.

---

## 8. Risks & mitigations

| Risk | Mitigation |
|------|-----------|
| Pack lacks an item of a type we need for an edge case (loop toggle, consuming toggle) | synthetic test pack supplies deterministic fixtures |
| `LuaItem` not constructible from JSON (`[DisallowCreationFromTag]`) | create via Lua in test pack `init.lua`; assert through `get_lua_global` |
| Composite/sectionchests have no own observable state | assert via the referenced sibling items / proxied `Section` |
| Accessibility expectations tied to a specific rando seed/logic | use unconditional/known gates + `ignoreAllLogic` setting toggle to restore determinism |
| Serializer changes could break existing smoke | keep new fields additive; existing 22-assertion suite must stay green |
| Variant/keysanity changes wiring between runs | run a bounded variant matrix, not all; pin pack version |

---

## 9. Out of scope
- Adding new runtime item types (this plan only *tests* existing ones).
- UI/visual interaction coverage beyond `capture_main_window`/`list_ui_elements`.
- Extensions (NDI/Twitch/Voice/Lua-debugger) and the autotracker tier (already covered).

---

## 10. Implementation status (2026-08)

**Implemented and verified.** All phases below are complete and green.

### What was built

| Piece | Location | Status |
|-------|----------|--------|
| Per-type item state in MCP serializers (`ProgressiveToggleItem`, `ToggleBadgedItem`, `SectionChestsProxyItem`, etc.) | `EmoTracker/Extensions/McpServer/Tools/PackDataTools.cs` (central `AddItemTypeState`) | ✅ all 10 types observable |
| Extended `set_item_state` (badged/prog-toggle/consumable-consumed) + `get_item_codes` + `advance_item_to_code` tools | `PackDataTools.cs` | ✅ |
| `get_maps` tool + `load_pack`/`list_packs` support for locally-installed (non-repo) packs | `LocationTools.cs`, `ApplicationControlTools.cs` | ✅ |
| Synthetic **all-item-types** pack (`smoke_all_item_types`): 10 concrete types + loop + consuming-toggle + accessibility-gated locations + LuaItem | `EmoTracker.Smoke/TestPacks/smoke_all_item_types/` | ✅ |
| Full item-type / accessibility / Lua tier (51 assertions) | `EmoTracker.Smoke/Tiers/FullItemTypeTier.cs` (`--tier items`) | ✅ |
| Orchestrator `items` mode (installs pack, boots app, runs tier) | `scripts/smoke/smoke.sh` | ✅ |
| CI job for the items tier | `.github/workflows/smoke.yml` | ✅ |

### Coverage of all 10 concrete item types
Verified in the synthetic pack: `ToggleItem` (non-loop + loop + consuming), `ConsumableItem`,
`ProgressiveItem` (incl. disabled stage + `AdvanceToCode`), `ProgressiveToggleItem`,
`ToggleBadgedItem`, `CompositeToggleItem`, `SectionChestsProxyItem`, `StaticItem`,
`BlankItem`, and `LuaItem` (created via Lua; left/right callbacks + global state + code
provisioning). Plus accessibility gating (multi-code AND, key counts, always-inspect) and
a save/load round-trip.

### Layout element coverage (all 17 concrete types)
The `smoke_all_item_types` pack's `layouts/smoke_layouts.json` builds `tracker_all_types`,
which nests **every** layout element type: `ArrayPanel`, `ButtonPopup`, `CanvasPanel`,
`Container`, `DockPanel`, `GroupBox`, `Image`, `Item`, `ItemGrid`, `LastClearedLocation`,
`LayoutReference`, `MapPanel`, `RecentPinnedLocations`, `ScrollPanel`, `TabPanel`,
`TextBlock`, `ViewBox`. A `get_layout_tree` MCP tool walks the parsed tree and asserts each
type loads and is structurally valid, and `capture_main_window` confirms it renders to a
PNG. `save_main_window_screenshot` writes the render to disk.

### Vision verification (optional, local Ollama)
`scripts/smoke/verify_vision.sh` sends the rendered screenshot to a local Ollama vision
model (`qwen2.5vl:32b`, with `qwen2.5vl:7b` fallback), downscaling to ≤700px so the image
fits GPU memory. It confirms the model produced a substantive description of the tracker
UI (item icons, panels, tabs, map) as an extra gate that the layout rendered meaningfully.
This runs in `smoke.sh items` mode whenever Ollama is reachable; it is informational
(non-fatal) if fragments vary run-to-run.

### Results
- `--tier items`: **51/51 PASS** (item-type / accessibility / Lua tier)
- `--tier items` with layout + vision: **70/70 PASS** + vision render confirmation
- existing backend mode (`sni-fxpakpro`): **22/22 PASS** (no regression)

### SMZ3 autotracking stress + seed replay
The `EmoTracker.MockRandomizer` additionally simulates the **SMZ3** (Super Metroid +
LttP combo Randomizer) memory space: an **ExHiROM** cartridge profile with both games'
live WRAM plus cross-game ExHiROM SRAM (`0xA06000`+ LTTP mirror, `0xA17900`/`0xA17B00`
SM/LTTP item mirrors, `0xA173FE` which-game byte). Global + per-reset cartridge-profile
switching is exposed via the control API (`/profile`, `/game`).

Two long-running SMZ3 tiers (real `smalttprando_gilgatex_emotracker3` pack):
- `--tier smz3` — randomized game-switch/location/item/boss churn with stuck detection.
- `--tier replay` — plays through an actual **seed spoiler log**, walking its playthrough
  in sphere order, travelling between worlds, clearing each pickup's location and
  granting the item (live WRAM + cross-game mirror) with realistic pickup delays.
  `EMOTRACKER_REPLAY_FAST=1` shortens delays for CI.

```bash
bash scripts/smoke/smoke.sh smz3 sni-fxpakpro 3 40        # randomized SMZ3 churn
SMZ3_SPOILER=~/Downloads/...Spoiler.txt \
SMZ3_PACK_ZIP=.../smalttprando_gilgatex_emotracker3.zip \
  EMOTRACKER_REPLAY_FAST=1 bash scripts/smoke/smoke.sh replay sni-fxpakpro 1
```

### Run
```bash
bash scripts/smoke/smoke.sh items 3          # full item-type / accessibility / Lua tier
bash scripts/smoke/smoke.sh sni-fxpakpro 3   # core + autotracker tier
bash scripts/smoke/smoke.sh sni-emulator 3
bash scripts/smoke/smoke.sh nwa 3
```
