using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Packages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 7.1.g/h: per-state pack-management operations. Pack identity
    /// (<see cref="Sessions.PackageInstance.GamePackage"/> /
    /// <see cref="Sessions.PackageInstance.ActiveVariant"/>) lives on the
    /// back-referenced <see cref="PackageInstance"/>; activating a
    /// different pack or variant swaps this state's PackageInstance to a
    /// new one.
    /// </summary>
    public sealed partial class TrackerState
    {
        // ---- Pack metadata helpers --------------------------------------

        /// <summary>
        /// The currently active variant's <c>UniqueID</c>, or the literal
        /// string <c>"none"</c> when no variant is active. Mirrors
        /// legacy save-file compatibility semantics.
        /// </summary>
        public string ActiveVariantUID =>
            PackageInstance?.ActiveVariant != null ? PackageInstance.ActiveVariant.UniqueID : "none";

        /// <summary>
        /// Returns true iff this state's active package's
        /// <c>UniqueID</c> matches <paramref name="uniqueId"/>.
        /// </summary>
        public bool IsActivePackage(string uniqueId)
        {
            var pkg = PackageInstance?.GamePackage;
            if (pkg == null) return false;
            return string.Equals(pkg.UniqueID, uniqueId, StringComparison.Ordinal);
        }

        // ---- Per-state pack-driven settings -----------------------------
        // These were on Tracker; pack init.lua (and pack settings.json)
        // mutate them per-load. They're per-state because each state
        // loaded with a different pack carries its own values.

        string mDisabledImageFilterSpec = "grayscale, dim";
        /// <summary>
        /// Image filter spec applied to "disabled" / unowned items. Set by
        /// <c>settings.json</c> / <c>tracker_layout.json</c>'s
        /// <c>disabled_image_filter</c> key during pack load; falls back
        /// to <c>"grayscale, dim"</c>.
        /// </summary>
        public string DisabledImageFilterSpec
        {
            get => mDisabledImageFilterSpec;
            set { SetProperty(ref mDisabledImageFilterSpec, value); }
        }

        bool mAllowResize = true;
        /// <summary>
        /// Whether the host window is allowed to resize. Set by the pack's
        /// <c>settings.json</c> / <c>tracker_layout.json</c>'s
        /// <c>allow_resize</c> key during pack load.
        /// </summary>
        public bool AllowResize
        {
            get => mAllowResize;
            set { SetProperty(ref mAllowResize, value); }
        }

        // ---- Reload / Activate ------------------------------------------

        bool mbReloadInProgress;

        /// <summary>
        /// Re-runs <see cref="PackageLoader.LoadInto"/> against this state
        /// using its current <see cref="PackageInstance"/>'s pack/variant.
        /// No-op if a reload is already in progress on this state.
        /// </summary>
        public void Reload()
        {
            if (mbReloadInProgress) return;
            mbReloadInProgress = true;
            try
            {
                var pi = PackageInstance;
                PackageLoader.LoadInto(this, pi?.GamePackage, pi?.ActiveVariant);
            }
            finally
            {
                mbReloadInProgress = false;
            }
        }

        /// <summary>
        /// Switches this state to a new <see cref="PackageInstance"/>
        /// constructed against the given <paramref name="package"/> and
        /// <paramref name="variant"/>, then runs
        /// <see cref="PackageLoader.LoadInto"/>. Pass null
        /// <paramref name="variant"/> when the pack has no chosen variant.
        /// </summary>
        public void ActivatePackage(IGamePackage package, IGamePackageVariant variant)
        {
            if (mbReloadInProgress) return;
            mbReloadInProgress = true;
            try
            {
                // Validate variant belongs to package, falling back to
                // none if the requested one isn't valid for the pack.
                if (package != null && variant != null
                    && !package.AvailableVariants.Contains(variant))
                {
                    variant = null;
                }

                if (package != null)
                {
                    ApplicationSettings.Instance.LastActivePackage = package.UniqueID;
                    ApplicationSettings.Instance.LastActivePackageVariant = variant?.UniqueID;
                }
                else
                {
                    ApplicationSettings.Instance.LastActivePackageVariant = null;
                }

                // Swap in a new PackageInstance for the new (pack, variant)
                // identity. The old PackageInstance is left to its existing
                // owner (typically ApplicationModel).
                PackageInstance = new PackageInstance(package, variant);

                PackageManager.Instance.RefreshActiveState();
                NotifyPropertyChanged(nameof(ActiveVariantUID));

                // Reset tracking settings to the user's saved defaults before
                // running init.lua, so pack scripts start from the user's
                // preferences and may optionally override them (issue #83).
                ApplicationSettings.Instance.SeedIntoSession(Settings);

                PackageLoader.LoadInto(this, package, variant);
            }
            finally
            {
                mbReloadInProgress = false;
            }
        }

        /// <summary>
        /// Swaps this state's <see cref="PackageInstance"/> to a fresh one
        /// for the given (pack, variant) without triggering a reload.
        /// Used by cross-PackageInstance tab switches that want pack
        /// metadata to track the active state without re-running the load
        /// (the destination pack is already loaded elsewhere).
        /// </summary>
        public void UpdatePackageInfoWithoutReload(IGamePackage package, IGamePackageVariant variant)
        {
            PackageInstance = new PackageInstance(package, variant);
            NotifyPropertyChanged(nameof(ActiveVariantUID));
        }

        // ---- Save / Load -----------------------------------------------

        /// <summary>
        /// Save this state's progress to <paramref name="path"/> as JSON.
        /// </summary>
        /// <param name="dataAction">
        /// Optional callback invoked after items / locations are written;
        /// lets the caller add additional keys (extension data, window
        /// metrics) before serialization.
        /// </param>
        public bool SaveProgress(string path, Action<JObject> dataAction = null)
        {
            var root = SaveProgressToJObject(dataAction);
            if (root == null) return false;

            try
            {
                using (TextWriter textWriter = new StreamWriter(path))
                using (JsonTextWriter writer = new JsonTextWriter(textWriter))
                {
                    writer.Formatting = Formatting.Indented;
                    root.WriteTo(writer);
                }
                return true;
            }
            catch
            {
                if (Scripts?.NotificationService != null)
                {
                    Scripts.NotificationService.PushMarkdownNotification(Scripting.NotificationType.Error,
@"### Couldn't Save Progress

An error occurred while saving. This may be due to anti-virus/malware software protecting your chosen save directory over-aggressively.
* Ensure you have write permissions for the directory you chose
* Saving to a different location may work");
                }
            }

            return false;
        }

        /// <summary>
        /// In-memory variant of <see cref="SaveProgress"/> — builds and
        /// returns the JSON envelope for this state's progress without
        /// writing to disk. Used by the multi-state "Save All" path on
        /// <see cref="ApplicationModel"/> which collects per-state JObjects
        /// into a workspace envelope. Returns null when the state has no
        /// loaded pack (nothing meaningful to serialize).
        /// </summary>
        public JObject SaveProgressToJObject(Action<JObject> dataAction = null)
        {
            var pi = PackageInstance;
            var pkg = pi?.GamePackage;
            if (pkg == null) return null;

            JObject root = new JObject();
            root["package_uid"] = pkg.UniqueID;
            if (pi.ActiveVariant != null)
                root["package_variant_uid"] = pi.ActiveVariant.UniqueID;
            root["package_version"] = pkg.Version.ToString();
            root["creation_time"] = DateTime.Now.ToString();
            root["ignore_all_logic"] = Settings.IgnoreAllLogic;
            root["display_all_locations"] = Settings.DisplayAllLocations;
            root["always_allow_chest_manipulation"] = Settings.AlwaysAllowClearing;
            root["auto_unpin_locations_on_clear"] = Settings.AutoUnpinLocationsOnClear;
            root["pin_locations_on_item_capture"] = Settings.PinLocationsOnItemCapture;

            Items.Save(root);
            Locations.Save(root);

            dataAction?.Invoke(root);
            return root;
        }

        /// <summary>
        /// Load previously-saved progress from <paramref name="path"/> into
        /// this state. Swaps <see cref="PackageInstance"/> to match the
        /// save's (pack, variant) and re-runs the pack load before reading
        /// items / locations.
        /// </summary>
        public bool LoadProgress(string path, Action<JObject> dataAction = null)
        {
            try
            {
                Scripts?.Output("Loading save game \"{0}\"", path);
                ((global::EmoTracker.Core.DataModel.IScriptManager)Scripts)?.InvokeStandardCallback(StandardCallback.StartLoadingSaveFile);

                using (StreamReader reader = new StreamReader(path))
                {
                    JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));

                    string packageUID = root.GetValue<string>("package_uid");
                    string packageVariantUID = root.GetValue<string>("package_variant_uid");

                    Version packVersion;
                    Version.TryParse(root.GetValue<string>("package_version"), out packVersion);

                    IGamePackage package = PackageManager.Instance.FindInstalledPackage(packageUID);
                    if (package == null) return false;
                    if (package.Version != packVersion) return false;

                    IGamePackageVariant packageVariant = package.FindVariant(packageVariantUID);
                    if (!string.IsNullOrWhiteSpace(packageVariantUID) && packageVariant == null)
                        return false;

                    // Swap to a fresh PackageInstance for the save's
                    // (pack, variant), then drive the load. The PI's own
                    // ActiveVariant carries the per-tab variant identity;
                    // no global slot needs to be set.
                    PackageInstance = new PackageInstance(package, packageVariant);
                    PackageLoader.LoadInto(this, package, packageVariant, suspendPackReadyEvent: true);

                    if (!Items.Load(root)) return false;
                    if (!Locations.Load(root)) return false;

                    Settings.IgnoreAllLogic = root.GetValue<bool>("ignore_all_logic", false);
                    Settings.DisplayAllLocations = root.GetValue<bool>("display_all_locations", false);
                    Settings.AlwaysAllowClearing = root.GetValue<bool>("always_allow_chest_manipulation", false);
                    Settings.AutoUnpinLocationsOnClear = root.GetValue<bool>("auto_unpin_locations_on_clear", true);
                    Settings.PinLocationsOnItemCapture = root.GetValue<bool>("pin_locations_on_item_capture", true);

                    dataAction?.Invoke(root);
                }
                return true;
            }
            catch (Exception ex)
            {
                Scripts?.OutputError("Error encountered while loading save game");
                Scripts?.OutputException(ex);
            }
            finally
            {
                ((global::EmoTracker.Core.DataModel.IScriptManager)Scripts)?.InvokeStandardCallback(StandardCallback.FinishLoadingSaveFile);
                ((global::EmoTracker.Core.DataModel.IScriptManager)Scripts)?.InvokeStandardCallback(StandardCallback.PackReady);

                Scripts?.Output("Finished loading save game \"{0}\"", path);
            }

            return false;
        }

        /// <summary>
        /// Restores items / locations / settings from <paramref name="root"/>
        /// into this <i>already-forked</i> state, WITHOUT swapping the
        /// <see cref="PackageInstance"/> or re-running pack-load.
        ///
        /// <para>
        /// Used by the multi-state "Load All" path: the workspace JSON
        /// envelope contains pre-built per-state JObjects (one per saved
        /// tab); the loader forks each PI's <c>DefinitionalState</c> to
        /// produce a fresh primary, then layers the saved progress on
        /// top via this method. This is faster than calling
        /// <see cref="LoadProgress(string, Action{JObject})"/> for every
        /// tab (which would each re-parse the pack from disk) and lets
        /// multiple tabs from the same (pack, variant) share a single
        /// PackageInstance.
        /// </para>
        ///
        /// <para>
        /// Does NOT validate the save's package_uid / package_version
        /// against this state's pack — the caller is responsible for
        /// having forked from the correct PackageInstance. Returns
        /// false if Items / Locations refuse the data.
        /// </para>
        /// </summary>
        public bool LoadProgressFromJObject(JObject root)
        {
            if (root == null) return false;

            try
            {
                if (!Items.Load(root)) return false;
                if (!Locations.Load(root)) return false;

                Settings.IgnoreAllLogic = root.GetValue<bool>("ignore_all_logic", false);
                Settings.DisplayAllLocations = root.GetValue<bool>("display_all_locations", false);
                Settings.AlwaysAllowClearing = root.GetValue<bool>("always_allow_chest_manipulation", false);
                Settings.AutoUnpinLocationsOnClear = root.GetValue<bool>("auto_unpin_locations_on_clear", true);
                Settings.PinLocationsOnItemCapture = root.GetValue<bool>("pin_locations_on_item_capture", true);

                return true;
            }
            catch (Exception ex)
            {
                Scripts?.OutputError("Error encountered while restoring saved tracker state");
                Scripts?.OutputException(ex);
                return false;
            }
        }

        // ---- ICodeProvider helpers (state-local) ------------------------

        /// <summary>
        /// Resolves <paramref name="code"/> against this state's catalogs.
        /// Codes prefixed with <c>@</c> resolve through
        /// <see cref="Locations"/>; <c>$</c> through <see cref="Scripts"/>;
        /// otherwise <see cref="Items"/>.
        /// </summary>
        public object FindObjectForCode(string code)
        {
            var provider = ResolveCodeProvider(ref code);
            return provider?.FindObjectForCode(code);
        }

        /// <summary>
        /// Counts providers for <paramref name="code"/> against this
        /// state's catalogs, with the maximum accessibility across them
        /// returned via <paramref name="maxAccessibility"/>.
        /// </summary>
        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            var provider = ResolveCodeProvider(ref code);
            if (provider == null)
            {
                maxAccessibility = AccessibilityLevel.None;
                return 0;
            }
            return provider.ProviderCountForCode(code, out maxAccessibility);
        }

        ICodeProvider ResolveCodeProvider(ref string code)
        {
            if (code.StartsWith("@"))
            {
                code = code.Substring(1);
                return Locations;
            }
            if (code.StartsWith("$"))
            {
                code = code.Substring(1);
                return Scripts;
            }
            return Items;
        }
    }
}
