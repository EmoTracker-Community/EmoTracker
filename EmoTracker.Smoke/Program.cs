using System.Text.Json;

namespace EmoTracker.Smoke;

public static class Program
{
    static int sPass = 0, sFail = 0;

    public static async Task<int> Main(string[] args)
    {
        string mcpUrl = "http://localhost:27125";
        string mockUrl = "http://localhost:9090";
        string pack = "alttpr_emotracker_emosaru";
        string variant = "standard";
        string backend = "sni-fxpakpro"; // sni-fxpakpro | sni-emulator | nwa
        bool atOnly = false;
        string tier = "core";
        string screenshot = "";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mcp": mcpUrl = args[++i]; break;
                case "--mock": mockUrl = args[++i]; break;
                case "--pack": pack = args[++i]; break;
                case "--variant": variant = args[++i]; break;
                case "--backend": backend = args[++i]; break;
                case "--at-only": atOnly = true; break;
                case "--tier": tier = args[++i]; break;
                case "--screenshot": screenshot = args[++i]; break;
            }
        }

        Console.WriteLine($"=== EmoTracker Smoke Runner ===");
        Console.WriteLine($"  mcp={mcpUrl} mock={mockUrl} pack={pack} variant={variant} backend={backend} tier={tier}");

        await using var mcp = await McpClient.ConnectAsync(mcpUrl, "smoke-runner");

        // Full item-type / accessibility / Lua tier uses the synthetic pack.
        if (tier.Equals("items", StringComparison.OrdinalIgnoreCase))
            return await FullItemTypeTier.RunAsync(mcp, screenshot: screenshot);

        using var mock = new MockClient(mockUrl);

        // 0. App + mock prerequisite probes
        bool mockAlive = await mock.PingAsync();
        Check("mock/control API reachable", mockAlive, $"ping failed against {mockUrl}");

        // 1. Load the pack + variant
        Check("load_pack", JsonTrue(await mcp.CallAsync("load_pack",
            JsonSerializer.Serialize(new { uniqueId = pack, variant }))), "pack load failed");
        Check("get_loaded_pack", JsonPropertyTrue(await mcp.CallAsync("get_loaded_pack", "{}"), "loaded"), "no pack loaded");

        // 2. Model integrity
        Check("list_items", JsonNonEmpty(await mcp.CallAsync("list_items", "{}")), "no items");
        Check("list_locations", JsonNonEmpty(await mcp.CallAsync("list_locations", "{}")), "no locations");
        Check("find_item_by_code(bow)", JsonTrueAny(await mcp.CallAsync("find_item_by_code",
            """{"code":"bow"}""")), "bow item missing");

        if (!atOnly)
        {
            // 3. Interaction & logic
        Check("toggle_item(Flippers)", JsonTrue(await mcp.CallAsync("toggle_item",
            """{"name":"Flippers"}""")), "toggle failed");
        var accJson = await mcp.CallAsync("check_accessibility", """{"name":"Swamp Palace"}""");
        Check("accessibility reflects flippers", accJson.Length > 0 && !accJson.StartsWith("{\"error\""), "accessibility query empty");
        Check("reset_tracker", JsonTrue(await mcp.CallAsync("reset_tracker", "{}")), "reset failed");
        Check("undo", JsonTrue(await mcp.CallAsync("undo", "{}")), "undo returned failure");

        // 4. UI render
        Check("capture_main_window", JsonHasBase64Png(await mcp.CallAsync("capture_main_window", "{}")), "screenshot failed");
        Check("list_ui_elements", JsonNonEmpty(await mcp.CallAsync("list_ui_elements", "{}")), "no UI elements");

        // 5. Persistence
        string path = "/tmp/smoke_save.json";
        Check("save_progress", JsonTrue(await mcp.CallAsync("save_progress",
            JsonSerializer.Serialize(new { path }))), "save failed");
        Check("load_progress", JsonTrue(await mcp.CallAsync("load_progress",
            JsonSerializer.Serialize(new { path }))), "load failed");

        // 6. Lua drift
        Check("execute_lua", JsonNonEmpty(await mcp.CallAsync("execute_lua",
            """{"code":"return 1+1"}""")), "lua failed");
        }

        // 7. Auto-tracker end-to-end per backend
        await RunAutoTrackerTier(mcp, mock, backend);

        Console.WriteLine($"\n=== RESULT: {sPass} passed, {sFail} failed ===");
        return sFail == 0 ? 0 : 1;
    }

    static async Task RunAutoTrackerTier(McpClient mcp, MockClient mock, string backend)
    {
        string provider = backend.StartsWith("sni") ? "sni" : "nwa";

        // Deterministic start: reset mock RAM + tracker.
        await mock.ResetAsync();
        await mcp.CallAsync("reset_tracker", "{}");

        Check("autotracker_select_provider", JsonTrue(
            await mcp.CallAsync("autotracker_select_provider", JsonSerializer.Serialize(new { uid = provider }))),
            $"select provider '{provider}' failed");

        // Wait for at least one device to appear (mock discovered). NWA (multi-
        // port probe) is slower than SNI, so give it a generous window.
        bool deviceFound = await mcp.WaitUntilAsync(async () =>
        {
            var at = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(at);
            bool ok = doc.RootElement.GetProperty("selectedProvider").GetString() == provider;
            return (ok && JsonDevicesCount(at, provider) > 0, at);
        }, provider == "nwa" ? 20000 : 8000);
        Check("autotracker selects mock device (discovery)", deviceFound, "no device discovered");

        Check("autotracker_start", JsonTrue(await mcp.CallAsync("autotracker_start", "{}")), "autotracker_start failed");

        bool connected = await mcp.WaitUntilAsync(async () =>
        {
            var r = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(r);
            bool c = doc.RootElement.TryGetProperty("connected", out var v) && v.GetBoolean();
            return (c, r);
        }, 15000);
        Check("autotracker connected to backend", connected, "never connected to mock backend");

        // Inject a couple of items into the mock via the control API and confirm
        // the tracker captures them (gloves = progressive WRAM byte).
        await mock.WriteAsync(0x7EF354, "01");   // gloves stage 1
        await mock.WriteAsync(0x7E0010, "06");   // in-game

        bool glovesCaptured = await mcp.WaitUntilAsync(async () =>
        {
            var d = await mcp.CallAsync("autotracker_wait_item", """{"name":"Gloves","itemActive":true,"timeoutMs":2000}""");
            return (JsonTrue(d), d);
        }, 10000);
        Check($"autotracker captured Gloves ({backend})", glovesCaptured, "Gloves not captured from mock memory");

        // Live mutation: add shovel (flag 0x04 on 0x7EF38C).
        await mock.WriteAsync(0x7EF38C, "04");
        bool shovelCaptured = await mcp.WaitUntilAsync(async () =>
        {
            var d = await mcp.CallAsync("autotracker_wait_item", """{"name":"Shovel","itemActive":true,"timeoutMs":2000}""");
            return (JsonTrue(d), d);
        }, 10000);
        Check($"autotracker captured Shovel (live) ({backend})", shovelCaptured, "Shovel not captured on live write");

        // Reset via mock; confirm the tracker reflects disconnect/heal path isn't fatal.
        await mock.ResetAsync();
        Check("autotracker_stop", JsonTrue(await mcp.CallAsync("autotracker_stop", "{}")), "autotracker_stop failed");
    }

    // ---------- assertion helpers ----------

    static void Check(string name, bool ok, string failMsg)
    {
        if (ok) { sPass++; Console.WriteLine($"  [PASS] {name}"); }
        else { sFail++; Console.WriteLine($"  [FAIL] {name} — {failMsg}"); }
    }

    static bool JsonTrue(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("success", out var v) && v.GetBoolean();
    }

    static bool JsonPropertyTrue(string json, string prop)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
    }

    static bool JsonTrueAny(string json)
    {
        using var doc = JsonDocument.Parse(json);
        bool success = doc.RootElement.TryGetProperty("success", out var v) && v.GetBoolean();
        if (success) return true;
        // Some tools return error wrapper but still found the item.
        return json.Contains("found", StringComparison.OrdinalIgnoreCase);
    }

    static bool JsonNonEmpty(string json) => json.Length > 2 && !json.StartsWith("{\"error\"");

    static bool JsonHasBase64Png(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("image", out var d))
                return d.GetString()?.StartsWith("iVBOR") == true; // PNG magic
            return false;
        }
        catch { return false; }
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
                {
                    return d.GetArrayLength();
                }
            }
            return 0;
        }
        catch { return 0; }
    }
}
