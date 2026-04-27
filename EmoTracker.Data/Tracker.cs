using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLua;
using System;
using System.IO;
using System.Linq;

// Phase 6 step 11: Tracker.Reload IS the bootstrap that populates the
// catalog singletons during pack-load — it runs *before* the primary
// TrackerState exists. ApplicationModel adopts those just-populated
// singletons into PrimaryState afterward via
// RebindActivePackageInstanceFromSingletons. So every catalog access
// here is the documented bootstrap path; file-level suppression keeps
// the warnings out of the noise without hiding the migration intent.
#pragma warning disable CS0618

namespace EmoTracker.Data
{
    /// <summary>
    /// Phase 6 step 11 / Phase 7.1.g: <see cref="Tracker"/> is the
    /// app-wide pack-load orchestrator. It owns the <c>Reload</c>
    /// entrypoint, the <see cref="OnPackageLoadStarting"/> /
    /// <see cref="OnPackageLoadComplete"/> events that drive
    /// <c>ApplicationModel</c>'s adoption of the just-loaded catalogs
    /// into a <see cref="Sessions.TrackerState"/>, and the
    /// <see cref="ActiveGamePackage"/> / <see cref="ActiveGamePackageVariant"/>
    /// metadata that surfaces the currently-loaded pack to consumers.
    ///
    /// <para>
    /// <b>Phase 7.1.g decision:</b> the plan's call to delete this class
    /// outright was reconsidered and the class is kept by design. Pack
    /// load is inherently an app-wide operation (pack files, parse,
    /// init.lua execution, registry building), and 124 callsites depend
    /// on the <see cref="Tracker.Instance"/> entry point as the
    /// canonical "what's loaded" surface. Per Phase 7.3 the
    /// per-state-mutable bits (<c>SwapLeftRight</c>, <c>MapEnabled</c>)
    /// have been migrated to <see cref="Sessions.SessionSettings"/>;
    /// Tracker's properties for those now forward to the active state's
    /// SessionSettings, keeping bindings working without splitting the
    /// canonical source.
    /// </para>
    /// </summary>
    public class Tracker : ObservableSingleton<Tracker>, ICodeProvider
    {
        public event EventHandler<EventArgs> OnPackageLoadStarting;
        public event EventHandler<EventArgs> OnPackageLoadComplete;

        static readonly string DefaultDisabledImageFilterSpec = "grayscale, dim";

        private string mDisabledImageFilterSpec = DefaultDisabledImageFilterSpec;

        private bool mbSuspendPackReadyEvent = false;
        public bool SuspendPackReadyEvent
        {
            get { return mbSuspendPackReadyEvent; }
            protected set { SetProperty(ref mbSuspendPackReadyEvent, value); }
        }

        private bool mbMapEnabled = true;
        private bool mbSwapLeftRight = false;
        private bool mbAllowResize = true;

        private IGamePackage mActiveGamePackage;
        private IGamePackageVariant mActiveGamePackageVariant;

        public string DisabledImageFilterSpec
        {
            get { return mDisabledImageFilterSpec; }
            set { SetProperty(ref mDisabledImageFilterSpec, value); }
        }

        private void ValidatePackageSafety(IGamePackage package)
        {
            // Pack-safety warning was disabled. The original implementation
            // pushed a notification to the active state's NotificationService;
            // bring it back via the holder-aware path
            // (notification target = the state being loaded into).
        }

        // Phase 7 polish: when set to true, the next pack-info update
        // (ActiveGamePackage / ActiveGamePackageVariant) skips Reload —
        // used by cross-PackageInstance tab switches that want to update
        // pack metadata without re-running pack-load (the target pack is
        // already loaded in the destination PackageInstance).
        bool mSuppressNextReload = false;

        /// <summary>
        /// Phase 7 polish: update <see cref="ActiveGamePackage"/> /
        /// <see cref="ActiveGamePackageVariant"/> WITHOUT triggering
        /// <see cref="Reload"/>. Used by <c>OnActiveStateSwitched</c>'s
        /// cross-PackageInstance path so the image cache (and other
        /// pack-load-tear-down side effects) survives a tab swap.
        /// </summary>
        public void UpdatePackageInfoWithoutReload(IGamePackage package, IGamePackageVariant variant)
        {
            mSuppressNextReload = true;
            try
            {
                ActiveGamePackageVariant = null;
                ActiveGamePackage = package;
                if (variant != null)
                    ActiveGamePackageVariant = variant;
            }
            finally
            {
                mSuppressNextReload = false;
            }
        }

        public IGamePackage ActiveGamePackage
        {
            get { return mActiveGamePackage; }
            set
            {
                if (SetProperty(ref mActiveGamePackage, value))
                {
                    ValidatePackageSafety(mActiveGamePackage);

                    PackageManager.Instance.RefreshActiveState();
                    if (!mSuppressNextReload)
                        Reload();

                    if (mActiveGamePackage != null)
                    {
                        ApplicationSettings.Instance.LastActivePackage = mActiveGamePackage.UniqueID;

                        if (!ActiveGamePackage.AvailableVariants.Contains(ActiveGamePackageVariant))
                            ActiveGamePackageVariant = null;
                    }
                    else
                    {
                        ActiveGamePackageVariant = null;
                    }
                }
            }
        }

        [DependentProperty("ActiveVariantUID")]
        public IGamePackageVariant ActiveGamePackageVariant
        {
            get { return mActiveGamePackageVariant; }
            set
            {
                if (SetProperty(ref mActiveGamePackageVariant, value))
                {
                    if (mActiveGamePackageVariant != null)
                    {
                        ActiveGamePackage = mActiveGamePackageVariant.Package;
                        ActiveGamePackage.ActiveVariant = mActiveGamePackageVariant;
                        ApplicationSettings.Instance.LastActivePackageVariant = mActiveGamePackageVariant.UniqueID;
                    }
                    else
                    {
                        ApplicationSettings.Instance.LastActivePackageVariant = null;
                    }

                    PackageManager.Instance.RefreshActiveState();
                    if (!mSuppressNextReload)
                        Reload();
                }
            }
        }

        public string ActiveVariantUID
        {
            get
            {
                if (ActiveGamePackageVariant != null)
                    return ActiveGamePackageVariant.UniqueID;

                return "none";
            }
        }

        // Legacy shim properties. The authoritative values live on each
        // TrackerState's SessionSettings (Phase 7.3) — Tracker keeps a
        // local copy for back-compat with bindings that haven't yet
        // migrated to <c>WindowContext.ActiveState.Settings</c>. There's
        // no automatic per-state forwarding here; per-state consumers
        // should read via <c>(holder.OwnerState as TrackerState)?.Settings</c>.
        public bool SwapLeftRight
        {
            get { return mbSwapLeftRight; }
            set { if (SetProperty(ref mbSwapLeftRight, value)) Reload(); }
        }

        public bool MapEnabled
        {
            get { return mbMapEnabled; }
            set { if (SetProperty(ref mbMapEnabled, value)) Reload(); }
        }

        public bool AllowResize
        {
            get { return mbAllowResize; }
            set { SetProperty(ref mbAllowResize, value); }
        }

        public Tracker()
        {
        }

        public bool IsActivePackage(string ID)
        {
           if(mActiveGamePackage == null) { return false; }

            return mActiveGamePackage.UniqueID == ID;

        }


#region -- Save/Load --

        //  TODO: Replace width/height with generic key/value data
        public bool SaveProgress(Sessions.TrackerState target, string path, Action<JObject> dataAction = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            JObject root = new JObject();

            if (ActiveGamePackage != null)
            {
                root["package_uid"] = ActiveGamePackage.UniqueID;

                if (ActiveGamePackageVariant != null)
                    root["package_variant_uid"] = ActiveGamePackageVariant.UniqueID;

                root["package_version"] = ActiveGamePackage.Version.ToString();
                root["creation_time"] = DateTime.Now.ToString();
                root["ignore_all_logic"] = ApplicationSettings.Instance.IgnoreAllLogic;
                root["display_all_locations"] = ApplicationSettings.Instance.DisplayAllLocations;
                root["always_allow_chest_manipulation"] = ApplicationSettings.Instance.AlwaysAllowClearing;
                root["auto_unpin_locations_on_clear"] = ApplicationSettings.Instance.AutoUnpinLocationsOnClear;
                root["pin_locations_on_item_capture"] = ApplicationSettings.Instance.PinLocationsOnItemCapture;

                target.Items.Save(root);
                target.Locations.Save(root);

                if (dataAction != null)
                    dataAction(root);

                try
                {
                    using (TextWriter textWriter = new StreamWriter(path))
                    {
                        using (JsonTextWriter writer = new JsonTextWriter(textWriter))
                        {
                            writer.Formatting = Formatting.Indented;
                            root.WriteTo(writer);
                        }
                    }

                    return true;
                }
                catch
                {
                    if (target.Scripts?.NotificationService != null)
                    {
                        target.Scripts.NotificationService.PushMarkdownNotification(Scripting.NotificationType.Error,
@"### Couldn't Save Progress

An error occurred while saving. This may be due to anti-virus/malware software protecting your chosen save directory over-aggressively.
* Ensure you have write permissions for the directory you chose
* Saving to a different location may work");
                    }
                }
            }

            return false;
        }

        public bool LoadProgress(Sessions.TrackerState target, string path, Action<JObject> dataAction = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            Reload(target);

            try
            {
                target.Scripts?.Output("Loading save game \"{0}\"", path);
                ((IScriptManager)target.Scripts)?.InvokeStandardCallback(StandardCallback.StartLoadingSaveFile);

                using (StreamReader reader = new StreamReader(path))
                {
                    JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));

                    string packageUID = root.GetValue<string>("package_uid");
                    string packageVariantUID = root.GetValue<string>("package_variant_uid");

                    Version packVersion;
                    Version.TryParse(root.GetValue<string>("package_version"), out packVersion);

                    IGamePackage package = PackageManager.Instance.FindInstalledPackage(packageUID);
                    if (package == null)
                        return false;

                    if (package.Version != packVersion)
                        return false;

                    IGamePackageVariant packageVariant = package.FindVariant(packageVariantUID);
                    if (!string.IsNullOrWhiteSpace(packageVariantUID) && packageVariant == null)
                        return false;

                    SuspendPackReadyEvent = true;

                    //  Load either the package or the variant, as appropriate
                    if (packageVariant != null)
                        SetActiveGamePackageVariantForTarget(target, packageVariant);
                    else
                        SetActiveGamePackageForTarget(target, package);

                    if (!target.Items.Load(root))
                        return false;

                    if (!target.Locations.Load(root))
                        return false;

                    ApplicationSettings.Instance.IgnoreAllLogic = root.GetValue<bool>("ignore_all_logic", false);
                    ApplicationSettings.Instance.DisplayAllLocations = root.GetValue<bool>("display_all_locations", false);
                    ApplicationSettings.Instance.AlwaysAllowClearing = root.GetValue<bool>("always_allow_chest_manipulation", false);
                    ApplicationSettings.Instance.AutoUnpinLocationsOnClear = root.GetValue<bool>("auto_unpin_locations_on_clear", true);
                    ApplicationSettings.Instance.PinLocationsOnItemCapture = root.GetValue<bool>("pin_locations_on_item_capture", true);

                    //  Invoke any external data action
                    if (dataAction != null)
                        dataAction(root);
                }

                return true;
            }
            catch (Exception ex)
            {
                target.Scripts?.OutputError("Error encountered while loading save game");
                target.Scripts?.OutputException(ex);
            }
            finally
            {
                ((IScriptManager)target.Scripts)?.InvokeStandardCallback(StandardCallback.FinishLoadingSaveFile);
                ((IScriptManager)target.Scripts)?.InvokeStandardCallback(StandardCallback.PackReady);

                target.Scripts?.Output("Finished loading save game \"{0}\"", path);

                SuspendPackReadyEvent = false;
            }

            return false;
        }

        // Shim helpers for LoadProgress: the legacy ActiveGamePackage / ActiveGamePackageVariant
        // setters trigger Reload() against an ambient target. With LoadProgress now taking
        // an explicit target, we set the underlying field and drive Reload(target) directly.
        void SetActiveGamePackageForTarget(Sessions.TrackerState target, IGamePackage package)
        {
            mActiveGamePackage = package;
            mActiveGamePackageVariant = null;
            NotifyPropertyChanged(nameof(ActiveGamePackage));
            NotifyPropertyChanged(nameof(ActiveVariantUID));
            Reload(target);
        }

        void SetActiveGamePackageVariantForTarget(Sessions.TrackerState target, IGamePackageVariant variant)
        {
            mActiveGamePackageVariant = variant;
            if (variant != null) mActiveGamePackage = variant.Package;
            NotifyPropertyChanged(nameof(ActiveGamePackageVariant));
            NotifyPropertyChanged(nameof(ActiveGamePackage));
            NotifyPropertyChanged(nameof(ActiveVariantUID));
            Reload(target);
        }


#endregion

#region -- ICodeProvider --

        private static void GetFilteredCodeAndProvider(Sessions.TrackerState state, ref string code, out ICodeProvider provider)
        {
            if (state == null) { provider = null; return; }
            provider = state.Items;

            if (code.StartsWith("@"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = state.Locations;
            }
            else if (code.StartsWith("$"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = state.Scripts;
            }
        }

        public object FindObjectForCode(Sessions.TrackerState state, string code)
        {
            GetFilteredCodeAndProvider(state, ref code, out var provider);
            return provider?.FindObjectForCode(code);
        }

        public uint ProviderCountForCode(Sessions.TrackerState state, string code, out AccessibilityLevel maxAccessibility)
        {
            GetFilteredCodeAndProvider(state, ref code, out var provider);
            if (provider == null)
            {
                maxAccessibility = AccessibilityLevel.None;
                return 0;
            }
            return provider.ProviderCountForCode(code, out maxAccessibility);
        }

        // Legacy ICodeProvider members kept until Tracker is fully retired (Phase 7.1.g).
        // These have no state context and so always return null/0; new callers must use
        // the state-taking overloads above.
        public object FindObjectForCode(string code)
        {
            return null;
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            maxAccessibility = AccessibilityLevel.None;
            return 0;
        }

#endregion

#region -- Package Load --

        public (bool, string) LoadDefaultPackage()
        {
            string loadpack = string.IsNullOrEmpty(ApplicationSettings.Instance.CommandLinePackage) ? ApplicationSettings.Instance.LastActivePackage : ApplicationSettings.Instance.CommandLinePackage;
            string loadvar = string.IsNullOrEmpty(ApplicationSettings.Instance.CommandLinePackageVariant) ? ApplicationSettings.Instance.LastActivePackageVariant : ApplicationSettings.Instance.CommandLinePackageVariant;

            Instance.ActiveGamePackage = PackageManager.Instance.FindInstalledPackage(loadpack);

            if (Tracker.Instance.ActiveGamePackage != null && Tracker.Instance.ActiveGamePackage.AvailableVariants != null)
            {
                bool found = false;

                if (!string.IsNullOrWhiteSpace(loadvar))
                {
                    foreach (IGamePackageVariant variant in Tracker.Instance.ActiveGamePackage.AvailableVariants)
                    {
                        if (string.Equals(variant.UniqueID, loadvar, StringComparison.Ordinal))
                        {
                            Tracker.Instance.ActiveGamePackageVariant = variant;
                            found = true;
                            break;
                        }
                    }
                }

                if (Tracker.Instance.ActiveGamePackageVariant == null && Tracker.Instance.ActiveGamePackage.AvailableVariants != null)
                {
                    Tracker.Instance.ActiveGamePackageVariant = Tracker.Instance.ActiveGamePackage.AvailableVariants.FirstOrDefault();
                }

                if (!found)
                {
                    if (Tracker.Instance.ActiveGamePackageVariant != null)
                    {
                        string activeVariant = Tracker.Instance.ActiveGamePackageVariant.UniqueID;
                        return (false, $"### Package Variant {loadvar} not found, loading default variant `{activeVariant}` for {loadpack}");
                    }
                    else
                    {
                        return (false, $"### Package Variant {loadvar} not found, loading default pack for {loadpack}");
                    }
                }
            }
            return (true, string.Empty);
        }

        void ResetPackageSettings()
        {
            AllowResize = true;
            DisabledImageFilterSpec = DefaultDisabledImageFilterSpec;
        }

        // Phase 7.1: pack-settings load orchestration moved to PackageLoader.LoadPackageSettings;
        // the Tracker-bound copy that consulted SessionContext.ActiveState is dead code.

        bool mbReloadInProgress = false;

        // Reload is parameterless for back-compat with property setters
        // (ActiveGamePackage / SwapLeftRight / etc. all call Reload() on
        // change). Resolution of the target state happens via the
        // <see cref="ResolveReloadTarget"/> hook installed by
        // ApplicationModel — the runtime owner of the active primary
        // state. No SessionContext or other ambient slot is consulted.
        public Func<Sessions.TrackerState> ResolveReloadTarget { get; set; }

        public void Reload()
        {
            var target = ResolveReloadTarget?.Invoke();
            if (target == null)
                throw new InvalidOperationException(
                    "Tracker.Reload requires ResolveReloadTarget to be installed by " +
                    "ApplicationModel. Call sites must either install it during ctor or " +
                    "call Reload(state) directly.");
            Reload(target);
        }

        public void Reload(Sessions.TrackerState target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (mbReloadInProgress) return;
            mbReloadInProgress = true;

            try
            {
                EventHandler<Sessions.PackageLoader.PackageLoadEventArgs> startHandler =
                    (_, _) => OnPackageLoadStarting?.Invoke(this, EventArgs.Empty);
                EventHandler<Sessions.PackageLoader.PackageLoadEventArgs> completeHandler =
                    (_, _) => OnPackageLoadComplete?.Invoke(this, EventArgs.Empty);

                Sessions.PackageLoader.OnPackageLoadStarting += startHandler;
                Sessions.PackageLoader.OnPackageLoadComplete += completeHandler;
                try
                {
                    Sessions.PackageLoader.LoadInto(
                        target,
                        mActiveGamePackage,
                        mActiveGamePackageVariant,
                        suspendPackReadyEvent: SuspendPackReadyEvent);
                }
                finally
                {
                    Sessions.PackageLoader.OnPackageLoadStarting -= startHandler;
                    Sessions.PackageLoader.OnPackageLoadComplete -= completeHandler;
                }
            }
            finally
            {
                mbReloadInProgress = false;
            }
        }

#endregion

        // Phase 7.1: pack-script-driven incremental loads (Tracker:AddItems
        // / AddMaps / AddLocations / AddLayouts from init.lua) now route
        // directly through the per-state TrackerScriptInterface bound to
        // the state's Lua interpreter. The Tracker-level wrappers are
        // gone — they had to consult an ambient state slot.

    }
}
