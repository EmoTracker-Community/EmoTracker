namespace EmoTracker.MockRandomizer.Memory;

/// <summary>
/// Resolves an SNI read/write (address + address space + memory mapping) to an
/// actual byte span in the <see cref="MemorySession"/>. The mock supports the
/// FxPakPro linear space and the SnesABus (LoROM) space, mirroring how
/// <c>SniDevice</c> drives real backends.
/// </summary>
public sealed class SnivAddressTranslator
{
    readonly MemorySession mSession;

    public SnivAddressTranslator(MemorySession session)
    {
        mSession = session;
    }

    /// <summary>Read from the session honoring the requested address space/mapping.</summary>
    public byte[] Read(uint requestAddress, int addressSpace, int memoryMapping, int length)
    {
        switch (addressSpace)
        {
            case 2: // Raw — pass through as bus address.
                return mSession.ReadBus(requestAddress, length);
            case 1: // SnesABus — bus address + LoROM mapping.
                return mSession.ReadBus(requestAddress, length);
            default: // FixPakPro (0) — translate linear FxPakPro map.
                return ReadFxPakPro(requestAddress, length);
        }
    }

    /// <summary>Write to the session honoring the requested space/mapping.</summary>
    public bool Write(uint requestAddress, int addressSpace, int memoryMapping, byte[] data)
    {
        switch (addressSpace)
        {
            case 2:
                mSession.WriteBus(requestAddress, data);
                return true;
            case 1:
                mSession.WriteBus(requestAddress, data);
                return true;
            default:
                return WriteFxPakPro(requestAddress, data);
        }
    }

    byte[] ReadFxPakPro(uint addr, int length)
    {
        if (addr < 0xE00000) // ROM: $000000-$DFFFFF
            return mSession.ReadDomain("CARTROM", addr, length);
        if (addr < 0xF00000) // SRAM: $E00000-$EFFFFF
            return mSession.ReadDomain("SRAM", addr - 0xE00000, length);
        if (addr is >= 0xF50000 and < 0xF70000) // WRAM: $F50000-$F6FFFF
            return mSession.ReadDomain("WRAM", addr - 0xF50000, length);
        return new byte[length];
    }

    bool WriteFxPakPro(uint addr, byte[] data)
    {
        if (addr < 0xE00000)
            return mSession.WriteDomain("CARTROM", addr, data);
        if (addr < 0xF00000)
            return mSession.WriteDomain("SRAM", addr - 0xE00000, data);
        if (addr is >= 0xF50000 and < 0xF70000)
            return mSession.WriteDomain("WRAM", addr - 0xF50000, data);
        return false;
    }
}
