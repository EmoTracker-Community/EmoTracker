using EmoTracker.Core;
using EmoTracker.Data.Packages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EmoTracker.Data.AutoTracking
{
    public class AutoTrackingProviderRegistry : ObservableSingleton<AutoTrackingProviderRegistry>
    {
        readonly List<IAutoTrackingProvider> mProviders = new List<IAutoTrackingProvider>();

        public IReadOnlyList<IAutoTrackingProvider> Providers => mProviders;

        public void DiscoverProviders()
        {
            mProviders.Clear();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface)
                            continue;

                        if (!typeof(IAutoTrackingProvider).IsAssignableFrom(type))
                            continue;

                        if (!type.IsDefined(typeof(AutoTrackingProviderAttribute), false))
                            continue;

                        try
                        {
                            IAutoTrackingProvider provider = (IAutoTrackingProvider)Activator.CreateInstance(type);
                            mProviders.Add(provider);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public IAutoTrackingProvider FindByUID(string uid)
        {
            return mProviders.FirstOrDefault(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<IAutoTrackingProvider> GetProvidersForPack(IGamePackage pack)
        {
            if (pack == null)
                return Array.Empty<IAutoTrackingProvider>();

            var manifestProviders = pack.AutoTrackerProviders;
            if (manifestProviders != null && manifestProviders.Count > 0)
            {
                return mProviders
                    .Where(p => manifestProviders.Any(uid => string.Equals(uid, p.UID, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return mProviders
                .Where(p => p.SupportedPlatforms.Contains(pack.Platform))
                .ToList();
        }
    }
}
