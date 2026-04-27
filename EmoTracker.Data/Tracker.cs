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
#if False
            if (package != null && package.FlaggedAsUnsafe)
            {
                if (Sessions.SessionContext.ActiveState?.Scripts.NotificationService != null)
                {
                    Sessions.SessionContext.ActiveState?.Scripts.NotificationService.PushMarkdownNotification(Scripting.NotificationType.Warning,
@"### Potentially Unsafe Package Loaded

The active game package uses potentially unsafe scripting functionality, which allows it to access your filesystem, can expose user information, and more.
* Make sure you trust the author of this package
* Proceed at your own risk", 60000);
                }
            }
#endif
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

        // Phase 7.11 polish: forward SwapLeftRight / MapEnabled to the
        // active state's SessionSettings (Phase 7.3 made these per-state).
        // Tracker keeps the property surface for back-compat with existing
        // bindings; the underlying value lives on SessionSettings.
        public bool SwapLeftRight
        {
            get
            {
                var s = Sessions.SessionContext.ActiveState?.Settings;
                return s != null ? s.SwapLeftRight : mbSwapLeftRight;
            }
            set
            {
                bool changed = SetProperty(ref mbSwapLeftRight, value);
                var s = Sessions.SessionContext.ActiveState?.Settings;
                if (s != null && s.SwapLeftRight != value)
                    s.SwapLeftRight = value;
                if (changed) Reload();
            }
        }

        public bool MapEnabled
        {
            get
            {
                var s = Sessions.SessionContext.ActiveState?.Settings;
                return s != null ? s.MapEnabled : mbMapEnabled;
            }
            set
            {
                bool changed = SetProperty(ref mbMapEnabled, value);
                var s = Sessions.SessionContext.ActiveState?.Settings;
                if (s != null && s.MapEnabled != value)
                    s.MapEnabled = value;
                if (changed) Reload();
            }
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
        public bool SaveProgress(string path, Action<JObject> dataAction = null)
        {
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

                Sessions.SessionContext.ActiveState?.Items.Save(root);
                Sessions.SessionContext.ActiveState?.Locations.Save(root);

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
                    if (Sessions.SessionContext.ActiveState?.Scripts.NotificationService != null)
                    {
                        Sessions.SessionContext.ActiveState?.Scripts.NotificationService.PushMarkdownNotification(Scripting.NotificationType.Error,
@"### Couldn't Save Progress

An error occurred while saving. This may be due to anti-virus/malware software protecting your chosen save directory over-aggressively.
* Ensure you have write permissions for the directory you chose
* Saving to a different location may work");
                    }
                }
            }

            return false;
        }

        public bool LoadProgress(string path, Action<JObject> dataAction = null)
        {
            Reload();

            try
            {
                Sessions.SessionContext.ActiveState?.Scripts.Output("Loading save game \"{0}\"", path);
                (ScriptManagerHost.Current ?? NullScriptManager.Instance).InvokeStandardCallback(StandardCallback.StartLoadingSaveFile);

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
                        ActiveGamePackageVariant = packageVariant;
                    else
                        ActiveGamePackage = package;

                    var loadTarget = Sessions.SessionContext.ActiveState
                        ?? throw new InvalidOperationException("LoadProgress called before SessionContext.ActiveState was installed");

                    if (!loadTarget.Items.Load(root))
                        return false;

                    if (!loadTarget.Locations.Load(root))
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
                Sessions.SessionContext.ActiveState?.Scripts.OutputError("Error encountered while loading save game");
                Sessions.SessionContext.ActiveState?.Scripts.OutputException(ex);
            }
            finally
            {
                (ScriptManagerHost.Current ?? NullScriptManager.Instance).InvokeStandardCallback(StandardCallback.FinishLoadingSaveFile);
                (ScriptManagerHost.Current ?? NullScriptManager.Instance).InvokeStandardCallback(StandardCallback.PackReady);

                Sessions.SessionContext.ActiveState?.Scripts.Output("Finished loading save game \"{0}\"", path);

                SuspendPackReadyEvent = false;
            }

            return false;
        }


#endregion

#region -- ICodeProvider --

        private void GetFilteredCodeAndProvider(ref string code, out ICodeProvider provider)
        {
            provider = Sessions.SessionContext.ActiveState?.Items;

            if (code.StartsWith("@"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = Sessions.SessionContext.ActiveState?.Locations;
            }
            else if (code.StartsWith("$"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = Sessions.SessionContext.ActiveState?.Scripts;
            }
        }

        public object FindObjectForCode(string code)
        {
            ICodeProvider provider;
            GetFilteredCodeAndProvider(ref code, out provider);

            return provider.FindObjectForCode(code);
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            ICodeProvider provider;
            GetFilteredCodeAndProvider(ref code, out provider);

            return provider.ProviderCountForCode(code, out maxAccessibility);
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

        void LoadPackageSettings()
        {
            if (mActiveGamePackage != null)
            {
                bool bLoadedSettings = false;
                using (Stream s = mActiveGamePackage.Open("settings.json"))
                {
                    if (s != null)
                    {
                        Sessions.SessionContext.ActiveState?.Scripts.Output("Loading package settings");
                        using (new LoggingBlock())
                        {
                            try
                            {
                                using (StreamReader reader = new StreamReader(s))
                                {
                                    JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                                    AllowResize = root.GetValue<bool>("allow_resize", true);

                                    string spec = root.GetValue<string>("disabled_image_filter", null);
                                    if (spec != null)
                                        DisabledImageFilterSpec = spec;

                                    Sessions.SessionContext.ActiveState?.Locations.ParseLocationVisualProperties(root, Sessions.SessionContext.ActiveState?.Locations.Root, mActiveGamePackage);

                                    AccessibilityRule.EnableCache = root.GetValue<bool>("enable_accessibility_rule_caching", true);

                                    bLoadedSettings = true;
                                }
                            }
                            catch (Exception e)
                            {
                                Sessions.SessionContext.ActiveState?.Scripts.OutputException(e);
                            }
                        }
                    } 
                }

                if (!bLoadedSettings)
                {
                    Sessions.SessionContext.ActiveState?.Scripts.OutputWarning("Loading legacy package settings from tracker_layout.json");
                    using (new LoggingBlock())
                    {
                        try
                        {
                            //  TODO : Destroy this fuckery with hot liquid rage
                            using (StreamReader reader = new StreamReader(mActiveGamePackage.Open("tracker_layout.json")))
                            {
                                JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                                AllowResize = root.GetValue<bool>("allow_resize", true);
                                string spec = root.GetValue<string>("disabled_image_filter", null);
                                if (spec != null)
                                    DisabledImageFilterSpec = spec;

                                Sessions.SessionContext.ActiveState?.Locations.ParseLocationVisualProperties(root, Sessions.SessionContext.ActiveState?.Locations.Root, mActiveGamePackage);
                            }
                        }
                        catch (Exception e)
                        {
                            Sessions.SessionContext.ActiveState?.Scripts.OutputException(e);
                        }
                    }
                }
            }
        }

        bool mbReloadInProgress = false;

        public void Reload()
        {
            if (mbReloadInProgress) return;
            mbReloadInProgress = true;

            try
            {
                // Phase 7.1: pack-load orchestration moved to PackageLoader
                // (loads into a target TrackerState instead of mutating
                // shared singleton catalogs). Tracker's own
                // OnPackageLoadStarting/Complete events fire as a thin shim
                // around PackageLoader's events for backwards compat through
                // 7.1.g (Tracker deletion).
                var target = Sessions.SessionContext.ActiveState;
                if (target == null)
                {
                    // Pre-7.1 we mutated the singleton catalogs here. Phase
                    // 7.1.b ensures ApplicationModel pre-allocates the
                    // primary state + installs SessionContext.ActiveState
                    // before any pack-load reaches Tracker.Reload, so this
                    // path is no longer exercised. Throwing makes the
                    // assumption explicit and turns any regression into a
                    // loud failure rather than a silent fallback to the
                    // deleted legacy path.
                    throw new InvalidOperationException(
                        "Tracker.Reload called before SessionContext.ActiveState was installed. " +
                        "ApplicationModel must pre-allocate the primary state during construction.");
                }

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

#region -- Incremental Load Wrappers --

        // Phase 7.1: route pack-script-driven incremental loads (called via
        // Tracker:AddItems / AddMaps / AddLocations / AddLayouts from
        // init.lua) into the active state's catalogs. PackageLoader sets
        // SessionContext.ActiveState to the target before invoking
        // ScriptManager.Load(package), which is when init.lua runs and
        // calls these helpers.
        // Phase 7.1 fix: thread the active state through to IncrementalLoad
        // so items/locations/maps get OwnerState stamped at construction
        // (and registered in state.Resolver) rather than left null. Without
        // this, items added via Tracker:AddItems from init.lua have null
        // OwnerState — so their KVMutable [OnChanged] side effects (e.g.
        // InvalidateAccessibility on Icon change) silently no-op because
        // `(this.OwnerState as TrackerState)?.Locations` is null.
        public void AddItems(string path)
        {
            if (ActiveGamePackage == null) return;
            var state = Sessions.SessionContext.ActiveState;
            if (state != null)
                state.Items.IncrementalLoad(path, ActiveGamePackage, bLegacy: false, state: state);
        }

        public void AddMaps(string path)
        {
            if (ActiveGamePackage == null) return;
            var state = Sessions.SessionContext.ActiveState;
            if (state != null)
                state.Maps.IncrementalLoad(path, ActiveGamePackage, state: state);
        }

        public void AddLocations(string path)
        {
            if (ActiveGamePackage == null) return;
            var state = Sessions.SessionContext.ActiveState;
            if (state != null)
                state.Locations.IncrementalLoad(path, ActiveGamePackage, bLegacy: false, state: state);
        }

        public void AddLayouts(string path)
        {
            if (ActiveGamePackage == null) return;
            var state = Sessions.SessionContext.ActiveState;
            if (state != null)
                state.Layouts.IncrementalLoad(path, ActiveGamePackage);
        }

        #endregion

    }
}
