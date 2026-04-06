using Serilog;
using System;
using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA.AddressMaps
{
    /// <summary>
    /// Maps SNES 24-bit bus addresses to BSNES NWA memory domains (WRAM, CARTROM, CARTRAM).
    /// Detects LoROM vs HiROM by reading the ROM header from the CARTROM domain during initialization.
    /// </summary>
    internal class SnesAddressMap : INwaAddressMap
    {
        internal enum SnesRomLayout
        {
            LoROM,
            HiROM
        }

        SnesRomLayout mLayout;

        internal SnesRomLayout Layout => mLayout;

        public async Task InitializeAsync(NwaRawDomainReader readDomain)
        {
            mLayout = await DetectRomLayoutAsync(readDomain).ConfigureAwait(false);
            Log.Information("[NWA/SNES] Detected ROM layout: {Layout}", mLayout);
        }

        static async Task<SnesRomLayout> DetectRomLayoutAsync(NwaRawDomainReader readDomain)
        {
            // The SNES internal ROM header contains a map mode byte and a checksum/complement pair.
            // In LoROM, the header is at CARTROM offset $7FB0; in HiROM, at $FFB0.
            // We read both and score them to determine which is valid.
            try
            {
                int loScore = 0;
                int hiScore = 0;

                byte[] loHeader = null;
                byte[] hiHeader = null;

                try { loHeader = await readDomain("CARTROM", 0x7FB0, 0x50).ConfigureAwait(false); }
                catch { }

                try { hiHeader = await readDomain("CARTROM", 0xFFB0, 0x50).ConfigureAwait(false); }
                catch { }

                if (loHeader != null && loHeader.Length >= 0x50)
                    loScore = ScoreHeader(loHeader, expectLoROM: true);

                if (hiHeader != null && hiHeader.Length >= 0x50)
                    hiScore = ScoreHeader(hiHeader, expectLoROM: false);

                Log.Debug("[NWA/SNES] Header scores — LoROM: {Lo}, HiROM: {Hi}", loScore, hiScore);

                if (hiScore > loScore)
                    return SnesRomLayout.HiROM;

                return SnesRomLayout.LoROM;
            }
            catch (Exception ex)
            {
                Log.Warning("[NWA/SNES] ROM layout detection failed, defaulting to LoROM: {Message}", ex.Message);
                return SnesRomLayout.LoROM;
            }
        }

        /// <summary>
        /// Score a candidate SNES internal header for validity.
        /// Header is 0x50 bytes read from $xxB0, so the map mode byte is at relative offset 0x25
        /// ($xxD5), checksum complement at 0x2C-0x2D ($xxDC-$xxDD), checksum at 0x2E-0x2F ($xxDE-$xxDF).
        /// </summary>
        static int ScoreHeader(byte[] header, bool expectLoROM)
        {
            int score = 0;

            // Map mode byte at offset 0x25 (= $xxD5 - $xxB0)
            byte mapMode = header[0x25];

            // Bits 7-5 should be 001 (value & 0xE0 == 0x20)
            if ((mapMode & 0xE0) == 0x20)
                score += 2;

            // Bit 0: 0 = LoROM, 1 = HiROM
            bool isHiROM = (mapMode & 0x01) != 0;
            if (expectLoROM && !isHiROM)
                score += 3;
            else if (!expectLoROM && isHiROM)
                score += 3;

            // Checksum complement at offset 0x2C-0x2D, checksum at 0x2E-0x2F
            ushort complement = (ushort)(header[0x2C] | (header[0x2D] << 8));
            ushort checksum = (ushort)(header[0x2E] | (header[0x2F] << 8));
            if ((ushort)(complement + checksum) == 0xFFFF && complement != 0 && checksum != 0)
                score += 4;

            // Title at offset 0x10 (= $xxC0 - $xxB0), 21 bytes — should be printable ASCII or spaces
            int printableCount = 0;
            for (int i = 0x10; i < 0x10 + 21; i++)
            {
                byte c = header[i];
                if ((c >= 0x20 && c <= 0x7E) || c == 0x00)
                    printableCount++;
            }
            if (printableCount >= 16)
                score += 2;

            // ROM size byte at offset 0x27 — should be reasonable (0x08 to 0x0D = 256KB to 8MB)
            byte romSize = header[0x27];
            if (romSize >= 0x08 && romSize <= 0x0D)
                score += 1;

            return score;
        }

        public NwaAddressMapping? MapAddress(ulong busAddress)
        {
            // Decompose 24-bit SNES address into bank (high byte) and offset (low 16 bits)
            int bank = (int)((busAddress >> 16) & 0xFF);
            int offset = (int)(busAddress & 0xFFFF);

            // WRAM: banks $7E-$7F — full 128KB
            if (bank == 0x7E || bank == 0x7F)
            {
                ulong wramOffset = (ulong)(bank - 0x7E) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("WRAM", wramOffset);
            }

            // WRAM mirror: banks $00-$3F, offsets $0000-$1FFF (low 8KB of WRAM)
            if (bank >= 0x00 && bank <= 0x3F && offset < 0x2000)
            {
                return new NwaAddressMapping("WRAM", (ulong)offset);
            }

            // WRAM mirror: banks $80-$BF, offsets $0000-$1FFF
            if (bank >= 0x80 && bank <= 0xBF && offset < 0x2000)
            {
                return new NwaAddressMapping("WRAM", (ulong)offset);
            }

            if (mLayout == SnesRomLayout.LoROM)
                return MapLoROM(bank, offset);
            else
                return MapHiROM(bank, offset);
        }

        static NwaAddressMapping? MapLoROM(int bank, int offset)
        {
            // LoROM: ROM is mapped in 32KB chunks at $8000-$FFFF per bank
            // Banks $00-$7D and $80-$FF (upper half)
            if (offset >= 0x8000)
            {
                int effectiveBank = bank;
                // Mirror: $80-$FF maps the same ROM as $00-$7F
                if (effectiveBank >= 0x80)
                    effectiveBank -= 0x80;

                // Banks $7E-$7F are WRAM, already handled above
                if (effectiveBank <= 0x7D)
                {
                    ulong romOffset = (ulong)effectiveBank * 0x8000 + (ulong)(offset - 0x8000);
                    return new NwaAddressMapping("CARTROM", romOffset);
                }
            }

            // LoROM SRAM: banks $70-$7D, offsets $0000-$7FFF
            if (bank >= 0x70 && bank <= 0x7D && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0x70) * 0x8000 + (ulong)offset;
                return new NwaAddressMapping("CARTRAM", sramOffset);
            }

            // LoROM SRAM mirror: banks $F0-$FF, offsets $0000-$7FFF
            if (bank >= 0xF0 && bank <= 0xFF && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0xF0) * 0x8000 + (ulong)offset;
                return new NwaAddressMapping("CARTRAM", sramOffset);
            }

            // Unmappable (hardware registers, open bus, etc.)
            return null;
        }

        static NwaAddressMapping? MapHiROM(int bank, int offset)
        {
            // HiROM: ROM is mapped in 64KB chunks
            // Banks $C0-$FF: full 64KB per bank
            if (bank >= 0xC0 && bank <= 0xFF)
            {
                ulong romOffset = (ulong)(bank - 0xC0) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // Banks $40-$7D: full 64KB per bank (same ROM, different mirror)
            if (bank >= 0x40 && bank <= 0x7D)
            {
                ulong romOffset = (ulong)(bank - 0x40) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // Banks $00-$3F, upper half $8000-$FFFF: ROM mirror
            if (bank >= 0x00 && bank <= 0x3F && offset >= 0x8000)
            {
                ulong romOffset = (ulong)bank * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // Banks $80-$BF, upper half $8000-$FFFF: ROM mirror
            if (bank >= 0x80 && bank <= 0xBF && offset >= 0x8000)
            {
                ulong romOffset = (ulong)(bank - 0x80) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // HiROM SRAM: banks $20-$3F, offsets $6000-$7FFF
            if (bank >= 0x20 && bank <= 0x3F && offset >= 0x6000 && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0x20) * 0x2000 + (ulong)(offset - 0x6000);
                return new NwaAddressMapping("CARTRAM", sramOffset);
            }

            // HiROM SRAM mirror: banks $A0-$BF, offsets $6000-$7FFF
            if (bank >= 0xA0 && bank <= 0xBF && offset >= 0x6000 && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0xA0) * 0x2000 + (ulong)(offset - 0x6000);
                return new NwaAddressMapping("CARTRAM", sramOffset);
            }

            // Unmappable (hardware registers, open bus, etc.)
            return null;
        }
    }
}
