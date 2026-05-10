using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 7.1: stateless static helper that loads a pack into a *target*
    /// <see cref="TrackerState"/>, populating its catalogs (Items / Locations /
    /// Maps / Layouts / Scripts) directly. Replaces the Phase 6 path where
    /// <c>Tracker.Reload</c> mutated shared singleton catalogs and
    /// <c>ApplicationModel.RebindActivePackageInstanceFromSingletons</c>
    /// adopted them post-hoc.
    ///
    /// <para>
    /// The orchestration sequence (matches the pre-7.1 <c>Tracker.Reload</c>
    /// body, with every singleton access redirected at the target):
    /// <list type="number">
    ///   <item>Fire <see cref="OnPackageLoadStarting"/>.</item>
    ///   <item>Clear <see cref="AccessibilityRule"/>'s static cache (still
    ///         static through 7.1; per-state in 7.2).</item>
    ///   <item>Reset target catalogs in dependency order (Layouts → Maps →
    ///         Locations → Items → Scripts).</item>
    ///   <item>Refresh <see cref="ApplicationColors"/>.</item>
    ///   <item>If a package is supplied:
    ///         load <c>settings.json</c> (or legacy <c>tracker_layout.json</c>),
    ///         bootstrap the per-state Lua interpreter via
    ///         <see cref="ScriptManager.Load"/>, then run the catalog
    ///         <c>LegacyLoad</c>s (Items → Maps → Locations).</item>
    ///   <item>Clear the rule cache again (post-load), build the item code
    ///         index, stamp <see cref="ModelTypeBase.OwnerState"/> + register
    ///         every model in <c>target.Resolver</c>.</item>
    ///   <item>Fire <see cref="OnPackageLoadComplete"/>.</item>
    ///   <item>Invoke the <c>PackReady</c> standard callback (unless
    ///         <paramref name="suspendPackReadyEvent"/> is true).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Re-entrancy: a single <c>mInProgress</c> flag prevents nested
    /// invocations on the same thread (matches Tracker's
    /// <c>mbReloadInProgress</c>). Cross-thread reentrancy is not supported
    /// — pack-load runs on the UI thread.
    /// </para>
    /// </summary>
    public static class PackageLoader
    {
        public sealed class PackageLoadEventArgs : EventArgs
        {
            public TrackerState Target { get; }
            public IGamePackage Package { get; }
            public IGamePackageVariant Variant { get; }

            public PackageLoadEventArgs(TrackerState target, IGamePackage package, IGamePackageVariant variant)
            {
                Target = target;
                Package = package;
                Variant = variant;
            }
        }

        /// <summary>Fires before any reset / load work, after re-entrancy guard.</summary>
        public static event EventHandler<PackageLoadEventArgs> OnPackageLoadStarting;

        /// <summary>Fires after all load work + index build + resolver stamping, before <c>PackReady</c>.</summary>
        public static event EventHandler<PackageLoadEventArgs> OnPackageLoadComplete;

        [ThreadStatic]
        static bool mInProgress;

        /// <summary>
        /// True while <see cref="LoadInto"/> is executing on this thread.
        /// Used by <see cref="ApplicationSettings.SyncSeedsFromSession"/> to
        /// suppress seed-writes driven by pack-script (init.lua) changes,
        /// so only explicit user UI changes update the saved defaults.
        /// </summary>
        internal static bool IsLoading => mInProgress;

        /// <summary>
        /// Loads <paramref name="package"/> (with optional <paramref name="variant"/>)
        /// into <paramref name="target"/>'s catalogs. Caller is responsible for
        /// having the <c>target</c>'s catalogs already wired (the
        /// <see cref="TrackerState"/> ctor does this) — PackageLoader does not
        /// allocate catalogs.
        ///
        /// <para>
        /// If <paramref name="package"/> is null, the target's catalogs are
        /// reset and left empty (used for "unload pack" scenarios).
        /// </para>
        /// </summary>
        public static void LoadInto(
            TrackerState target,
            IGamePackage package,
            IGamePackageVariant variant,
            bool suspendPackReadyEvent = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (mInProgress)
                return;
            mInProgress = true;

            var evtArgs = new PackageLoadEventArgs(target, package, variant);

            try
            {
                target.Locations.PushSuspendRefresh();

                // Phase 7.1.h: pack metadata lives on the target's
                // PackageInstance back-ref. The caller (TrackerState.
                // ActivatePackage / LoadProgress / Reload) is responsible
                // for setting up the PackageInstance to match the
                // (package, variant) being loaded BEFORE invoking LoadInto.

                // Phase 7.2: per-state rule cache clear (replaces static AccessibilityRule.ClearCaches).
                target.Locations.RuleCache.Clear();

                OnPackageLoadStarting?.Invoke(null, evtArgs);

                // Phase 7.1.g: reset pack-driven UI flags on the target state
                // so the pack's settings.json values overwrite stale ones.
                ResetPackageSettings(target);

                target.Layouts.Clear();
                target.Maps.Reset();
                target.Locations.Reset();
                target.Items.Reset();
                target.Scripts.Reset();

                // Flush the PackageInstance's decoded-image caches. Old
                // ImageReference keys from the now-reset catalogs would
                // otherwise keep both the source SKBitmaps and the resolved
                // IImages alive across every Reload, accumulating ~250 MB per
                // reload for large packs.
                FlushImageCaches(target.PackageInstance);

                ApplicationColors.Instance.LoadColors();

                if (package != null)
                {
                    target.Scripts.Output("Beginning Package Load");
                    using (new LoggingBlock(target.Scripts))
                    {
                        target.Scripts.Output(string.Format("Package: {0}", package.UniqueID));
                        if (variant != null)
                            target.Scripts.Output(string.Format("Variant: {0}", variant.UniqueID));

                        LoadPackageSettings(target, package);

                        target.Scripts.Load(package);

                        // Legacy loads — should this be contingent on a flag in the manifest?
                        target.Items.LegacyLoad(package, target);
                        target.Maps.LegacyLoad(package, target);
                        target.Locations.LegacyLoad(package, target);
                    }
                    target.Scripts.Output("Package Load Finished");
                }
            }
            finally
            {
                // Defensive: each pre-Pop step is wrapped so a throw inside
                // RuleCache.Clear() or BuildCodeIndex() cannot strand the
                // suspend count above zero (which would leave the
                // LocationDatabase permanently suspended and cause the
                // accessibility-not-updating symptom we hunted earlier).
                try { target.Locations.RuleCache.Clear(); }
                catch (Exception e) { target.Scripts?.OutputException(e); }

                try { target.Items.BuildCodeIndex(); }
                catch (Exception e) { target.Scripts?.OutputException(e); }

                target.Locations.PopSuspendRefresh();

                mInProgress = false;

                OnPackageLoadComplete?.Invoke(null, evtArgs);

                if (!suspendPackReadyEvent)
                {
                    ((IScriptManager)target.Scripts)?.InvokeStandardCallback(StandardCallback.PackReady);

                    // Phase 7.1.h: pack scripts (e.g. CodeTracker) commonly
                    // gate their tracker_on_accessibility_updated body on a
                    // `STATUS.TRACKER_READY` flag that they only set to true
                    // inside tracker_on_pack_ready. PopSuspendRefresh's
                    // earlier accessibility cascade fires AccessibilityUpdated
                    // BEFORE PackReady, so the pack's gate-check returns
                    // false and the cascade evaluates against pre-pack-ready
                    // Lua state — leaving section.mCachedAccessibility stuck
                    // at None for everything pack-script-derived. Trigger
                    // ONE more refresh now that PackReady has fired so the
                    // callback runs with TRACKER_READY = true and pack-side
                    // accessibility logic gets to actually populate values.
                    try
                    {
                        target.Locations.RefreshAccessibility();
                    }
                    catch (Exception e)
                    {
                        target.Scripts?.OutputException(e);
                    }
                }
            }
        }

        // Dispose and clear both image caches on a PackageInstance. Called
        // before each load so stale SKBitmaps and resolved IImages from the
        // previous load are released rather than accumulated in memory.
        static void FlushImageCaches(PackageInstance pi)
        {
            if (pi == null) return;
            lock (pi.SourceImageCacheLock)
            {
                foreach (var kvp in pi.SourceImageCache)
                    (kvp.Value as IDisposable)?.Dispose();
                pi.SourceImageCache.Clear();
            }
            pi.ImageCache.Clear();
        }

        // Phase 7.1.g: pack-driven UI flags now live on the per-state
        // TrackerState. Reset them at the start of each load to defaults
        // so a pack reload picks up the pack's settings.json fresh.
        static void ResetPackageSettings(TrackerState target)
        {
            target.AllowResize = true;
            target.DisabledImageFilterSpec = TrackerDefaults.DisabledImageFilterSpec;
        }

        // Mirrors Tracker.LoadPackageSettings, but operates on the target's
        // LocationDatabase (not the singleton).
        static void LoadPackageSettings(TrackerState target, IGamePackage package)
        {
            if (package == null) return;

            bool bLoadedSettings = false;
            using (Stream s = package.Open("settings.json", target.PackageInstance?.ActiveVariant))
            {
                if (s != null)
                {
                    target.Scripts.Output("Loading package settings");
                    using (new LoggingBlock(target.Scripts))
                    {
                        try
                        {
                            using (StreamReader reader = new StreamReader(s))
                            {
                                JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                                target.AllowResize = root.GetValue<bool>("allow_resize", true);

                                string spec = root.GetValue<string>("disabled_image_filter", null);
                                if (spec != null)
                                    target.DisabledImageFilterSpec = spec;

                                target.Locations.ParseLocationVisualProperties(root, target.Locations.Root, package);

                                AccessibilityRule.EnableCache = root.GetValue<bool>("enable_accessibility_rule_caching", true);

                                bLoadedSettings = true;
                            }
                        }
                        catch (Exception e)
                        {
                            target.Scripts.OutputException(e);
                        }
                    }
                }
            }

            if (!bLoadedSettings)
            {
                target.Scripts.OutputWarning("Loading legacy package settings from tracker_layout.json");
                using (new LoggingBlock(target.Scripts))
                {
                    try
                    {
                        using (Stream s = package.Open("tracker_layout.json", target.PackageInstance?.ActiveVariant))
                        {
                            if (s == null) return;
                            using (StreamReader reader = new StreamReader(s))
                            {
                                JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                                target.AllowResize = root.GetValue<bool>("allow_resize", true);
                                string spec = root.GetValue<string>("disabled_image_filter", null);
                                if (spec != null)
                                    target.DisabledImageFilterSpec = spec;

                                target.Locations.ParseLocationVisualProperties(root, target.Locations.Root, package);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        target.Scripts.OutputException(e);
                    }
                }
            }
        }
    }

    // Constants previously private to Tracker; lifted to a tiny helper so
    // PackageLoader doesn't duplicate the literal in two places.
    static class TrackerDefaults
    {
        public const string DisabledImageFilterSpec = "grayscale, dim";
    }
}
