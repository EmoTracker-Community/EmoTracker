using System.Text.Json;

namespace EmoTracker.Smoke;

/// <summary>
/// Plays through an actual SMZ3 seed using its spoiler log, driving the mock's
/// memory so the real <c>smalttprando_gilgatex_emotracker3</c> pack autotracks
/// the whole run with realistic delays between pickups.
///
/// For each pickup in the playthrough (in sphere order) we:
///   1. Travel to the pickup's world (switch the mock game) if it changed.
///   2. Clear the location's tracker section in the pack's memory model.
///   3. Grant the item in both the live WRAM and cross-game SRAM mirror, so the
///      tracker reflects it whether it later views LTTP or SM.
///   4. Sleep a realistic pickup delay (default; short via EMOTRACKER_REPLAY_FAST).
///
/// Stuck detection: every verified item must become active on the tracker within
/// a timeout while the auto-tracker stays connected — a timeout on a live tracker
/// means tracking is "stuck". The connection is monitored throughout.
///
/// Memory model (from the pack's autotracking.lua):
///   LTTP items live: 0x7EF300+off,   mirror (seen in SM): 0xA17B00+off
///   SM   items live: 0x7E09A2+off,   mirror (seen in LTTP): 0xA17900+off
///   SM   ammo  live: 0x7E09C2+off,   mirror: 0xA17920+off
///   LTTP room data: 0x7EF000 (U16 room words, bit=chest)
///   LTTP overworld events: 0x7EF280+idx (flag 0x40)
///   LTTP NPC flags: 0x7EF410+off
///   SM rooms: 0x7ED870+off
/// </summary>
public static class SeedReplayTier
{
    public const string PackUid = "smalttprando_gilgatex_emotracker3";

    // ---- memory bases ----
    const ulong LtppItemsLive = 0x7EF300;
    const ulong LtppItemsMirror = 0xA17B00;
    const ulong SmItemsLive = 0x7E09A2;
    const ulong SmItemsMirror = 0xA17900;
    const ulong SmAmmoLive = 0x7E09C2;
    const ulong SmAmmoMirror = 0xA17920;
    const ulong LtppRooms = 0x7EF000;
    const ulong LtppOverworld = 0x7EF280;
    const ulong LtppNpc = 0x7EF410;
    const ulong SmRooms = 0x7ED870;

    enum Game { Lttp, Sm }

    sealed class ItemDef
    {
        public Game Game;
        public string VerifyName = "";   // empty = grant only (no tracker assertion)
        public ulong WramOff;       // offset within the game's item base
        public byte Flag;           // 0 = set byte to 1; else OR flag into byte
        public byte ByteValue;      // used when Flag==0
    }

    sealed class LocDef
    {
        public Game Game;
        public byte Kind; // 0=rooms, 1=overworld, 2=npc, 3=smroom, 4=dungeon(skip)
        public ulong Off;
        public byte Flag;
        public (int Room, int Bit)[] Slots = System.Array.Empty<(int, int)>();
        public string AssertName = ""; // get_location name (empty = don't assert)
        public string AssertSection = "";
    }

    static int sPass = 0, sFail = 0;
    static readonly List<string> sFailures = new();
    static int sStuck = 0, sDisconnects = 0;
    static int sPickups = 0, sTotalPickups = 0;
    static bool sFast;
    static Random sRng = new();

    static readonly HashSet<string> sGranted = new();

    // ---------------- item grant table (spoiler item -> pack item) ----------------
    static readonly Dictionary<string, ItemDef> sItems = new()
    {
        // LTTP toggle items (byte set to 1)
        { "Bow", new ItemDef { Game=Game.Lttp, VerifyName="Bow", WramOff=0x40 } },
        { "Hookshot", new ItemDef { Game=Game.Lttp, VerifyName="Hookshot", WramOff=0x42 } },
        { "Fire Rod", new ItemDef { Game=Game.Lttp, VerifyName="Fire Rod", WramOff=0x45 } },
        { "Ice Rod", new ItemDef { Game=Game.Lttp, VerifyName="Ice Rod", WramOff=0x46 } },
        { "Bombos", new ItemDef { Game=Game.Lttp, VerifyName="Bombos", WramOff=0x47 } },
        { "Ether", new ItemDef { Game=Game.Lttp, VerifyName="Ether", WramOff=0x48 } },
        { "Quake", new ItemDef { Game=Game.Lttp, VerifyName="Quake", WramOff=0x49 } },
        { "Lamp", new ItemDef { Game=Game.Lttp, VerifyName="Lamp", WramOff=0x4a } },
        { "Hammer", new ItemDef { Game=Game.Lttp, VerifyName="Hammer", WramOff=0x4b } },
        { "Bug Catching Net", new ItemDef { Game=Game.Lttp, VerifyName="Bug Catching Net", WramOff=0x4d } },
        { "Book of Mudora", new ItemDef { Game=Game.Lttp, VerifyName="Book of Mudora", WramOff=0x4e } },
        { "Cane of Somaria", new ItemDef { Game=Game.Lttp, VerifyName="Cane of Somaria", WramOff=0x50 } },
        { "Cane of Byrna", new ItemDef { Game=Game.Lttp, VerifyName="Cane of Byrna", WramOff=0x51 } },
        { "Magic Cape", new ItemDef { Game=Game.Lttp, VerifyName="Magic Cape", WramOff=0x52 } },
        { "Pegasus Boots", new ItemDef { Game=Game.Lttp, VerifyName="Boots", WramOff=0x55 } },
        { "Zora's Flippers", new ItemDef { Game=Game.Lttp, VerifyName="Flippers", WramOff=0x56 } },
        { "Moon Pearl", new ItemDef { Game=Game.Lttp, VerifyName="Moon Pearl", WramOff=0x57 } },
        { "Half Magic", new ItemDef { Game=Game.Lttp, VerifyName="Half-Magic", WramOff=0x7b } },
        // LTTP progressive (byte set to 1 => stage 1)
        { "Progressive Glove", new ItemDef { Game=Game.Lttp, VerifyName="Gloves", WramOff=0x54 } },
        { "Progressive Sword", new ItemDef { Game=Game.Lttp, VerifyName="Sword", WramOff=0x59 } },
        { "Progressive Shield", new ItemDef { Game=Game.Lttp, VerifyName="Shield", WramOff=0x5a } },
        { "Magic Mirror", new ItemDef { Game=Game.Lttp, VerifyName="Mirror", WramOff=0x53 } },
        // LTTP special bytes (Flag semantics)
        { "Flute", new ItemDef { Game=Game.Lttp, VerifyName="Ocarina", WramOff=0x8c, Flag=0x02 } },
        { "Magic Powder", new ItemDef { Game=Game.Lttp, VerifyName="Magic Powder", WramOff=0x8c, Flag=0x10 } },
        { "Mushroom", new ItemDef { Game=Game.Lttp, VerifyName="Mushroom", WramOff=0x8c, Flag=0x20 } },
        { "Shovel", new ItemDef { Game=Game.Lttp, VerifyName="Shovel", WramOff=0x8c, Flag=0x04 } },
        { "Bottle", new ItemDef { Game=Game.Lttp, VerifyName="Bottles", WramOff=0x5c, Flag=0xff } },

        // SM toggle items (flag bits)
        { "Morphing Ball", new ItemDef { Game=Game.Sm, VerifyName="Morph Ball", WramOff=0x02, Flag=0x04 } },
        { "Varia Suit", new ItemDef { Game=Game.Sm, VerifyName="Varia Suit", WramOff=0x02, Flag=0x01 } },
        { "Spring Ball", new ItemDef { Game=Game.Sm, VerifyName="Spring Ball", WramOff=0x02, Flag=0x02 } },
        { "Screw Attack", new ItemDef { Game=Game.Sm, VerifyName="Screw Attack", WramOff=0x02, Flag=0x08 } },
        { "Gravity Suit", new ItemDef { Game=Game.Sm, VerifyName="Gravity Suit", WramOff=0x02, Flag=0x20 } },
        { "Hi-Jump Boots", new ItemDef { Game=Game.Sm, VerifyName="High Jump", WramOff=0x03, Flag=0x01 } },
        { "Space Jump", new ItemDef { Game=Game.Sm, VerifyName="Space Jump", WramOff=0x03, Flag=0x02 } },
        { "Morph Bombs", new ItemDef { Game=Game.Sm, VerifyName="Bomb", WramOff=0x03, Flag=0x10 } },
        { "Speed Booster", new ItemDef { Game=Game.Sm, VerifyName="Speed Booster", WramOff=0x03, Flag=0x20 } },
        { "Grappling Beam", new ItemDef { Game=Game.Sm, VerifyName="Grapple", WramOff=0x03, Flag=0x40 } },
        { "Wave Beam", new ItemDef { Game=Game.Sm, VerifyName="Wave Beam", WramOff=0x06, Flag=0x01 } },
        { "Ice Beam", new ItemDef { Game=Game.Sm, VerifyName="Ice Beam", WramOff=0x06, Flag=0x02 } },
        { "Plasma Beam", new ItemDef { Game=Game.Sm, VerifyName="Plasma Beam", WramOff=0x06, Flag=0x08 } },
        { "Charge Beam", new ItemDef { Game=Game.Sm, VerifyName="Charge Beam", WramOff=0x07, Flag=0x10 } },

        // SM ammo (grant only; count semantics are encoding-specific)
        { "Energy Tank", new ItemDef { Game=Game.Sm, VerifyName="", WramOff=0x02, Flag=0xa0, ByteValue=100 } },
        { "Missile", new ItemDef { Game=Game.Sm, VerifyName="", WramOff=0x06, Flag=0xa0, ByteValue=5 } },
        { "Super Missile", new ItemDef { Game=Game.Sm, VerifyName="", WramOff=0x0a, Flag=0xa0, ByteValue=5 } },
        { "Power Bomb", new ItemDef { Game=Game.Sm, VerifyName="", WramOff=0x0e, Flag=0xa0, ByteValue=5 } },
        { "Reserve Tank", new ItemDef { Game=Game.Sm, VerifyName="", WramOff=0x12, Flag=0xa0, ByteValue=100 } },
    };

    // ---------------- location clear table (spoiler location -> pack memory) --------
    static readonly Dictionary<string, LocDef> sLocations = new()
    {
        // LTTP cave/overworld room-slot sections (rooms)
        { "Kakariko Well - Middle", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(47,5),(47,6),(47,7),(47,8)}, AssertName="Kakariko Well", AssertSection="Cave" } },
        { "Blind's Hideout - Far Right", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(285,5),(285,6),(285,7),(285,8)} } },
        { "Aginah's Cave", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(266,4)}, AssertName="Aginah's Cave", AssertSection="Aginah's Cave" } },
        { "Mini Moldorm Cave - Right", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(291,4),(291,5),(291,6),(291,7),(291,10)} } },
        { "Secret Passage", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(85,4)} } },
        { "Spectacle Rock Cave", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(234,10)}, AssertName="Spectacle Rock Cave", AssertSection="Cave" } },
        { "Paradox Cave Lower - Far Left", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(255,4),(255,5)} } },
        { "Chest Game", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(262,10)} } },
        { "Mire Shed - Left", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(269,4),(269,5)} } },
        { "Mimic Cave", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(268,4)}, AssertName="Mimic Cave", AssertSection="Mimic Cave" } },
        { "Pyramid Fairy - Right", new LocDef { Game=Game.Lttp, Kind=0, Slots=new[]{(278,4),(278,5)} } },
        // LTTP overworld events (0x7EF280+idx flag 0x40)
        { "Sunken Treasure", new LocDef { Game=Game.Lttp, Kind=1, Off=59, Flag=0x40, AssertName="The Dam", AssertSection="Sunken Treasure" } },
        { "Zora's Ledge", new LocDef { Game=Game.Lttp, Kind=1, Off=129, Flag=0x40 } },
        // LTTP NPC flags (0x7EF410+off)
        { "Library", new LocDef { Game=Game.Lttp, Kind=2, Off=0, Flag=0x80 } },

        // SM rooms (0x7ED870+off flag)
        { "Power Bomb (blue Brinstar)", new LocDef { Game=Game.Sm, Kind=3, Off=0x03, Flag=0x08, AssertName="Power Bomb (blue Brinstar)", AssertSection="Power Bomb (blue Brinstar)" } },
        { "Power Bomb (green Brinstar bottom)", new LocDef { Game=Game.Sm, Kind=3, Off=0x01, Flag=0x20 } },
        { "Power Bomb (pink Brinstar)", new LocDef { Game=Game.Sm, Kind=3, Off=0x03, Flag=0x01 } },
        { "Missile (red Brinstar spike room)", new LocDef { Game=Game.Sm, Kind=3, Off=0x05, Flag=0x02 } },
        { "Missile (Crateria gauntlet left)", new LocDef { Game=Game.Sm, Kind=3, Off=0x01, Flag=0x04 } },
        { "Missile (outside Wrecked Ship bottom)", new LocDef { Game=Game.Sm, Kind=3, Off=0x00, Flag=0x02 } },
        { "Missile (outside Wrecked Ship top)", new LocDef { Game=Game.Sm, Kind=3, Off=0x00, Flag=0x04 } },
        { "Missile (outside Wrecked Ship middle)", new LocDef { Game=Game.Sm, Kind=3, Off=0x00, Flag=0x08 } },
        { "Missile (Gravity Suit)", new LocDef { Game=Game.Sm, Kind=3, Off=0x10, Flag=0x04 } },
        { "Energy Tank, Wrecked Ship", new LocDef { Game=Game.Sm, Kind=3, Off=0x10, Flag=0x10 } },
        { "Right Super, Wrecked Ship", new LocDef { Game=Game.Sm, Kind=3, Off=0x10, Flag=0x40 } },
        { "Reserve Tank, Norfair", new LocDef { Game=Game.Sm, Kind=3, Off=0x07, Flag=0x20 } },
        { "Missile (bubble Norfair green door)", new LocDef { Game=Game.Sm, Kind=3, Off=0x07, Flag=0x80 } },
        { "Missile (bubble Norfair)", new LocDef { Game=Game.Sm, Kind=3, Off=0x08, Flag=0x01 } },
        { "Speed Booster", new LocDef { Game=Game.Sm, Kind=3, Off=0x08, Flag=0x04, AssertName="Speed Booster", AssertSection="Speed Booster" } },
        { "Wave Beam", new LocDef { Game=Game.Sm, Kind=3, Off=0x08, Flag=0x10, AssertName="Wave Beam", AssertSection="Wave Beam" } },
        { "Super Missile (green Maridia)", new LocDef { Game=Game.Sm, Kind=3, Off=0x11, Flag=0x02 } },
        { "Energy Tank, Mama turtle", new LocDef { Game=Game.Sm, Kind=3, Off=0x11, Flag=0x04 } },
        { "Missile (green Maridia tatori)", new LocDef { Game=Game.Sm, Kind=3, Off=0x11, Flag=0x08 } },
        { "Missile (yellow Maridia false wall)", new LocDef { Game=Game.Sm, Kind=3, Off=0x11, Flag=0x40 } },
        { "Reserve Tank, Maridia", new LocDef { Game=Game.Sm, Kind=3, Off=0x12, Flag=0x02 } },
        { "Screw Attack", new LocDef { Game=Game.Sm, Kind=3, Off=0x09, Flag=0x80 } },
        { "Energy Tank, Ridley", new LocDef { Game=Game.Sm, Kind=3, Off=0x09, Flag=0x40 } },
        { "Energy Tank, Crocomire", new LocDef { Game=Game.Sm, Kind=3, Off=0x06, Flag=0x10 } },
        { "Missile (below Crocomire)", new LocDef { Game=Game.Sm, Kind=3, Off=0x07, Flag=0x04 } },
        { "Spring Ball", new LocDef { Game=Game.Sm, Kind=3, Off=0x12, Flag=0x40 } },
        { "Ice Beam", new LocDef { Game=Game.Sm, Kind=3, Off=0x06, Flag=0x04 } },
        { "Missile (green Brinstar behind reserve tank)", new LocDef { Game=Game.Sm, Kind=3, Off=0x02, Flag=0x04 } },
    };

    public static async Task<int> RunAsync(McpClient mcp, MockClient mock, string backend, string spoilerPath)
    {
        sPass = sFail = 0;
        sFailures.Clear();
        sFailures.TrimExcess();
        sGranted.Clear();
        sStuck = 0; sDisconnects = 0; sPickups = 0;

        string provider = backend.StartsWith("sni") ? "sni" : "nwa";
        sFast = Environment.GetEnvironmentVariable("EMOTRACKER_REPLAY_FAST") == "1";
        int seed = Random.Shared.Next();
        var seedEnv = Environment.GetEnvironmentVariable("EMOTRACKER_SMZ3_SEED");
        if (!string.IsNullOrWhiteSpace(seedEnv) && int.TryParse(seedEnv, out var se)) seed = se;
        sRng = new Random(seed);

        // Load the spoiler playthrough.
        List<SpoilerPickup> picks;
        try { picks = SpoilerLogParser.ParsePlaythrough(spoilerPath); }
        catch (Exception ex) { Console.WriteLine($"[FAIL] could not parse spoiler: {ex.Message}"); return 1; }
        sTotalPickups = picks.Count;
        Console.WriteLine($"=== SEED REPLAY TIER ===");
        Console.WriteLine($"  pack={PackUid} backend={backend} spoiler={spoilerPath}");
        Console.WriteLine($"  playthrough pickups={picks.Count} seed={seed} fast={sFast}");

        // 1. SMZ3 profile + load pack + connect.
        Check("mock/profile smz3", await SetProfile(mcp, mock), "set smz3 profile failed");
        Check("load_pack(smalttprando)", JsonTrue(await mcp.CallAsync("load_pack",
            JsonSerializer.Serialize(new { uniqueId = PackUid, variant = "standard" }))), "SMZ3 pack load failed");
        Check("select provider", JsonTrue(await mcp.CallAsync("autotracker_select_provider",
            JsonSerializer.Serialize(new { uid = provider }))), $"provider '{provider}' failed");
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
        }, 15000);
        Check("select mock device", deviceFound, "no device discovered");
        Check("autotracker_start", JsonTrue(await mcp.CallAsync("autotracker_start", "{}")), "start failed");
        Check("autotracker connected", await WaitConnected(mcp, 15000), "never connected");

        // Start in the world of the first pickup.
        Game current = GuessGame(picks[0].Location);
        await SwitchGame(mock, current);

        // 2. Play through.
        int lastSphere = 0;
        foreach (var p in picks)
        {
            sPickups++;

            // Travel to the pickup's world if it changed.
            Game g = GuessGame(p.Location);
            if (g != current)
            {
                await Travel(mock, g);
                current = g;
            }

            // Clear the location.
            if (sLocations.TryGetValue(p.Location, out var loc))
                await ClearLocation(mock, loc, g);

            // Grant the item (live + mirror).
            if (sItems.TryGetValue(p.Item, out var it))
                await GrantItem(mock, it);

            // Realistic pickup delay.
            await PickupDelay();

            // Verify the item reached the tracker (stuck detection).
            if (it != null && it.VerifyName.Length > 0)
            {
                bool ok = await WaitItem(mcp, it.VerifyName, true, sFast ? 8000 : 12000);
                if (!ok)
                {
                    if (await IsConnected(mcp)) sStuck++;
                    else sDisconnects++;
                    sFailures.Add($"pickup {p.Sphere}:{p.Item} ('{it.VerifyName}') not tracked @{p.Location} (pickup {sPickups}/{sTotalPickups})");
                }
            }

            // Assert location clear on a sampled subset.
            if (loc != null && loc.AssertName.Length > 0 && (sFast || sPickups % 3 == 0))
            {
                bool cleared = await LocCleared(mcp, loc.AssertName, loc.AssertSection, sFast ? 4000 : 8000);
                Check($"loc cleared {loc.AssertName}/{loc.AssertSection} @{p.Location}",
                    cleared, $"location not noted cleared @{p.Location} (pickup {sPickups}/{sTotalPickups})");
                if (!cleared) continue;
            }

            if (p.Sphere != lastSphere)
            {
                lastSphere = p.Sphere;
                await SphereDelay();
            }
        }

        // 3. Summary.
        Check($"played through all {picks.Count} pickups", sPickups == picks.Count, $"short {sPickups}/{picks.Count}");
        Check("auto-tracking never stuck", sStuck == 0, $"{sStuck} unpropagated pickups while connected");
        Check("no connection drop", sDisconnects == 0, $"{sDisconnects} disconnects");
        Check("connected at end", await WaitConnected(mcp, 8000), "lost connection by end");
        Check($"granted {sGranted.Count} unique items", sGranted.Count > 0, "no items granted");

        Console.WriteLine($"\n=== SEED REPLAY RESULT: {sPass} passed, {sFail} failed (pickups={sPickups}, stuck={sStuck}, disconnects={sDisconnects}) ===");
        if (sFailures.Count > 0)
        {
            Console.WriteLine("Failures:");
            foreach (var f in sFailures) Console.WriteLine($"  - {f}");
        }
        await mcp.CallAsync("autotracker_stop", "{}");
        return sFail == 0 ? 0 : 1;
    }

    // ---------------- world travel / delays ----------------

    static async Task<bool> SetProfile(McpClient mcp, MockClient mock)
    {
        await mock.SetProfileAsync("smz3");
        await mock.ResetAsync();
        return (await mock.GetProfileAsync()).Equals("smz3", StringComparison.OrdinalIgnoreCase);
    }

    static async Task SwitchGame(MockClient mock, Game g)
    {
        await mock.SwitchGameAsync(g == Game.Sm);
        await Task.Delay(sFast ? 400 : 1500); // world-load transition
    }

    static async Task Travel(MockClient mock, Game g)
    {
        await SwitchGame(mock, g);
        Console.WriteLine($"  -> travelled to {(g == Game.Sm ? "SM" : "LTTP")} (pickup {sPickups}/{sTotalPickups})");
    }

    static Task PickupDelay()
        => Task.Delay(sFast ? 350 : sRng.Next(2500, 5000));

    static Task SphereDelay()
        => Task.Delay(sFast ? 600 : sRng.Next(8000, 18000));

    // ---------------- mock memory writers ----------------

    static async Task ClearLocation(MockClient mock, LocDef loc, Game g)
    {
        switch (loc.Kind)
        {
            case 0: // LTTP room slots
                foreach (var (room, bit) in loc.Slots)
                    await SetWordBit(mock, LtppRooms + (ulong)room * 2, bit);
                break;
            case 1: // LTTP overworld flag
                await SetByteFlag(mock, LtppOverworld + loc.Off, loc.Flag);
                break;
            case 2: // LTTP NPC flag
                await SetByteFlag(mock, LtppNpc + loc.Off, loc.Flag);
                break;
            case 3: // SM room flag
                await SetByteFlag(mock, SmRooms + loc.Off, loc.Flag);
                break;
        }
    }

    static async Task GrantItem(MockClient mock, ItemDef it)
    {
        sGranted.Add(it.VerifyName.Length > 0 ? it.VerifyName : $"({it.Game}@{it.WramOff})");
        ulong liveBase = it.Game == Game.Sm ? SmItemsLive : LtppItemsLive;
        ulong mirrorBase = it.Game == Game.Sm ? SmItemsMirror : LtppItemsMirror;
        if (it.Game == Game.Sm && it.Flag == 0xa0)
        {
            // SM ammo lives in a different base pair (ammo sentinel Flag==0xa0).
            liveBase = SmAmmoLive;
            mirrorBase = SmAmmoMirror;
        }

        await WriteDet(mock, liveBase + it.WramOff, it);
        await WriteDet(mock, mirrorBase + it.WramOff, it);

        // Mirror LTTP items need the value at base+off within the watched region;
        // the pack's callback base (0xA17B00) requires off+0x40 >= watch start — all
        // LTTP item offsets here are >= 0x40, so the mirror write lands in-watch.
    }

    static async Task WriteDet(MockClient mock, ulong addr, ItemDef it)
    {
        if (it.Flag == 0xa0)
        {
            // ammo: 2-byte LE count
            byte lo = (byte)(it.ByteValue & 0xFF);
            byte hi = (byte)((it.ByteValue >> 8) & 0xFF);
            await mock.WriteAsync(addr, $"{lo:X2} {hi:X2}");
        }
        else if (it.Flag == 0xff)
        {
            // bottle: FFS set one bottle byte
            await mock.WriteAsync(addr, "01");
        }
        else if (it.Flag == 0)
        {
            await mock.WriteAsync(addr, it.ByteValue == 0 ? "01" : it.ByteValue.ToString("X2"));
        }
        else
        {
            await SetByteFlag(mock, addr, it.Flag);
        }
    }

    static async Task SetWordBit(MockClient mock, ulong addr, int bit)
    {
        // read the current U16, OR the bit, write back
        var cur = await mock.ReadBusAsync(addr, 2);
        ushort val = (ushort)(cur.Length >= 2 ? (cur[0] | (cur[1] << 8)) : 0);
        val |= (ushort)(1 << bit);
        await mock.WriteAsync(addr, $"{val & 0xFF:X2} {(val >> 8) & 0xFF:X2}");
    }

    static async Task SetByteFlag(MockClient mock, ulong addr, byte flag)
    {
        var cur = await mock.ReadBusAsync(addr, 1);
        byte v = cur.Length >= 1 ? cur[0] : (byte)0;
        v |= flag;
        await mock.WriteAsync(addr, v.ToString("X2"));
    }

    static Game GuessGame(string loc)
    {
        if (sLocations.TryGetValue(loc, out var l)) return l.Game;
        // Heuristic: SM-zone keywords => SM.
        if (loc.Contains("Brinstar") || loc.Contains("Crateria") || loc.Contains("Wrecked Ship")
            || loc.Contains("Maridia") || loc.Contains("Norfair") || loc.Contains("Crocomire"))
            return Game.Sm;
        // Dungeon-prefixed LTTP locations default to LTTP.
        return Game.Lttp;
    }

    // ---------------- MCP verification ----------------

    static async Task<bool> WaitItem(McpClient mcp, string name, bool active, int ms)
        => JsonTrue(await mcp.CallAsync("autotracker_wait_item",
            JsonSerializer.Serialize(new { name, itemActive = active, timeoutMs = ms })));

    static async Task<bool> LocCleared(McpClient mcp, string loc, string section, int ms)
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
                        && s.TryGetProperty("availableChestCount", out var a) && a.GetInt32() <= 0)
                        return true;
                }
                await Task.Delay(150);
            }
            return false;
        }
        catch { return false; }
    }

    static async Task<bool> IsConnected(McpClient mcp)
    {
        try
        {
            var r = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(r);
            bool c = doc.RootElement.TryGetProperty("connected", out var v) && v.GetBoolean();
            bool rc = doc.RootElement.TryGetProperty("reconnecting", out var rv) && rv.GetBoolean();
            return c && !rc;
        }
        catch { return false; }
    }

    static async Task<bool> WaitConnected(McpClient mcp, int ms)
        => await mcp.WaitUntilAsync(async () =>
        {
            var r = await mcp.CallAsync("autotracker_get", "{}");
            using var doc = JsonDocument.Parse(r);
            return (doc.RootElement.TryGetProperty("connected", out var v) && v.GetBoolean(), r);
        }, ms);

    // ---------------- helpers ----------------

    static void Check(string name, bool ok, string failMsg)
    {
        if (ok) { sPass++; Console.WriteLine($"  [PASS] {name}"); }
        else { sFail++; sFailures.Add($"{name} — {failMsg}"); Console.WriteLine($"  [FAIL] {name} — {failMsg}"); }
    }

    static bool JsonTrue(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("success", out var v) && v.GetBoolean(); }
        catch { return false; }
    }

    static JsonElement Json(string json)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.Clone();
    }

    static int JsonDevicesCount(string json, string providerUid)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.GetProperty("providers").EnumerateArray())
                if (p.TryGetProperty("uid", out var uid)
                    && uid.GetString()?.Equals(providerUid, StringComparison.OrdinalIgnoreCase) == true
                    && p.TryGetProperty("availableDevices", out var d))
                    return d.GetArrayLength();
            return 0;
        }
        catch { return 0; }
    }
}
