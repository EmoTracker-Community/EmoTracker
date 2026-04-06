using EmoTracker.Core;
using EmoTracker.Data.Packages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Data.AutoTracking
{
    public class AutoTrackingProviderRegistry : ObservableSingleton<AutoTrackingProviderRegistry>
    {
        public IEnumerable<IAutoTrackingProvider> Providers => TypedObjectRegistry<IAutoTrackingProvider>.SupportRegistry;

        public IAutoTrackingProvider FindByUID(string uid)
        {
            return Providers.FirstOrDefault(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<IAutoTrackingProvider> GetProvidersForPack(IGamePackage pack)
        {
            if (pack == null)
                return Array.Empty<IAutoTrackingProvider>();

            var manifestProviders = pack.AutoTrackerProviders;
            if (manifestProviders != null && manifestProviders.Count > 0)
            {
                return Providers
                    .Where(p => manifestProviders.Any(uid => string.Equals(uid, p.UID, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return Providers
                .Where(p => p.SupportedPlatforms.Contains(pack.Platform))
                .ToList();
        }
    }
}
