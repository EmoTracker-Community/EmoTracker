using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA.AddressMaps
{
    /// <summary>
    /// Fallback address map for platforms without a specialized implementation.
    /// Uses "System Bus" as the domain with identity offset mapping, which works for
    /// emulators that expose a flat system bus domain via NWA.
    /// </summary>
    internal class DefaultAddressMap : INwaAddressMap
    {
        readonly string mDomain;

        public DefaultAddressMap(string domain = "System Bus")
        {
            mDomain = domain;
        }

        public Task InitializeAsync(NwaRawDomainReader readDomain)
        {
            return Task.CompletedTask;
        }

        public NwaAddressMapping? MapAddress(ulong busAddress)
        {
            return new NwaAddressMapping(mDomain, busAddress);
        }
    }
}
