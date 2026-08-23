namespace EmoTracker.MockRandomizer.Memory;

/// <summary>
/// The single mutable backing store shared by all mock backends (SNI gRPC and
/// NWA TCP). Holds the virtual WRAM, CARTROM, and SRAM images plus a live
/// "in-game" flag that simulates the room-load state the emosaru pack watches at
/// <c>0x7E0010</c>.
/// </summary>
public sealed class MemorySession
{
    public byte[] Wram = Array.Empty<byte>();
    public byte[] CartRom = Array.Empty<byte>();
    public byte[] Sram = Array.Empty<byte>();

    readonly object mLock = new();

    public MemorySession()
    {
        Reset();
    }

    /// <summary>Restores the default image (WRAM zeroed, header in place, in-game).</summary>
    public void Reset()
    {
        lock (mLock)
        {
            Wram = new byte[SnesBus.WramSize * 2]; // banks 7E-7F
            CartRom = new byte[SnesBus.CartRomSize];
            Sram = new byte[SnesBus.SramSize];

            // Place a valid LoROM header at both candidate offsets so both the
            // NWA SnesAddressMap (0x7FB0/0xFFB0/0x40FFB0) and SNI MappingDetect
            // (0x00FFB0 bus) resolve LoROM.
            var header = SnesBus.BuildLoRomHeader();
            Array.Copy(header, 0, CartRom, 0x7FB0, 0x50);
            Array.Copy(header, 0, CartRom, 0xFFB0, 0x50);

            // In-game flag: 0x06+ means the player is in-game (emosaru watches this).
            WriteBusLocked(0x7E0010, new byte[] { 0x06 });
        }
    }

    // ---------- Bus reads/writes (24-bit SNES bus) --------------------------

    public byte[] ReadBus(ulong busAddress, int length)
    {
        lock (mLock)
        {
            var region = SnesBus.Map(busAddress);
            if (region == null) return new byte[length];
            byte[] bytes = GetRegionBytes(region.Value.Domain);
            return Slice(bytes, region.Value.Offset, length);
        }
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
