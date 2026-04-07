using Serilog;
using System;
using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA.AddressMaps
{
    /// <summary>
    /// Maps SNES 24-bit bus addresses to BSNES NWA memory domains (WRAM, CARTROM, CARTRAM).
    /// Detects LoROM/HiROM/ExLoROM/ExHiROM by reading the ROM header from the CARTROM domain
    /// during initialization.
    /// </summary>
    internal class SnesAddressMap : INwaAddressMap
    {
        internal enum SnesRomLayout
        {
            LoROM,
            HiROM,
            ExLoROM,
            ExHiROM
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
            // The SNES internal ROM header is 0x50 bytes starting at $xxB0.
            // LoROM/ExLoROM header: CARTROM offset $7FB0
            // HiROM/ExHiROM header: CARTROM offset $FFB0
            // ExHiROM also has a header at CARTROM offset $40FFB0 (upper 4MB half)
            //
            // The map mode byte at relative offset 0x25 ($xxD5) encodes:
            //   $20 = LoROM           $21 = HiROM
            //   $30 = LoROM+FastROM   $31 = HiROM+FastROM
            //   $32 = ExLoROM         $35 = ExHiROM
            try
            {
                byte[] loHeader = null;
                byte[] hiHeader = null;
                byte[] exHiHeader = null;

                try { loHeader = await readDomain("CARTROM", 0x7FB0, 0x50).ConfigureAwait(false); }
                catch { }

                try { hiHeader = await readDomain("CARTROM", 0xFFB0, 0x50).ConfigureAwait(false); }
                catch { }

                // ExHiROM: header at $40FFB0 in the extended ROM area
                try { exHiHeader = await readDomain("CARTROM", 0x40FFB0, 0x50).ConfigureAwait(false); }
                catch { }

                int loScore = 0, hiScore = 0, exLoScore = 0, exHiScore = 0;

                if (loHeader != null && loHeader.Length >= 0x50)
                {
                    loScore = ScoreHeader(loHeader, 0x20);    // LoROM
                    exLoScore = ScoreHeader(loHeader, 0x32);   // ExLoROM
                }

                if (hiHeader != null && hiHeader.Length >= 0x50)
                {
                    hiScore = ScoreHeader(hiHeader, 0x21);    // HiROM
                    exHiScore = ScoreHeader(hiHeader, 0x35);   // ExHiROM
                }

                // ExHiROM header in extended area gets extra weight if valid
                if (exHiHeader != null && exHiHeader.Length >= 0x50)
                {
                    int extScore = ScoreHeader(exHiHeader, 0x35);
                    if (extScore > exHiScore)
                        exHiScore = extScore;
                }

                Log.Debug("[NWA/SNES] Header scores — LoROM: {Lo}, HiROM: {Hi}, ExLoROM: {ExLo}, ExHiROM: {ExHi}",
                    loScore, hiScore, exLoScore, exHiScore);

                // Pick the highest-scoring layout
                int bestScore = loScore;
                var bestLayout = SnesRomLayout.LoROM;

                if (hiScore > bestScore) { bestScore = hiScore; bestLayout = SnesRomLayout.HiROM; }
                if (exLoScore > bestScore) { bestScore = exLoScore; bestLayout = SnesRomLayout.ExLoROM; }
                if (exHiScore > bestScore) { bestScore = exHiScore; bestLayout = SnesRomLayout.ExHiROM; }

                return bestLayout;
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
        /// <param name="header">0x50 bytes starting from the header base address.</param>
        /// <param name="expectedMapMode">The map mode byte value to match (masking out FastROM bit 4).</param>
        static int ScoreHeader(byte[] header, byte expectedMapMode)
        {
            int score = 0;

            // Map mode byte at offset 0x25 (= $xxD5 - $xxB0)
            byte mapMode = header[0x25];

            // Match the expected map mode, ignoring the FastROM bit (bit 4)
            // e.g. $20 and $30 are both LoROM; $21 and $31 are both HiROM
            if ((mapMode & ~0x10) == (expectedMapMode & ~0x10))
                score += 5;
            else if ((mapMode & 0x0F) == (expectedMapMode & 0x0F))
                score += 2; // At least the low nibble matches

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

            // WRAM mirror: banks $00-$3F and $80-$BF, offsets $0000-$1FFF (low 8KB of WRAM)
            if ((bank <= 0x3F || (bank >= 0x80 && bank <= 0xBF)) && offset < 0x2000)
            {
                return new NwaAddressMapping("WRAM", (ulong)offset);
            }

            switch (mLayout)
            {
                case SnesRomLayout.LoROM:   return MapLoROM(bank, offset);
                case SnesRomLayout.HiROM:   return MapHiROM(bank, offset);
                case SnesRomLayout.ExLoROM: return MapExLoROM(bank, offset);
                case SnesRomLayout.ExHiROM: return MapExHiROM(bank, offset);
                default:                    return MapLoROM(bank, offset);
            }
        }

        // --- LoROM (up to 4MB ROM) ---

        static NwaAddressMapping? MapLoROM(int bank, int offset)
        {
            // ROM: banks $00-$7D / $80-$FF, upper half $8000-$FFFF, 32KB per bank
            if (offset >= 0x8000)
            {
                int effectiveBank = bank >= 0x80 ? bank - 0x80 : bank;
                if (effectiveBank <= 0x7D)
                {
                    ulong romOffset = (ulong)effectiveBank * 0x8000 + (ulong)(offset - 0x8000);
                    return new NwaAddressMapping("CARTROM", romOffset);
                }
            }

            // SRAM: banks $70-$7D, offsets $0000-$7FFF
            if (bank >= 0x70 && bank <= 0x7D && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0x70) * 0x8000 + (ulong)offset;
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            // SRAM mirror: banks $F0-$FF, offsets $0000-$7FFF
            if (bank >= 0xF0 && bank <= 0xFF && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0xF0) * 0x8000 + (ulong)offset;
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            return null;
        }

        // --- HiROM (up to 4MB ROM) ---

        static NwaAddressMapping? MapHiROM(int bank, int offset)
        {
            // ROM: banks $C0-$FF, full 64KB per bank
            if (bank >= 0xC0 && bank <= 0xFF)
            {
                ulong romOffset = (ulong)(bank - 0xC0) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // ROM: banks $40-$7D, full 64KB per bank
            if (bank >= 0x40 && bank <= 0x7D)
            {
                ulong romOffset = (ulong)(bank - 0x40) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // ROM mirror: banks $00-$3F, upper half $8000-$FFFF
            if (bank >= 0x00 && bank <= 0x3F && offset >= 0x8000)
            {
                ulong romOffset = (ulong)bank * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // ROM mirror: banks $80-$BF, upper half $8000-$FFFF
            if (bank >= 0x80 && bank <= 0xBF && offset >= 0x8000)
            {
                ulong romOffset = (ulong)(bank - 0x80) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // SRAM: banks $20-$3F, offsets $6000-$7FFF
            if (bank >= 0x20 && bank <= 0x3F && offset >= 0x6000 && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0x20) * 0x2000 + (ulong)(offset - 0x6000);
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            // SRAM mirror: banks $A0-$BF, offsets $6000-$7FFF
            if (bank >= 0xA0 && bank <= 0xBF && offset >= 0x6000 && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0xA0) * 0x2000 + (ulong)(offset - 0x6000);
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            return null;
        }

        // --- ExLoROM (up to 8MB ROM) ---
        // Extends LoROM by using banks $80-$FF for the upper 4MB of ROM.
        // Banks $00-$7D upper half map to ROM offset $000000-$3EFFFF (lower 4MB).
        // Banks $80-$FF upper half map to ROM offset $400000-$7FFFFF (upper 4MB).

        static NwaAddressMapping? MapExLoROM(int bank, int offset)
        {
            if (offset >= 0x8000)
            {
                if (bank >= 0x80 && bank <= 0xFF)
                {
                    // Upper 4MB: banks $80-$FF map to ROM $400000+
                    int effectiveBank = bank - 0x80;
                    ulong romOffset = 0x400000 + (ulong)effectiveBank * 0x8000 + (ulong)(offset - 0x8000);
                    return new NwaAddressMapping("CARTROM", romOffset);
                }

                if (bank <= 0x7D)
                {
                    // Lower 4MB: banks $00-$7D
                    ulong romOffset = (ulong)bank * 0x8000 + (ulong)(offset - 0x8000);
                    return new NwaAddressMapping("CARTROM", romOffset);
                }
            }

            // SRAM: same as LoROM — banks $70-$7D, offsets $0000-$7FFF
            if (bank >= 0x70 && bank <= 0x7D && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0x70) * 0x8000 + (ulong)offset;
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            // SRAM mirror: banks $F0-$FF, offsets $0000-$7FFF
            if (bank >= 0xF0 && bank <= 0xFF && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0xF0) * 0x8000 + (ulong)offset;
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            return null;
        }

        // --- ExHiROM (up to 8MB ROM) ---
        // Extends HiROM by using banks $00-$3F for the upper 4MB of ROM.
        // Banks $C0-$FF / $40-$7D map to ROM $000000-$3FFFFF (lower 4MB).
        // Banks $00-$3F upper half ($8000-$FFFF) and $80-$BF upper half map to ROM $400000+ (upper 4MB).

        static NwaAddressMapping? MapExHiROM(int bank, int offset)
        {
            // Lower 4MB: banks $C0-$FF, full 64KB per bank
            if (bank >= 0xC0 && bank <= 0xFF)
            {
                ulong romOffset = (ulong)(bank - 0xC0) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // Lower 4MB: banks $40-$7D, full 64KB per bank
            if (bank >= 0x40 && bank <= 0x7D)
            {
                ulong romOffset = (ulong)(bank - 0x40) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // Upper 4MB: banks $00-$3F, upper half $8000-$FFFF
            if (bank >= 0x00 && bank <= 0x3F && offset >= 0x8000)
            {
                ulong romOffset = 0x400000 + (ulong)bank * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // Upper 4MB: banks $80-$BF, upper half $8000-$FFFF
            if (bank >= 0x80 && bank <= 0xBF && offset >= 0x8000)
            {
                ulong romOffset = 0x400000 + (ulong)(bank - 0x80) * 0x10000 + (ulong)offset;
                return new NwaAddressMapping("CARTROM", romOffset);
            }

            // SRAM: banks $20-$3F, offsets $6000-$7FFF (same as HiROM)
            if (bank >= 0x20 && bank <= 0x3F && offset >= 0x6000 && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0x20) * 0x2000 + (ulong)(offset - 0x6000);
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            // SRAM mirror: banks $A0-$BF, offsets $6000-$7FFF
            if (bank >= 0xA0 && bank <= 0xBF && offset >= 0x6000 && offset < 0x8000)
            {
                ulong sramOffset = (ulong)(bank - 0xA0) * 0x2000 + (ulong)(offset - 0x6000);
                return new NwaAddressMapping("SRAM", sramOffset);
            }

            return null;
        }
    }
}
