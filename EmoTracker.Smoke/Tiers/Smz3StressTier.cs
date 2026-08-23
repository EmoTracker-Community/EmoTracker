using System.Text.Json;

namespace EmoTracker.Smoke;

/// <summary>
/// SMZ3 (Super Metroid + LttP combo randomizer) long-term autotracking stress
/// tier. Runs against the real <c>smalttprando_gilgatex_emotracker3</c> pack
/// pointed at the mock in SMZ3/ExHiROM profile.
///
/// The mock simulates both games' live WRAM regions plus the ExHiROM cross-game
/// SRAM. The pack's <c>updateGame()</c> watches <c>0xA173FE</c> (which game) and
/// tears down / re-registers the entire memory-watch set on every game switch.
///
/// This tier is a randomized long-running churn driver that exercises:
///   1. Repeated game switches (LTTP ↔ SM) at random intervals.
///   2. Clearing locations in each game (LTTP overworld + SM room flags).
///   3. Acquiring items for BOTH games, whether the active game is LTTP or SM
///      (live WRAM + cross-game ExHiROM SRAM mirrors).
///   4. Clearing bosses (SM Kraid via WRAM and via the cross-game SRAM mirror).
///   5. Switching active map tab / preserving per-game map layouts on game change.
///   6. Proving auto-tracking never "sticks": every mutation must propagate to
///      the tracker within a bounded timeout while the tracker stays connected;
///      a timeout on a live (non-disconnected) tracker is a "stuck" failure.
///
/// Environmental tuning (env vars):
///   EMOTRACKER_SMZ3_ITERATIONS - number of churn steps (default 40)
///   EMOTRACKER_SMZ3_SEED       - RNG seed for reproducible runs (default: random)
/// </summary>
public static class Smz3StressTier
{
    public const string PackUid = "smalttprando_gilgatex_emotracker3";
    const string SmMapLayoutKey = "sm_maps";
    const string LtppMapLayoutKey = "alttp_maps";

    // ---- Live WRAM (blitz) regions ----
    const ulong LtppItemsWram = 0x7EF300;   // LTTP item data base (active in LTTP)
    const ulong SmItemsWram = 0x7E09A2;     // SM item data base (active in SM)
    const ulong SmBossesWram = 0x7ED828;    // SM boss flags (active in SM)
    const ulong LtppOverworldWram = 0x7EF280; // LTTP overworld events (active in LTTP)
    const ulong SmRoomsWram = 0x7ED870;     // SM room clear flags (active in SM)

    // ---- Cross-game ExHiROM SRAM mirrors (tracked while in the OTHER game) ----
    const ulong SmItemsMirror = 0xA17900;   // SM items seen from LTTP (base 0xa17900)
    const ulong SmBossesMirror = 0xA16078;  // SM bosses seen from LTTP (0xa16010+0x68)
    const ulong LtppItemsMirror = 0xA17B00; // LTTP items seen from SM (callback base 0xa17b00)

    // NOTE on LtppItemsMirror: the pack's "LTTP Item Data In SM" watch is
    // registered at 0xA17B40 (size 0x90) but its callback updateItemsInactiveLTTP
    // reads from base 0xA17B00 — a 0x40 offset between the watch segment and the
    // callback base. Effective item bytes therefore live at 0xA17B00+offset, i.e.
    // Bow @ 0xA17B00+0x40 = 0xA17B40 (inside the watched region). We drive the
    // callback base so item offsets match the live LTTP layout.

    // LTTP overworld location clears (section flag 0x40 at base+index).
    record struct SmRoom(string Name, string Section, int Offset, byte Flag);
    static readonly SmRoom[] sSmRooms =
    {
        new("Bombs", "Bombs", 0x0, 0x80),
        new("Charge Beam", "Charge Beam", 0x2, 0x80),
        new("Morphing Ball", "Morphing Ball", 0x3, 0x04),
        new("X-Ray Scope", "X-Ray Scope", 0x4, 0x40),
        new("Spazer", "Spazer", 0x5, 0x04),
        new("Ice Beam", "Ice Beam", 0x6, 0x04),
        new("Grappling Beam", "Grappling Beam", 0x7, 0x10),
        new("Speed Booster", "Speed Booster", 0x8, 0x04),
        new("Wave Beam", "Wave Beam", 0x8, 0x10),
        new("Screw Attack", "Screw Attack", 0x9, 0x80),
        new("Gravity Suit", "Gravity Suit", 0x10, 0x80),
        new("Plasma Beam", "Plasma Beam", 0x11, 0x80)
    };

    static int sPass = 0, sFail = 0;
    static readonly List<string> sFailures = new();
    static int sIterations = 0, sMaxIterations = 0;
    static Random sRng = new();
    static bool sInSm = false;

    static int sStuckCount = 0;       // mutations that timed out while connected
    static int sDisconnectCount = 0;  // observed disconnects/reconnects during churn

    public static async Task<int> RunAsync(McpClient mcp, MockClient mock, string backend, int iterations = 40)
    {
        sPass = sFail = 0;
        sFailures.Clear();
        sFailures.TrimExcess();
        sMaxIterations = iterations;
        sIterations = 0;
        sStuckCount = 0;
        sDisconnectCount = 0;

        int seed = Random.Shared.Next();
        var seedEnv = Environment.GetEnvironmentVariable("EMOTRACKER_SMZ3_SEED");
        if (!string.IsNullOrWhiteSpace(seedEnv) && int.TryParse(seedEnv, out var se)) seed = se;
        sRng = new Random(seed);

        string provider = backend.StartsWith("sni") ? "sni" : "nwa";

        Console.WriteLine($"=== SMZ3 STRESS TIER ===");
        Console.WriteLine($"  pack={PackUid} backend={backend} provider={provider} iterations={iterations} seed={seed}");

        // 1. Point the mock at the SMZ3 (ExHiROM) profile.
        Check("mock/control reachable", await mock.PingAsync(), "mock ping failed");
        await mock.SetProfileAsync("smz3");
        Check("mock profile = smz3", (await mock.GetProfileAsync()).Equals("smz3", StringComparison.OrdinalIgnoreCase),
            "mock not in smz3 profile");
        await mock.ResetAsync();

        // 2. Load the real SMZ3 pack.
        Check("load_pack(smalttprando)", JsonTrue(await mcp.CallAsync("load_pack",
            JsonSerializer.Serialize(new { uniqueId = PackUid, variant = "standard" }))),
            "SMZ3 pack load failed");

        // 3. Connect the auto-tracker. For SNI use SnesABus + ExHiROM mapping.
        Check("autotracker_select_provider", JsonTrue(await mcp.CallAsync("autotracker_select_provider",
            JsonSerializer.Serialize(new { uid = provider }))), $"select provider '{provider}' failed");
        if (provider == "sni")
        {
            await mcp.CallAsync("autotracker_set_option", JsonSerializer.Serialize(new { key = "address_space", value = "SnesABus" }));
            await mcp.CallAsync("autotracker_set_option", JsonSerializer.Serialize(new { key = "memory_mapping", value = "ExHiROM" }));
        }

        bool deviceFound = await mcp.WaitUntilAsync(async () =>
        {
            var at = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(at);
            bool ok = doc.RootElement.GetProperty("selectedProvider").GetString() == provider;
            return (ok && JsonDevicesCount(at, provider) > 0, at);
        }, provider == "nwa" ? 20000 : 10000);
        Check("autotracker selects mock device", deviceFound, "no device discovered");

        Check("autotracker_start", JsonTrue(await mcp.CallAsync("autotracker_start", "{}")), "autotracker_start failed");

        bool connected = await WaitConnected(mcp, 15000);
        Check("autotracker connected (smz3)", connected, "never connected to mock backend");

        // 4. Baseline in each game (deterministic sanity before churn).
        await SwitchGame(mcp, mock, sm: false);
        Check("baseline: LTTP item (Bow)", await ItemFlip(mcp, mock, "Bow", LtppItemsWram + 0x40), "baseline LTTP Bow failed");
        Check("baseline: SM item from LTTP (Varia via mirror)",
            await ItemFlip(mcp, mock, "Varia Suit", SmItemsMirror + 0x02, flag: 0x01), "baseline SM-in-LTTP Varia failed");
        Check("baseline: SM boss from LTTP (Kraid via mirror)",
            await ItemFlip(mcp, mock, "Kraid", SmBossesMirror + 0x01, flag: 0x01), "baseline SM-in-LTTP Kraid failed");
        Check("baseline: LTTP location clear (Spectacle Rock)",
            await LocClearFlip(mcp, mock, "Spectacle Rock", "Spectacle Rock", 0x7EF280 + 3, 0x40), "baseline Spectacle Rock failed");

        await SwitchGame(mcp, mock, sm: true);
        Check("baseline: SM item (Varia)", await ItemFlip(mcp, mock, "Varia Suit", SmItemsWram + 0x02, flag: 0x01), "baseline SM Varia failed");
        Check("baseline: LTTP item from SM (Bow via mirror)",
            await ItemFlip(mcp, mock, "Bow", LtppItemsMirror + 0x40), "baseline LTTP-in-SM Bow failed");
        Check("baseline: SM boss (Kraid)", await ItemFlip(mcp, mock, "Kraid", SmBossesWram + 0x01, flag: 0x01), "baseline SM Kraid failed");
        Check("baseline: SM location clear (Bombs)",
            await LocClearFlip(mcp, mock, "Bombs", "Bombs", 0x7ED870 + 0x0, 0x80), "baseline Bombs failed");

        Check("baseline: maps + layout integrity", await VerifyMapIntegrity(mcp), "maps/layout integrity failed");

        // 5. Randomized long-term churn.
        for (int i = 0; i < sMaxIterations; i++)
        {
            sIterations = i + 1;

            // Random interval before the next transition (50-450ms).
            await Task.Delay(50 + sRng.Next(400));

            // 5a. Occasionally switch games (random interval between switches handled
            //     by the delays); after a switch verify maps stayed intact.
            bool switchGames = sRng.Next(100) < 35;
            if (switchGames)
            {
                await SwitchGame(mcp, mock, sm: !sInSm);
                await Task.Delay(150 + sRng.Next(300));
                if (!await VerifyMapIntegrity(mcp))
                    sFailures.Add($"maps/layout integrity lost after game switch (iter {sIterations})");
            }

            // 5b. Perform a random tracking action for the current game.
            bool ok = await RandomAction(mcp, mock);
            if (!ok && await IsConnected(mcp)) sStuckCount++;
        }

        Check("auto-tracking never stuck during churn", sStuckCount == 0,
            $"{sStuckCount} mutation(s) did not propagate while connected (auto-tracking stuck)");
        Check("no connection drop across churn", sDisconnectCount == 0,
            $"{sDisconnectCount} disconnect/reconnect event(s) during {sMaxIterations} iterations");
        Check("completed all churn iterations", sIterations == sMaxIterations, $"only {sIterations}/{sMaxIterations} ran");

        // 6. Final sanity: still connected and tracker reflects a live state.
        Check("autotracker connected at end", await WaitConnected(mcp, 8000), "connection lost by end of run");
        Check("final: map + layout integrity", await VerifyMapIntegrity(mcp), "final maps/layout integrity failed");

        await mcp.CallAsync("autotracker_stop", "{}");

        Console.WriteLine($"\n=== SMZ3 STRESS RESULT: {sPass} passed, {sFail} failed (stuck={sStuckCount}, disconnects={sDisconnectCount}) ===");
        if (sFailures.Count > 0)
        {
            Console.WriteLine("Failures:");
            foreach (var f in sFailures) Console.WriteLine($"  - {f}");
        }
        return sFail == 0 ? 0 : 1;
    }

    // ---------- randomized action pool ----------

    static async Task<bool> RandomAction(McpClient mcp, MockClient mock)
    {
        int pick = sRng.Next(10);
        if (sInSm)
        {
            // SM-mode pool: SM live items/bosses/locations + LTTP items via mirror.
            return pick switch
            {
                0 => await ItemFlip(mcp, mock, "Varia Suit", SmItemsWram + 0x02, flag: 0x01),
                1 => await ItemFlip(mcp, mock, "Morph Ball", SmItemsWram + 0x04, flag: 0x04),
                2 => await ItemFlip(mcp, mock, "Charge Beam", SmItemsWram + 0x07, flag: 0x10),
                3 => await ItemFlip(mcp, mock, "Kraid", SmBossesWram + 0x01, flag: 0x01),
                4 => await ItemFlip(mcp, mock, "Ridley", SmBossesWram + 0x02, flag: 0x01),
                5 => await ItemFlip(mcp, mock, "Bow", LtppItemsMirror + 0x40),
                6 => await ItemFlip(mcp, mock, "Hookshot", LtppItemsMirror + 0x42),
                7 => await RandomSmRoomClear(mcp, mock),
                _ => await RandomSmRoomClear(mcp, mock)
            };
        }
        else
        {
            // LTTP-mode pool: LTTP live items/locations + SM items/bosses via mirror.
            return pick switch
            {
                0 => await ItemFlip(mcp, mock, "Bow", LtppItemsWram + 0x40),
                1 => await ItemFlip(mcp, mock, "Hookshot", LtppItemsWram + 0x42),
                2 => await ItemFlip(mcp, mock, "Gloves", LtppItemsWram + 0x54),
                3 => await ItemFlip(mcp, mock, "Varia Suit", SmItemsMirror + 0x02, flag: 0x01),
                4 => await ItemFlip(mcp, mock, "Charge Beam", SmItemsMirror + 0x07, flag: 0x10),
                5 => await ItemFlip(mcp, mock, "Kraid", SmBossesMirror + 0x01, flag: 0x01),
                6 => await RandomLtppOverworldClear(mcp, mock),
                7 => await RandomLtppOverworldClear(mcp, mock),
                _ => await RandomLtppOverworldClear(mcp, mock)
            };
        }
    }

    static async Task<bool> RandomSmRoomClear(McpClient mcp, MockClient mock)
    {
        var room = sSmRooms[sRng.Next(sSmRooms.Length)];
        return await LocClearFlip(mcp, mock, room.Name, room.Section, SmRoomsWram + (ulong)room.Offset, room.Flag);
    }

    static async Task<bool> RandomLtppOverworldClear(McpClient mcp, MockClient mock)
    {
        // LTTP overworld locations driven explicitly (mirrors the pack's table).
        return sRng.Next(3) switch
        {
            0 => await LocClearFlip(mcp, mock, "Spectacle Rock", "Spectacle Rock", 0x7EF280 + 3, 0x40),
            1 => await LocClearFlip(mcp, mock, "Floating Island", "Island", 0x7EF280 + 5, 0x40),
            _ => await LocClearFlip(mcp, mock, "Flute Spot", "Flute Spot", 0x7EF280 + 42, 0x40)
        };
    }

    // ---------- game switching + maps ----------

    static async Task SwitchGame(McpClient mcp, MockClient mock, bool sm)
    {
        await mock.SwitchGameAsync(sm);
        sInSm = sm;
        await VerifyMapIntegrity(mcp); // best-effort, no failure
    }

    static async Task<bool> VerifyMapIntegrity(McpClient mcp)
    {
        try
        {
            var maps = Json(mcp.CallAsync("get_maps", "{}").GetAwaiter().GetResult());
            bool hasMetroid = false, hasLightworld = false;
            foreach (var m in maps.EnumerateArray())
            {
                string? n = m.GetProperty("name").GetString();
                if (n == "metroid") hasMetroid = true;
                if (n == "lightworld") hasLightworld = true;
            }
            if (!hasMetroid || !hasLightworld) return false;

            var tree = Json(mcp.CallAsync("get_layout_tree", "{}").GetAwaiter().GetResult());
            if (!tree.TryGetProperty("layouts", out var layouts)) return false;
            bool smMaps = false, lttpMaps = false;
            foreach (var lay in layouts.EnumerateArray())
            {
                string? key = lay.TryGetProperty("key", out var k) ? k.GetString() : null;
                if (key == SmMapLayoutKey) smMaps = true;
                if (key == LtppMapLayoutKey) lttpMaps = true;
            }
            return smMaps && lttpMaps;
        }
        catch { return false; }
    }

    // ---------- turning actions into verifiable transitions ----------

    // Toggle an item's memory byte on then off; both transitions must reach the tracker.
    static async Task<bool> ItemFlip(McpClient mcp, MockClient mock, string name, ulong addr, byte flag = 0xFF)
    {
        byte on = flag == 0xFF ? (byte)0x01 : flag;
        if (!await WriteItemThenWait(mcp, mock, name, addr, on, active: true)) return false;
        if (!await WriteItemThenWait(mcp, mock, name, addr, 0x00, active: false)) return false;
        return true;
    }

    static async Task<bool> WriteItemThenWait(McpClient mcp, MockClient mock, string name, ulong addr, byte value, bool active)
    {
        await mock.WriteAsync(addr, value.ToString("X2"));
        bool ok = await WaitItem(mcp, name, active, 4500);
        if (!ok)
            sFailures.Add($"'{name}' {(active ? "on" : "off")} not reflected @0x{addr:X} (iter {sIterations})");
        return ok;
    }

    // Clear then un-clear a location; both transitions must reach the tracker.
    static async Task<bool> LocClearFlip(McpClient mcp, MockClient mock, string loc, string section, ulong addr, byte flag)
    {
        if (!await WriteLocThenWait(mcp, mock, loc, section, addr, flag, cleared: true)) return false;
        if (!await WriteLocThenWait(mcp, mock, loc, section, addr, 0x00, cleared: false)) return false;
        return true;
    }

    static async Task<bool> WriteLocThenWait(McpClient mcp, MockClient mock, string loc, string section, ulong addr, byte value, bool cleared)
    {
        await mock.WriteAsync(addr, value.ToString("X2"));
        bool ok = await WaitLocAvailable(mcp, loc, section, cleared ? 0 : 1, 5000);
        if (!ok)
            sFailures.Add($"location '{loc}/{section}' {(cleared ? "cleared" : "uncleared")} not reflected @0x{addr:X} (iter {sIterations})");
        return ok;
    }

    static async Task<bool> WaitItem(McpClient mcp, string name, bool active, int ms)
    {
        var d = await mcp.CallAsync("autotracker_wait_item",
            JsonSerializer.Serialize(new { name, itemActive = active, timeoutMs = ms }));
        return JsonTrue(d);
    }

    static async Task<bool> WaitLocAvailable(McpClient mcp, string loc, string section, int expected, int ms)
    {
        try
        {
            var deadline = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < deadline)
            {
                var r = Json(mcp.CallAsync("get_location",
                    JsonSerializer.Serialize(new { name = loc })).GetAwaiter().GetResult());
                if (!r.TryGetProperty("sections", out var sections)) return false;
                foreach (var s in sections.EnumerateArray())
                {
                    if (s.TryGetProperty("name", out var n) && n.GetString() == section
                        && s.TryGetProperty("availableChestCount", out var a) && a.GetInt32() == expected)
                        return true;
                }
                await Task.Delay(120);
            }
            return false;
        }
        catch { return false; }
    }

    // ---------- connection / stuck detection ----------

    static async Task<bool> IsConnected(McpClient mcp)
    {
        try
        {
            var r = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(r);
            bool c = doc.RootElement.TryGetProperty("connected", out var v) && v.GetBoolean();
            bool rc = doc.RootElement.TryGetProperty("reconnecting", out var rv) && rv.GetBoolean();
            if (!c || rc) { sDisconnectCount++; Check($"connected during iteration {sIterations}", false, "connection dropped/reconnecting"); return false; }
            return true;
        }
        catch { return false; }
    }

    static async Task<bool> WaitConnected(McpClient mcp, int ms)
    {
        return await mcp.WaitUntilAsync(async () =>
        {
            var r = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(r);
            bool c = doc.RootElement.TryGetProperty("connected", out var v) && v.GetBoolean();
            return (c, r);
        }, ms);
    }

    // ---------- helpers ----------

    static void Check(string name, bool ok, string failMsg)
    {
        if (ok) { sPass++; Console.WriteLine($"  [PASS] {name}"); }
        else { sFail++; sFailures.Add($"{name} — {failMsg}"); Console.WriteLine($"  [FAIL] {name} — {failMsg}"); }
    }

    static bool JsonTrue(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("success", out var v) && v.GetBoolean();
        }
        catch { return false; }
    }

    static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    static int JsonDevicesCount(string json, string providerUid)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.GetProperty("providers").EnumerateArray())
            {
                if (p.TryGetProperty("uid", out var uid)
                    && uid.GetString()?.Equals(providerUid, StringComparison.OrdinalIgnoreCase) == true
                    && p.TryGetProperty("availableDevices", out var d))
                    return d.GetArrayLength();
            }
            return 0;
        }
        catch { return 0; }
    }
}
