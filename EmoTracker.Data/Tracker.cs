using EmoTracker.Core;
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

namespace EmoTracker.Data
{
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
                if (ScriptManager.Instance.NotificationService != null)
                {
                    ScriptManager.Instance.NotificationService.PushMarkdownNotification(Scripting.NotificationType.Warning,
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

                ItemDatabase.Instance.Save(root);
                LocationDatabase.Instance.Save(root);

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
                    if (ScriptManager.Instance.NotificationService != null)
                    {
                        ScriptManager.Instance.NotificationService.PushMarkdownNotification(Scripting.NotificationType.Error,
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
                ScriptManager.Instance.Output("Loading save game \"{0}\"", path);
                ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.StartLoadingSaveFile);

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

                    if (!ItemDatabase.Instance.Load(root))
                        return false;

                    if (!LocationDatabase.Instance.Load(root))
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
                ScriptManager.Instance.OutputError("Error encountered while loading save game");
                ScriptManager.Instance.OutputException(ex);
            }
            finally
            {
                ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.FinishLoadingSaveFile);
                ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.PackReady);

                ScriptManager.Instance.Output("Finished loading save game \"{0}\"", path);

                SuspendPackReadyEvent = false;
            }

            return false;
        }


#endregion

#region -- ICodeProvider --

        private void GetFilteredCodeAndProvider(ref string code, out ICodeProvider provider)
        {
            provider = ItemDatabase.Instance;

            if (code.StartsWith("@"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = LocationDatabase.Instance;
            }
            else if (code.StartsWith("$"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = ScriptManager.Instance;
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
                        ScriptManager.Instance.Output("Loading package settings");
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

                                    LocationDatabase.Instance.ParseLocationVisualProperties(root, LocationDatabase.Instance.Root, mActiveGamePackage);

                                    AccessibilityRule.EnableCache = root.GetValue<bool>("enable_accessibility_rule_caching", true);

                                    bLoadedSettings = true;
                                }
                            }
                            catch (Exception e)
                            {
                                ScriptManager.Instance.OutputException(e);
                            }
                        }
                    } 
                }

                if (!bLoadedSettings)
                {
                    ScriptManager.Instance.OutputWarning("Loading legacy package settings from tracker_layout.json");
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

                                LocationDatabase.Instance.ParseLocationVisualProperties(root, LocationDatabase.Instance.Root, mActiveGamePackage);
                            }
                        }
                        catch (Exception e)
                        {
                            ScriptManager.Instance.OutputException(e);
                        }
                    }
                }
            }
        }

        bool mbReloadInProgress = false;

        public void Reload()
        {
            if (!mbReloadInProgress)
            {
                mbReloadInProgress = true;

                try
                {
                    AccessibilityRule.ClearCaches();

                    if (OnPackageLoadStarting != null)
                        OnPackageLoadStarting(this, EventArgs.Empty);

                    ResetPackageSettings();

                    LayoutManager.Instance.Clear();
                    MapDatabase.Instance.Reset();
                    LocationDatabase.Instance.Reset();
                    ItemDatabase.Instance.Reset();
                    ScriptManager.Instance.Reset();

                    // Reload application colors customization data
                    ApplicationColors.Instance.LoadColors();

                    if (mActiveGamePackage != null)
                    {
                        ScriptManager.Instance.Output("Beginning Package Load");
                        using (new LoggingBlock())
                        {
                            ScriptManager.Instance.Output(string.Format("Package: {0}", ActiveGamePackage.UniqueID));
                            if (ActiveGamePackageVariant != null)
                                ScriptManager.Instance.Output(string.Format("Variant: {0}", ActiveGamePackageVariant.UniqueID));

                            LoadPackageSettings();

                            ScriptManager.Instance.Load(mActiveGamePackage);

                            //  Legacy loads - should this be contingent on a flag in the manifest
                            ItemDatabase.Instance.LegacyLoad(mActiveGamePackage);
                            MapDatabase.Instance.LegacyLoad(mActiveGamePackage);
                            LocationDatabase.Instance.LegacyLoad(mActiveGamePackage);
                        }
                        ScriptManager.Instance.Output("Package Load Finished");
                    }
                }
                finally
                {
                    AccessibilityRule.ClearCaches();

                    mbReloadInProgress = false;

                    ItemDatabase.Instance.BuildCodeIndex();

                    if (OnPackageLoadComplete != null)
                        OnPackageLoadComplete(this, EventArgs.Empty);

                    if (!SuspendPackReadyEvent)
                        ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.PackReady);
                }
            }
        }

#endregion

#region -- Incremental Load Wrappers --

        public void AddItems(string path)
        {
            if (ActiveGamePackage != null)
                ItemDatabase.Instance.IncrementalLoad(path, ActiveGamePackage);
        }

        public void AddMaps(string path)
        {
            if (ActiveGamePackage != null)
                MapDatabase.Instance.IncrementalLoad(path, ActiveGamePackage);
        }

        public void AddLocations(string path)
        {
            if (ActiveGamePackage != null)
                LocationDatabase.Instance.IncrementalLoad(path, ActiveGamePackage);
        }

        public void AddLayouts(string path)
        {
            if (ActiveGamePackage != null)
                LayoutManager.Instance.IncrementalLoad(path, ActiveGamePackage);
        }

        #endregion

    }
}
