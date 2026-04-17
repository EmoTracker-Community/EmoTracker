using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA.AddressMaps
{
    /// <summary>
    /// Maps platform bus addresses to NWA memory domain + offset pairs.
    /// Each platform exposes different memory domains via NWA (e.g., WRAM, CARTROM, CARTRAM),
    /// and autotracker scripts use bus addresses. This interface bridges the two.
    /// </summary>
    internal interface INwaAddressMap
    {
        /// <summary>
        /// Perform any platform-specific initialization (e.g., reading ROM headers to detect
        /// memory mapping). Called once during device connection.
        /// </summary>
        /// <param name="readDomain">
        /// Raw NWA domain read function: (domainName, offset, length) → data bytes.
        /// Bypasses the address map so implementations can read from specific domains directly.
        /// </param>
        Task InitializeAsync(NwaRawDomainReader readDomain);

        /// <summary>
        /// Map a bus address to an NWA memory domain and offset within that domain.
        /// Returns null if the address cannot be mapped (e.g., hardware registers).
        /// </summary>
        NwaAddressMapping? MapAddress(ulong busAddress);
    }

    internal struct NwaAddressMapping
    {
        public string Domain;
        public ulong Offset;

        public NwaAddressMapping(string domain, ulong offset)
        {
            Domain = domain;
            Offset = offset;
        }
    }

    /// <summary>
    /// Delegate for reading raw bytes from a named NWA memory domain, bypassing address mapping.
    /// Used during address map initialization (e.g., reading ROM headers).
    /// </summary>
    internal delegate Task<byte[]> NwaRawDomainReader(string domain, ulong offset, int length);
}
