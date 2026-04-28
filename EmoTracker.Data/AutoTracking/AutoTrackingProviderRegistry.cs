using EmoTracker.Core;
using EmoTracker.Data.Packages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Data.AutoTracking
{
    public class AutoTrackingProviderRegistry : ObservableSingleton<AutoTrackingProviderRegistry>
    {
        /// <summary>
        /// App-wide singleton instances of every <see cref="IAutoTrackingProvider"/>
        /// implementation discovered by reflection. Read-only metadata
        /// surface (display name, UID, supported platforms) — these
        /// instances are NOT used for live connections, since multiple
        /// per-state AutoTracker extensions cannot share a singleton's
        /// connection state. Use <see cref="GetProvidersForPack"/> for
        /// per-state runtime instances.
        /// </summary>
        public IEnumerable<IAutoTrackingProvider> Providers => TypedObjectRegistry<IAutoTrackingProvider>.SupportRegistry;

        public IAutoTrackingProvider FindByUID(string uid)
        {
            return Providers.FirstOrDefault(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns FRESH provider instances suitable for the given pack
        /// (filtered by manifest's <c>AutoTrackerProviders</c> list, or
        /// platform compatibility when no manifest list is supplied).
        /// Each call mints a new instance per matched provider type — the
        /// caller (per-state <c>AutoTrackerExtension</c>) takes ownership
        /// and is responsible for disposing them when its state detaches.
        ///
        /// <para>
        /// Per-state instances are required because connection state +
        /// the <c>ConnectionStatusChanged</c> / <c>AvailableDevicesChanged</c>
        /// event subscriptions are intrinsically per-instance: with a
        /// shared singleton, disconnecting one state's AT would fire
        /// ConnectionStatusChanged on every other state's AT subscribed
        /// to the same singleton, flipping their <c>Connected</c> flag
        /// to false and turning their status indicators yellow.
        /// </para>
        /// </summary>
        public IReadOnlyList<IAutoTrackingProvider> GetProvidersForPack(IGamePackage pack)
        {
            if (pack == null)
                return Array.Empty<IAutoTrackingProvider>();

            // The registry's singleton instances are used here only as
            // metadata templates — we read UID / SupportedPlatforms off
            // them, then instantiate fresh copies of the matching types.
            var manifestProviders = pack.AutoTrackerProviders;
            IEnumerable<IAutoTrackingProvider> templates;
            if (manifestProviders != null && manifestProviders.Count > 0)
            {
                templates = Providers
                    .Where(p => manifestProviders.Any(uid => string.Equals(uid, p.UID, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                templates = Providers
                    .Where(p => p.SupportedPlatforms.Contains(pack.Platform));
            }

            var result = new List<IAutoTrackingProvider>();
            foreach (var template in templates)
            {
                try
                {
                    var inst = Activator.CreateInstance(template.GetType()) as IAutoTrackingProvider;
                    if (inst != null)
                        result.Add(inst);
                }
                catch
                {
                    // Defensive: a faulty provider ctor shouldn't strand
                    // the rest of the per-pack provider list.
                }
            }
            return result;
        }
    }
}
