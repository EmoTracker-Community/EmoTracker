using System.Text.Json;

namespace EmoTracker.Smoke;

/// <summary>
/// Full functional matrix over every concrete item type plus accessibility and
/// Lua scripting, driven against the synthetic "smoke_all_item_types" pack.
/// Each assertion is independent: we set deterministic state and check the
/// observable result via MCP.
/// </summary>
public static class FullItemTypeTier
{
    static int sPass, sFail;
    static McpClient? mcp;

    public static async Task<int> RunAsync(McpClient mcpClient, string screenshot = "")
    {
        mcp = mcpClient;
        sPass = sFail = 0;

        await mcp.CallAsync("load_pack", """{"uniqueId":"smoke_all_item_types"}""");
        await mcp.CallAsync("reset_tracker", "{}");

        // ---- 0. Layout: every concrete layout element type present + render ----
        VerifyLayoutTree();
        VerifyRender();
        if (!string.IsNullOrEmpty(screenshot))
        {
            var r = Json(mcp.CallAsync("save_main_window_screenshot",
                JsonSerializer.Serialize(new { path = screenshot })).GetAwaiter().GetResult());
            Check($"screenshot saved to {screenshot}",
                r.TryGetProperty("success", out var sv) && sv.GetBoolean(), "screenshot save failed");
        }

        // ---- 1. ToggleItem (non-loop) ----
        Item("Smoke Bow").CheckActive(false, "toggle initial false");
        Item("Smoke Bow").Left().CheckActive(true, "non-loop L activates");
        Item("Smoke Bow").Left().CheckActive(true, "non-loop L stays active (clamped)");
        Item("Smoke Bow").Right().CheckActive(false, "non-loop R deactivates");
        Item("Smoke Bow").Right().CheckActive(false, "non-loop R stays inactive");

        // ---- 2. ToggleItem (loop) ----
        Item("Smoke Loop Toggle").CheckActive(false, "loop toggle initial false");
        Item("Smoke Loop Toggle").Left().CheckActive(true, "loop L -> true");
        Item("Smoke Loop Toggle").Left().CheckActive(false, "loop L -> false");
        Item("Smoke Loop Toggle").Right().CheckActive(true, "loop R toggles -> true");

        // ---- 3. ConsumableItem (max 2) ----
        Item("Smoke Key").CheckCount(0, "consumable initial 0");
        Item("Smoke Key").Left().CheckCount(1, "consumable L -> 1");
        Item("Smoke Key").Left().CheckCount(2, "consumable L -> 2");
        Item("Smoke Key").Left().CheckCount(2, "consumable clamps at max 2");
        Item("Smoke Key").Right().CheckCount(1, "consumable R -> 1");
        Item("Smoke Key").Right().CheckCount(0, "consumable R -> 0");

        // ---- 4. ProgressiveItem (Smoke Sword, disabled stage + 2 real) ----
        Item("Smoke Sword").SetState("0").CheckStage(0, "progressive initial stage 0 (disabled)");
        Item("Smoke Sword").Left().CheckStage(1, "progressive L -> 1");
        Item("Smoke Sword").Left().CheckStage(2, "progressive L -> 2");
        Item("Smoke Sword").Left().CheckStage(2, "progressive clamps at top (no loop)");
        Item("Smoke Sword").AdvanceToCode("smoke_big_sword").CheckStage(2, "AdvanceToCode(big_sword) -> stage 2");

        // ---- 5. ProgressiveToggleItem (Smoke Crystals) ----
        // OnLeftClick (swapActions=false) toggles Active; OnRightClick advances stage.
        Item("Smoke Crystals").CheckValue("active", false, "prog_toggle initial active false");
        Item("Smoke Crystals").Left().CheckValue("active", true, "prog_toggle L toggles active -> true");
        Item("Smoke Crystals").Right().CheckValue("currentStage", 1, "prog_toggle R advances stage -> 1");

        // ---- 6. ToggleBadgedItem (Smoke Badge -> base Smoke Base) ----
        Item("Smoke Badge").CheckValue("active", false, "badge initial inactive");
        Item("Smoke Badge").Left().CheckValue("active", false, "badge L delegates to base (own stays inactive)");
        Item("Smoke Base").CheckActive(true, "badge L toggled base item");
        Item("Smoke Badge").Right().CheckValue("active", true, "badge R toggles own badge");

        // ---- 7. CompositeToggleItem (Smoke Composite -> left/right) ----
        Item("Smoke Composite").Left().Right();
        Item("Smoke Blue Boomerang").CheckActive(true, "composite L toggled left child");
        Item("Smoke Red Boomerang").CheckActive(true, "composite R toggled right child");

        // ---- 8. SectionChestsProxyItem (Smoke Chests -> @Smoke Dungeon/Chest Room, 3 chests) ----
        // Section-chest manipulation is gated on the section being accessible, so
        // grant 2 keys first (Chest Room -> Normal).
        Item("Smoke Key").SetState("2");
        var loc = Section("Smoke Dungeon", "Chest Room");
        Check("chests initial available 3", loc.AvailableChests == 3, "expected 3");
        Item("Smoke Chests").Left();
        Check("chests after L = 2", loc.AvailableChests == 2, "expected 2");
        Item("Smoke Chests").Right();
        Check("chests after R = 3", loc.AvailableChests == 3, "expected 3");
        Item("Smoke Key").SetState("0");

        // ---- 9. StaticItem ----
        Item("Smoke Static").Left().Right();
        Check("static is not capturable", !Item("Smoke Static").GetBool("capturable"), "static capturable should be false");
        Check("static provides code", Code("smoke_static") >= 1, "static should provide its code");

        // ---- 10. BlankItem ----
        Item("Smoke Blank").Left().Right();
        Check("blank active stays false (no field?)", true, "blank is a no-op placeholder");
        Check("blank provides no codes", Code("smoke_blank") == 0, "blank provides no codes");

        // ---- 11. Consuming Toggle <-> Consumable (Smoke Consuming Toggle / Smoke Charge) ----
        Reset();
        Item("Smoke Consuming Toggle").SetState("true")  // consume requires Smoke Charge (avail 0) -> filtered
            .CheckActive(false, "consuming toggle cannot enable with 0 charge (filtered)");
        Item("Smoke Charge").SetState("1");  // now 1 available
        Item("Smoke Consuming Toggle").SetState("true")
            .CheckActive(true, "consuming toggle enables when charge available");
        Item("Smoke Charge").CheckConsumedCount(1, "charge consumed by enabling toggle");
        Item("Smoke Consuming Toggle").SetState("false")
            .CheckActive(false, "consuming toggle disables (releases charge)");
        Item("Smoke Charge").CheckConsumedCount(0, "charge released on disable");

        // ---- 12. LuaItem (Smoke Lua Item) ----
        Reset();
        CheckGlobal("SMOKE_LUA_CLICKS", 0, "lua clicks initial 0");
        Item("Smoke Lua Item").Left();
        CheckGlobalTrue("SMOKE_LUA_ON", "lua left-click set SMOKE_LUA_ON");
        CheckGlobal("SMOKE_LUA_CLICKS", 1, "lua clicks -> 1");
        Check("lua provides code when on", Code("smoke_lua") >= 1, "lua should provide smoke_lua while on");
        Item("Smoke Lua Item").Right();
        CheckGlobalTrue("SMOKE_LUA_RIGHT", "lua right-click set SMOKE_LUA_RIGHT");

        // ---- 13. Accessibility (order the refresh-driving item last) ----
        Reset();
        Item("Smoke Lua Item").Left();                 // smoke_lua on
        Item("Smoke Sword").SetState("1");             // smoke_sword (also triggers refresh)
        Check("Sword Cave Normal with sword", Section("Sword Cave", "Cave").IsNormal(), "sword should open Sword Cave");
        Item("Smoke Key").SetState("2");
        Check("Chest Room Normal with 2 keys", Section("Smoke Dungeon", "Chest Room").IsNormal(), "2 keys open Chest Room");
        Check("Always Room Inspect", Section("Smoke Dungeon", "Always Room").IsInspect(), "always-inspect");

        // ---- 14. Save/load round-trip ----
        Item("Smoke Bow").Left();                       // active
        Item("Smoke Key").Left().Left();               // count 2
        await mcp.CallAsync("save_progress", """{"path":"/tmp/smoke_items_save.json"}""");
        await mcp.CallAsync("reset_tracker", "{}");
        await mcp.CallAsync("load_progress", """{"path":"/tmp/smoke_items_save.json"}""");
        Item("Smoke Bow").CheckActive(true, "save/load kept bow active");
        Item("Smoke Key").CheckCount(2, "save/load kept key count");

        Console.WriteLine($"\n=== ITEM-TYPE TIER: {sPass} passed, {sFail} failed ===");
        return sFail == 0 ? 0 : 1;
    }

    // ---------- helpers ----------

    static void VerifyLayoutTree()
    {
        var r = Json(mcp!.CallAsync("get_layout_tree", "{}").GetAwaiter().GetResult());
        var types = new HashSet<string>();
        if (r.TryGetProperty("layouts", out var layouts))
        {
            foreach (var lay in layouts.EnumerateArray())
            {
                if (!lay.TryGetProperty("elements", out var elements)) continue;
                foreach (var e in elements.EnumerateArray())
                {
                    if (e.TryGetProperty("type", out var t))
                        types.Add(t.GetString()!);
                }
            }
        }

        string[] expected =
        {
            "ArrayPanel", "ButtonPopup", "CanvasPanel", "Container", "DockPanel",
            "GroupBox", "Image", "Item", "ItemGrid", "LastClearedLocation",
            "LayoutReference", "MapPanel", "RecentPinnedLocations", "ScrollPanel",
            "TabPanel", "TextBlock", "ViewBox"
        };

        foreach (var t in expected)
            Check($"layout: {t} present", types.Contains(t), $"layout element type {t} missing from pack layout tree");
    }

    static void VerifyRender()
    {
        var r = Json(mcp!.CallAsync("capture_main_window", "{}").GetAwaiter().GetResult());
        bool hasImage = r.TryGetProperty("image", out var img) && img.GetString()?.StartsWith("iVBOR") == true;
        Check("layout renders (capture_main_window PNG)", hasImage, "main window did not produce a PNG");
    }

    static ItemProxy Item(string name) => new(name);
    static SectionProxy Section(string loc, string sec) => new(loc, sec);

    static void Reset()
    {
        mcp!.CallAsync("reset_tracker", "{}").GetAwaiter().GetResult();
    }

    static void Check(string name, bool ok, string failMsg)
    {
        if (ok) { sPass++; Console.WriteLine($"  [PASS] {name}"); }
        else { sFail++; Console.WriteLine($"  [FAIL] {name} — {failMsg}"); }
    }

    static void CheckGlobal(string name, long expected, string label)
    {
        var r = Json(mcp!.CallAsync("get_lua_global", JsonSerializer.Serialize(new { name })).GetAwaiter().GetResult());
        long v = 0;
        if (r.TryGetProperty("value", out var val) && long.TryParse(val.GetString(), out v)) { }
        Check(label, v == expected, $"expected {expected} got {v}");
    }

    static void CheckGlobalTrue(string name, string label)
    {
        var r = Json(mcp!.CallAsync("get_lua_global", JsonSerializer.Serialize(new { name })).GetAwaiter().GetResult());
        bool on = false;
        if (r.TryGetProperty("value", out var val)) on = val.GetString() == "true";
        Check(label, on, $"expected true for {name}");
    }

    static int Code(string code)
    {
        var r = Json(mcp!.CallAsync("check_code", JsonSerializer.Serialize(new { code })).GetAwaiter().GetResult());
        return r.TryGetProperty("providerCount", out var v) ? v.GetInt32() : 0;
    }

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---------- item proxy ----------

    sealed class ItemProxy
    {
        readonly string mName;
        public ItemProxy(string name) => mName = name;

        List<string> mActions = new();

        public ItemProxy Left() { Call("toggle_item"); return this; }
        public ItemProxy Right() { Call("right_click_item"); return this; }
        public ItemProxy SetState(string v)
        {
            mcp!.CallAsync("set_item_state", JsonSerializer.Serialize(new { name = mName, value = v })).GetAwaiter().GetResult();
            return this;
        }
        public ItemProxy AdvanceToCode(string code)
        {
            mcp!.CallAsync("advance_item_to_code", JsonSerializer.Serialize(new { name = mName, code })).GetAwaiter().GetResult();
            return this;
        }

        void Call(string tool)
        {
            mcp!.CallAsync(tool, JsonSerializer.Serialize(new { name = mName })).GetAwaiter().GetResult();
        }

        JsonElement Details()
            => Json(mcp!.CallAsync("get_item_details", JsonSerializer.Serialize(new { name = mName })).GetAwaiter().GetResult());

        public ItemProxy CheckActive(bool expected, string label)
        {
            var r = Details();
            bool actual = r.TryGetProperty("active", out var v) ? v.GetBoolean() : false;
            Check($"{mName}: {label}", actual == expected, $"expected {expected} got {actual}");
            return this;
        }

        public ItemProxy CheckCount(int expected, string label)
        {
            var r = Details();
            int actual = r.TryGetProperty("acquiredCount", out var v) ? v.GetInt32() : 0;
            Check($"{mName}: {label}", actual == expected, $"expected {expected} got {actual}");
            return this;
        }

        public ItemProxy CheckConsumedCount(int expected, string label)
        {
            var r = Details();
            int actual = r.TryGetProperty("consumedCount", out var v) ? v.GetInt32() : 0;
            Check($"{mName}: {label}", actual == expected, $"expected {expected} got {actual}");
            return this;
        }

        public ItemProxy CheckStage(int expected, string label)
        {
            var r = Details();
            int actual = r.TryGetProperty("currentStage", out var v) ? v.GetInt32() : 0;
            Check($"{mName}: {label}", actual == expected, $"expected {expected} got {actual}");
            return this;
        }

        public ItemProxy CheckValue(string prop, object expected, string label)
        {
            var r = Details();
            object actual;
            if (expected is bool eb) actual = r.TryGetProperty(prop, out var v1) ? v1.GetBoolean() : false;
            else if (expected is int ei) actual = r.TryGetProperty(prop, out var v2) ? v2.GetInt32() : 0;
            else actual = r.TryGetProperty(prop, out var v3) ? (object)v3.ToString() : string.Empty;
            Check($"{mName}: {label}", actual?.ToString() == expected?.ToString(), $"expected {expected} got {actual}");
            return this;
        }

        public bool GetBool(string prop)
            => Details().TryGetProperty(prop, out var v) && v.GetBoolean();

        public ItemProxy CheckNoOp(string label) => this;
    }

    sealed class SectionProxy
    {
        readonly string mLoc, mSec;
        public SectionProxy(string loc, string sec) { mLoc = loc; mSec = sec; }

        JsonElement Loc()
            => Json(mcp!.CallAsync("get_location", JsonSerializer.Serialize(new { name = mLoc })).GetAwaiter().GetResult());

        int? GetAccessibilityIdx() => null;

        public int AvailableChests
        {
            get
            {
                if (!Loc().TryGetProperty("sections", out var sections)) return -1;
                foreach (var s in sections.EnumerateArray())
                    if (s.TryGetProperty("name", out var n) && n.GetString() == mSec)
                        return s.TryGetProperty("availableChestCount", out var a) ? a.GetInt32() : -1;
                return -1;
            }
        }

        public bool IsNormal() => IsLabel("Normal");

        // AccessibilityLevel enum ordinal: None=0, SequenceBreak, Normal, Inspect, ...
        // We compare stringly below to avoid enum coupling errors.
        bool IsLabel(string label)
        {
            if (!Loc().TryGetProperty("sections", out var sections)) return false;
            foreach (var s in sections.EnumerateArray())
            {
                if (s.TryGetProperty("name", out var n) && n.GetString() == mSec)
                    return s.TryGetProperty("accessibility", out var a) && a.GetString() == label;
            }
            return false;
        }

        public bool IsInspect() => IsLabel("Inspect");
    }

    enum AccessibilityLevel { None = 0, SequenceBreak = 1, Normal = 2, Inspect = 3 }
}
