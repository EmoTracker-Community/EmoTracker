namespace EmoTracker.MockRandomizer.Memory;

/// <summary>
/// Translates 24-bit SNES bus addresses into a (region, offset) pair for a
/// LoROM cart. This mirrors <c>EmoTracker.Providers.NWA.AddressMaps.SnesAddressMap</c>
/// and the SNI <c>SnesABus</c> convention so that reads of e.g. <c>0x7E0010</c>
/// resolve to WRAM and <c>0x00FFB0</c> to the CARTROM header.
/// </summary>
public readonly record struct SnesRegion(string Domain, ulong Offset);

public static class SnesBus
{
    public const ulong WramSize = 0x10000; // 64 KB per bank; banks 7E-7F = 128 KB
    public const ulong SramSize = 0x10000; // 64 KB
    public const ulong CartRomSize = 0x400000; // 4 MB LoROM

    /// <summary>
    /// Maps a 24-bit bus address to a virtual region, or null if unmapped.
    /// Implements the LoROM layout (matching SnesAddressMap).
    /// </summary>
    public static SnesRegion? Map(ulong busAddress)
    {
        int bank = (int)((busAddress >> 16) & 0xFF);
        int offset = (int)(busAddress & 0xFFFF);

        // WRAM: banks $7E-$7F — full 128 KB.
        if (bank == 0x7E || bank == 0x7F)
        {
            ulong wramOffset = (ulong)(bank - 0x7E) * 0x10000 + (ulong)offset;
            return new SnesRegion("WRAM", wramOffset);
        }

        // WRAM mirror: banks $00-$3F and $80-$BF, offsets $0000-$1FFF.
        if ((bank <= 0x3F || (bank >= 0x80 && bank <= 0xBF)) && offset < 0x2000)
            return new SnesRegion("WRAM", (ulong)offset);

        // LoROM ROM: banks $00-$7D / $80-$FF, upper half $8000-$FFFF, 32 KB/bank.
        if (offset >= 0x8000)
        {
            int effectiveBank = bank >= 0x80 ? bank - 0x80 : bank;
            if (effectiveBank <= 0x7D)
            {
                ulong romOffset = (ulong)effectiveBank * 0x8000 + (ulong)(offset - 0x8000);
                if (romOffset < CartRomSize)
                    return new SnesRegion("CARTROM", romOffset);
            }
        }

        // LoROM SRAM: banks $70-$7D, offsets $0000-$7FFF.
        if (bank >= 0x70 && bank <= 0x7D && offset < 0x8000)
            return new SnesRegion("SRAM", (ulong)(bank - 0x70) * 0x8000 + (ulong)offset);

        // SRAM mirror: banks $F0-$FF, offsets $0000-$7FFF.
        if (bank >= 0xF0 && bank <= 0xFF && offset < 0x8000)
            return new SnesRegion("SRAM", (ulong)(bank - 0xF0) * 0x8000 + (ulong)offset);

        return null;
    }

    /// <summary>
    /// Returns a valid LoROM internal header (0x50 bytes) placed at the
    /// requested CARTROM offset (caller places at 0x7FB0 and 0xFFB0).
    /// </summary>
    public static byte[] BuildLoRomHeader(string title = "MOCK ALTTP RANDO")
    {
        byte[] h = new byte[0x50];

        // Title at +0x10 (21 bytes).
        byte[] titleBytes = System.Text.Encoding.ASCII.GetBytes(title);
        for (int i = 0; i < 21 && i < titleBytes.Length; i++)
            h[0x10 + i] = titleBytes[i];

        // Map mode byte at +0x25: $20 = LoROM.
        h[0x25] = 0x20;

        // ROM size byte at +0x27: 0x0A = 4 MB.
        h[0x27] = 0x0A;

        // Checksum (little-endian) at +0x2E/0x2F and complement at +0x2C/0x2D.
        ushort checksum = 0x1234;
        ushort complement = (ushort)~checksum;
        h[0x2C] = (byte)(complement & 0xFF);
        h[0x2D] = (byte)(complement >> 8);
        h[0x2E] = (byte)(checksum & 0xFF);
        h[0x2F] = (byte)(checksum >> 8);

        return h;
    }
}
