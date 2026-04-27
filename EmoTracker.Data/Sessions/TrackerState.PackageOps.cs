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
    /// Phase 7.1.g: per-state pack-management operations. Previously these
    /// lived on the <c>Tracker</c> singleton; with the singleton retired,
    /// they're per-state methods on <see cref="TrackerState"/>. The state
    /// is the natural owner: each operation acts on this state's catalogs
    /// (<see cref="Items"/>, <see cref="Locations"/>, <see cref="Maps"/>,
    /// <see cref="Layouts"/>, <see cref="Scripts"/>) and updates this
    /// state's pack metadata (<see cref="Package"/>, <see cref="ActiveVariant"/>).
    /// </summary>
    public sealed partial class TrackerState
    {
        // ---- Pack metadata helpers --------------------------------------

        /// <summary>
        /// The currently active variant's <c>UniqueID</c>, or the literal
        /// string <c>"none"</c> when no variant is active. Mirrors
        /// <c>Tracker.ActiveVariantUID</c>'s semantics for save-file
        /// compatibility.
        /// </summary>
        public string ActiveVariantUID =>
            mActiveVariant != null ? mActiveVariant.UniqueID : "none";

        /// <summary>
        /// Returns true iff this state's active package's
        /// <c>UniqueID</c> matches <paramref name="uniqueId"/>.
        /// </summary>
        public bool IsActivePackage(string uniqueId)
        {
            if (mPackage == null) return false;
            return string.Equals(mPackage.UniqueID, uniqueId, StringComparison.Ordinal);
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
        /// using its current <see cref="Package"/> / <see cref="ActiveVariant"/>.
        /// No-op if a reload is already in progress on this state.
        /// </summary>
        public void Reload()
        {
            if (mbReloadInProgress) return;
            mbReloadInProgress = true;
            try
            {
                PackageLoader.LoadInto(this, mPackage, mActiveVariant);
            }
            finally
            {
                mbReloadInProgress = false;
            }
        }

        /// <summary>
        /// Sets this state's <see cref="Package"/> / <see cref="ActiveVariant"/>
        /// and re-runs <see cref="PackageLoader.LoadInto"/>. Pass
        /// <paramref name="variant"/> = null when the new pack has no
        /// chosen variant (or to default to its first available).
        /// </summary>
        public void ActivatePackage(IGamePackage package, IGamePackageVariant variant)
        {
            if (mbReloadInProgress) return;
            mbReloadInProgress = true;
            try
            {
                // Validate variant belongs to package, falling back to first
                // available variant when the requested one is invalid (matches
                // legacy Tracker behavior).
                if (package != null && variant != null
                    && !package.AvailableVariants.Contains(variant))
                {
                    variant = null;
                }

                Package = package;
                ActiveVariant = variant;
                if (package != null)
                {
                    package.ActiveVariant = variant;
                    ApplicationSettings.Instance.LastActivePackage = package.UniqueID;
                    ApplicationSettings.Instance.LastActivePackageVariant = variant?.UniqueID;
                }
                else
                {
                    ApplicationSettings.Instance.LastActivePackageVariant = null;
                }

                PackageManager.Instance.RefreshActiveState();
                NotifyPropertyChanged(nameof(ActiveVariantUID));

                PackageLoader.LoadInto(this, mPackage, mActiveVariant);
            }
            finally
            {
                mbReloadInProgress = false;
            }
        }

        /// <summary>
        /// Updates <see cref="Package"/> / <see cref="ActiveVariant"/>
        /// without triggering a reload. Used by cross-PackageInstance tab
        /// switches that want to update pack metadata without re-running
        /// the load (the destination pack is already loaded).
        /// </summary>
        public void UpdatePackageInfoWithoutReload(IGamePackage package, IGamePackageVariant variant)
        {
            Package = package;
            ActiveVariant = variant;
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
            if (mPackage == null) return false;

            JObject root = new JObject();
            root["package_uid"] = mPackage.UniqueID;
            if (mActiveVariant != null)
                root["package_variant_uid"] = mActiveVariant.UniqueID;
            root["package_version"] = mPackage.Version.ToString();
            root["creation_time"] = DateTime.Now.ToString();
            root["ignore_all_logic"] = Settings.IgnoreAllLogic;
            root["display_all_locations"] = Settings.DisplayAllLocations;
            root["always_allow_chest_manipulation"] = Settings.AlwaysAllowClearing;
            root["auto_unpin_locations_on_clear"] = Settings.AutoUnpinLocationsOnClear;
            root["pin_locations_on_item_capture"] = Settings.PinLocationsOnItemCapture;

            Items.Save(root);
            Locations.Save(root);

            dataAction?.Invoke(root);

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
        /// Load previously-saved progress from <paramref name="path"/> into
        /// this state. Triggers a fresh <see cref="Reload"/> first to
        /// ensure the catalogs are clean.
        /// </summary>
        public bool LoadProgress(string path, Action<JObject> dataAction = null)
        {
            // Reload first so we start from a clean slate against the
            // pack/variant referenced by the save file.
            //
            // The pack identity in the save file MAY differ from the
            // currently active one — handle below.
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

                    // Reload into this state with the save's pack/variant.
                    Package = package;
                    ActiveVariant = packageVariant;
                    if (package != null)
                        package.ActiveVariant = packageVariant;
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
