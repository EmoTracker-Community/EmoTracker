namespace EmoTracker.MockRandomizer.Memory;

/// <summary>Which randomizer cartridge profile the virtual console is booting.</summary>
public enum CartridgeProfile
{
    /// <summary>A Link to the Past Randomizer — LoROM, pure WRAM tracking.</summary>
    AlttpLoRom,

    /// <summary>Super Metroid + LttP combo Randomizer (SMZ3) — ExHiROM, with both
    /// games' live data in WRAM and cross-game data mirrored into ExHiROM SRAM.</summary>
    Smz3ExHiRom
}

/// <summary>
/// The single mutable backing store shared by all mock backends (SNI gRPC and
/// NWA TCP). Holds the virtual WRAM, CARTROM, and SRAM images plus a live
/// "in-game" flag that simulates the room-load state the packs watch at
/// <c>0x7E0010</c> (ALttPR) and the which-game byte at <c>0xA173FE</c> (SMZ3).
/// </summary>
public sealed class MemorySession
{
    public byte[] Wram = Array.Empty<byte>();
    public byte[] CartRom = Array.Empty<byte>();
    public byte[] Sram = Array.Empty<byte>();

    readonly object mLock = new();

    public CartridgeProfile ActiveProfile { get; private set; } = CartridgeProfile.AlttpLoRom;

    public MemorySession()
    {
        Reset();
    }

    /// <summary>Selects a cartridge profile and reloads its default image.</summary>
    public void SetProfile(CartridgeProfile profile)
    {
        lock (mLock)
        {
            ActiveProfile = profile;
            ResetLocked();
        }
    }

    /// <summary>Restores the default image for the active profile.</summary>
    public void Reset()
    {
        lock (mLock)
        {
            ResetLocked();
        }
    }

    void ResetLocked()
    {
        Wram = new byte[SnesBus.WramSize * 2]; // banks 7E-7F
        CartRom = new byte[SnesBus.CartRomSize];
        Sram = new byte[ActiveProfile == CartridgeProfile.Smz3ExHiRom
            ? SnesBus.SramSizeExHiRom
            : SnesBus.SramSizeLoRom];

        if (ActiveProfile == CartridgeProfile.Smz3ExHiRom)
            InitSmz3Image();
        else
            InitAlttpImage();
    }

    void InitAlttpImage()
    {
        // Place a valid LoROM header at both candidate offsets so both the
        // NWA SnesAddressMap (0x7FB0/0xFFB0/0x40FFB0) and SNI MappingDetect
        // (0x00FFB0 bus) resolve LoROM.
        var header = SnesBus.BuildLoRomHeader();
        Array.Copy(header, 0, CartRom, 0x7FB0, 0x50);
        Array.Copy(header, 0, CartRom, 0xFFB0, 0x50);

        // In-game flag: 0x06+ means the player is in-game (emosaru watches this).
        WriteBusLocked(0x7E0010, new byte[] { 0x06 });
    }

    void InitSmz3Image()
    {
        // ExHiROM ROM header (SMZ3 is ExHiROM). The mock's bus map resolves the
        // header-detection reads MappingDetect issues: LoROM reads bus $00:FFB0
        // → CartRom 0x7FB0; HiROM/ExHiROM reads bus $40:FFB0 → CartRom 0x207FB0.
        // The ExHiROM enum is returned by MappingDetect regardless, but placing
        // the header at both offsets keeps detection self-consistent.
        var header = SnesBus.BuildExHiRomHeader();
        Array.Copy(header, 0, CartRom, 0x7FB0, 0x50);
        Array.Copy(header, 0, CartRom, 0x207FB0, 0x50);

        // Which-game byte read by updateGame(): 0x00 = LTTP, 0xFF = SM.
        // Default to LTTP and mark both games "in-game ready".
        WriteBusLocked(0xA173FE, new byte[] { 0x00 });
        WriteBusLocked(0x7E0010, new byte[] { 0x06 });   // LTTP in-game module
        WriteBusLocked(0x7E0998, new byte[] { 0x07 });   // SM in-game module
        WriteBusLocked(0xA17402, new byte[] { 0x00 });   // SM done (brain)
        WriteBusLocked(0xA17506, new byte[] { 0x00 });   // LTTP done (ganon)
    }

    /// <summary>
    /// In SMZ3 mode, switches the simulated "current game". EmoTracker's
    /// <c>updateGame()</c> watches <c>0xA173FE</c> (which game) and re-registers
    /// its memory watches accordingly; the corresponding in-game module flag is
    /// set so live data is applied. No-op in ALttP mode.
    /// </summary>
    public void SwitchSmz3Game(bool sm)
    {
        lock (mLock)
        {
            if (ActiveProfile != CartridgeProfile.Smz3ExHiRom)
                return;

            WriteBusLocked(0xA173FE, new byte[] { sm ? (byte)0xFF : (byte)0x00 });
            if (sm)
                WriteBusLocked(0x7E0998, new byte[] { 0x07 }); // SM in-game module
            else
                WriteBusLocked(0x7E0010, new byte[] { 0x06 }); // LTTP in-game module
        }
    }

    /// <summary>Returns whether SMZ3 mode is currently simulating the SM game.</summary>
    public bool IsSmz3GameSm()
    {
        lock (mLock)
        {
            if (ActiveProfile != CartridgeProfile.Smz3ExHiRom) return false;
            return ReadBusLocked(0xA173FE, 1).FirstOrDefault() == 0xFF;
        }
    }

    // ---------- Bus reads/writes (24-bit SNES bus) --------------------------

    public byte[] ReadBus(ulong busAddress, int length)
    {
        lock (mLock)
        {
            return ReadBusLocked(busAddress, length);
        }
    }

    byte[] ReadBusLocked(ulong busAddress, int length)
    {
        var region = SnesBus.Map(busAddress);
        if (region == null) return new byte[length];
        byte[] bytes = GetRegionBytes(region.Value.Domain);
        return Slice(bytes, region.Value.Offset, length);
    }

    public void WriteBus(ulong busAddress, byte[] data)
    {
        lock (mLock)
        {
            WriteBusLocked(busAddress, data);
        }
    }

    void WriteBusLocked(ulong busAddress, byte[] data)
    {
        var region = SnesBus.Map(busAddress);
        if (region == null) return;
        byte[] bytes = GetRegionBytes(region.Value.Domain);
        if (region.Value.Offset >= (ulong)bytes.Length) return;
        int n = (int)Math.Min((ulong)data.Length, (ulong)bytes.Length - region.Value.Offset);
        Array.Copy(data, 0, bytes, (int)region.Value.Offset, n);
    }

    // ---------- Named-domain reads/writes (NWA CORE_READ / bCORE_WRITE) -----

    public byte[] ReadDomain(string domain, ulong offset, int length)
    {
        lock (mLock)
        {
            byte[] region = GetRegionBytes(domain);
            if (region.Length == 0 && !domain.Equals("System Bus", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<byte>();
            return Slice(region, offset, length);
        }
    }

    public bool WriteDomain(string domain, ulong offset, byte[] data)
    {
        lock (mLock)
        {
            byte[] region = GetRegionBytes(domain);
            if (region.Length == 0) return false;
            if (offset >= (ulong)region.Length) return false;
            int n = (int)Math.Min((ulong)data.Length, (ulong)region.Length - offset);
            Array.Copy(data, 0, region, (int)offset, n);
            return true;
        }
    }

    /// <summary>With the System Bus preferred by NWA: bus address == domain offset.</summary>
    public byte[] ReadSystemBus(ulong offset, int length) => ReadBus(offset, length);
    public void WriteSystemBus(ulong offset, byte[] data) => WriteBus(offset, data);

    /// <summary>Direct bus write convenience used by the CLI/scripting layer.</summary>
    public void SetBus(ulong busAddress, byte[] data) => WriteBus(busAddress, data);

    byte[] GetRegionBytes(string domain)
    {
        switch (domain.ToLowerInvariant())
        {
            case "wram": return Wram;
            case "cartrom": return CartRom;
            case "sram": return Sram;
            case "system bus":
            {
                // System Bus spans 8MB; expose a bus-addressed view. We emulate it
                // as a read straight through the bus mapper, handled by callers via
                // ReadSystemBus. Here we return an empty marker so domain callers
                // that expect raw bytes use ReadSystemBus instead.
                return Array.Empty<byte>();
            }
            default: return Array.Empty<byte>();
        }
    }

    static byte[] Slice(byte[] region, ulong offset, int length)
    {
        if (offset >= (ulong)region.Length) return new byte[length];
        int n = (int)Math.Min((ulong)length, (ulong)region.Length - offset);
        var result = new byte[length];
        Array.Copy(region, (int)offset, result, 0, n);
        return result;
    }
}
