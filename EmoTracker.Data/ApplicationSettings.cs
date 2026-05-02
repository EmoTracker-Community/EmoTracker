using EmoTracker.Core;
using EmoTracker.Core.Services;
using EmoTracker.Data.JSON;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace EmoTracker.Data
{


    public class ApplicationSettings : ObservableSingleton<ApplicationSettings>
    {
        readonly Dictionary<string, string> mProviderSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string GetProviderSetting(string key, string defaultValue = null)
        {
            if (mProviderSettings.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public void SetProviderSetting(string key, string value)
        {
            if (value == null)
                mProviderSettings.Remove(key);
            else
                mProviderSettings[key] = value;
            WriteSettings();
        }

        double mInitialX = double.NaN;
        double mInitialY = double.NaN;
        double mInitialWidth = -1.0;
        double mInitialHeight = -1.0;
        bool mbInitialMaximized = false;
        double mNDIFrameRate = 30.0;
        int mNDIOutputScale = 1;
        bool mbEnableBackgroundNdi = true;
        bool mbEnableAutoUpdateCheck = true;
        bool mbAlwaysOnTop = false;
        bool mbEnableDiscordRichPresence = false;
        bool mbEnableVoice = true;
        bool mbEnableNoteTaking = true;
        bool mbEnableVariantSwitcher = false;
        bool mbPromptOnRefreshClose = false;
        string mbVoiceInputDeviceName;

        // Phase 7.3: these seven settings moved to per-state SessionSettings.
        // The fields here serve as a "seed" that's:
        //   - Used as the source for new TrackerStates' Settings (via SeedIntoSession).
        //   - Kept in sync with the active state's Settings on writes (so
        //     reads from code that doesn't have an OwnerState handle still
        //     observe the user's most recent toggle).
        //   - Persisted to ApplicationSettings.json as a fallback / migration
        //     source for pre-Phase-7 saves.
        //
        // Direct reads / writes via ApplicationSettings.Instance.X are
        // forwarders to the active state's Settings if any; otherwise the
        // backing seed field is used.
        bool mbSeedDisplayAllLocations = Sessions.SessionSettings.DefaultDisplayAllLocations;
        bool mbSeedIgnoreAllLogic = Sessions.SessionSettings.DefaultIgnoreAllLogic;
        bool mbSeedAutoUnpinLocationsOnClear = Sessions.SessionSettings.DefaultAutoUnpinLocationsOnClear;
        bool mbSeedAlwaysAllowClearing = Sessions.SessionSettings.DefaultAlwaysAllowClearing;
        bool mbSeedPinLocationsOnItemCapture = Sessions.SessionSettings.DefaultPinLocationsOnItemCapture;

        // Phase 7.1 cleanup: ApplicationSettings is a process-wide singleton
        // and must NOT consult an ambient state slot. Cross-assembly hookup:
        // ApplicationModel installs <see cref="ActiveSessionSettingsResolver"/>
        // at startup so per-state values flow through whichever state the
        // app currently treats as the active primary. The resolver, like
        // <see cref="Data.Tracker.ResolveReloadTarget"/>, evaluates the
        // relevant state at call time — no slot is held here.
        public static Func<Sessions.SessionSettings> ActiveSessionSettingsResolver { get; set; }

        Sessions.SessionSettings ActiveSessionSettings
            => ActiveSessionSettingsResolver?.Invoke();

        /// <summary>
        /// Phase 7.3: copy this app-settings instance's seed values onto a
        /// freshly-constructed <see cref="Sessions.SessionSettings"/>.
        /// Called from <see cref="Sessions.TrackerState"/>'s adoption ctor
        /// so a primary state inherits the user's pre-existing preferences
        /// loaded from <c>ApplicationSettings.json</c>.
        /// </summary>
        internal void SeedIntoSession(Sessions.SessionSettings target)
        {
            if (target == null) return;
            target.IgnoreAllLogic = mbSeedIgnoreAllLogic;
            target.DisplayAllLocations = mbSeedDisplayAllLocations;
            target.AlwaysAllowClearing = mbSeedAlwaysAllowClearing;
            target.AutoUnpinLocationsOnClear = mbSeedAutoUnpinLocationsOnClear;
            target.PinLocationsOnItemCapture = mbSeedPinLocationsOnItemCapture;
        }

        public bool IgnoreAllLogic
        {
            get
            {
                var s = ActiveSessionSettings;
                return s != null ? s.IgnoreAllLogic : mbSeedIgnoreAllLogic;
            }
            set
            {
                // Update both the seed and (if any) the active state's
                // setting. The state's setter triggers RefeshAccessibility
                // via its OnChanged hook, mirroring the legacy behavior.
                bool changed = false;
                if (SetProperty(ref mbSeedIgnoreAllLogic, value))
                    changed = true;
                var s = ActiveSessionSettings;
                if (s != null && s.IgnoreAllLogic != value)
                {
                    s.IgnoreAllLogic = value;
                    changed = true;
                }
                if (changed)
                    NotifyPropertyChanged(nameof(IgnoreAllLogic));
            }
        }

        public bool AutoUnpinLocationsOnClear
        {
            get
            {
                var s = ActiveSessionSettings;
                return s != null ? s.AutoUnpinLocationsOnClear : mbSeedAutoUnpinLocationsOnClear;
            }
            set
            {
                SetProperty(ref mbSeedAutoUnpinLocationsOnClear, value);
                var s = ActiveSessionSettings;
                if (s != null && s.AutoUnpinLocationsOnClear != value)
                    s.AutoUnpinLocationsOnClear = value;
                NotifyPropertyChanged(nameof(AutoUnpinLocationsOnClear));
            }
        }

        public bool AlwaysAllowClearing
        {
            get
            {
                var s = ActiveSessionSettings;
                return s != null ? s.AlwaysAllowClearing : mbSeedAlwaysAllowClearing;
            }
            set
            {
                SetProperty(ref mbSeedAlwaysAllowClearing, value);
                var s = ActiveSessionSettings;
                if (s != null && s.AlwaysAllowClearing != value)
                    s.AlwaysAllowClearing = value;
                NotifyPropertyChanged(nameof(AlwaysAllowClearing));
            }
        }

        public bool PinLocationsOnItemCapture
        {
            get
            {
                var s = ActiveSessionSettings;
                return s != null ? s.PinLocationsOnItemCapture : mbSeedPinLocationsOnItemCapture;
            }
            set
            {
                SetProperty(ref mbSeedPinLocationsOnItemCapture, value);
                var s = ActiveSessionSettings;
                if (s != null && s.PinLocationsOnItemCapture != value)
                    s.PinLocationsOnItemCapture = value;
                NotifyPropertyChanged(nameof(PinLocationsOnItemCapture));
            }
        }

        public bool DisplayAllLocations
        {
            get
            {
                var s = ActiveSessionSettings;
                return s != null ? s.DisplayAllLocations : mbSeedDisplayAllLocations;
            }
            set
            {
                SetProperty(ref mbSeedDisplayAllLocations, value);
                var s = ActiveSessionSettings;
                if (s != null && s.DisplayAllLocations != value)
                    s.DisplayAllLocations = value;
                NotifyPropertyChanged(nameof(DisplayAllLocations));
            }
        }

        bool mbFastTooltips = false;
        public bool FastToolTips
        {
            get { return mbFastTooltips; }
            set { SetProperty(ref mbFastTooltips, value); }
        }

        bool mbSupportLua53VersionChecks = false;
        public bool SupportLua53VersionChecks
        {
            get { return mbSupportLua53VersionChecks; }
            set { SetProperty(ref mbSupportLua53VersionChecks, value); }
        }

        string mServiceBaseURL = "https://emotracker-community.github.io/EmoTracker-Service/service/";
        string mTwitchChannelName;
        string mLastActivePackage;
        string mLastActivePackageVariant;
        string mCommandLinePackage;
        string mCommandLinePackageVariant;
        bool mNoAsyncImages;

        ObservableCollection<string> mPackageRepositories = new ObservableCollection<string>();

        public double InitialX
        {
            get { return mInitialX; }
            set { SetProperty(ref mInitialX, value); }
        }

        public double InitialY
        {
            get { return mInitialY; }
            set { SetProperty(ref mInitialY, value); }
        }

        public double InitialWidth
        {
            get { return mInitialWidth; }
            set { SetProperty(ref mInitialWidth, value); }
        }

        public double InitialHeight
        {
            get { return mInitialHeight; }
            set { SetProperty(ref mInitialHeight, value); }
        }

        public bool InitialMaximized
        {
            get { return mbInitialMaximized; }
            set { SetProperty(ref mbInitialMaximized, value); }
        }

        public bool AlwaysOnTop
        {
            get { return mbAlwaysOnTop; }
            set { SetProperty(ref mbAlwaysOnTop, value); }
        }

        public bool EnableVoiceControl
        {
            get { return mbEnableVoice; }
            set { SetProperty(ref mbEnableVoice, value); }
        }

        public bool EnableNoteTaking
        {
            get { return mbEnableNoteTaking; }
            set { SetProperty(ref mbEnableNoteTaking, value); }
        }

        public bool EnableVariantSwitcher
        {
            get { return mbEnableVariantSwitcher; }
            set { SetProperty(ref mbEnableVariantSwitcher, value); }
        }

        public string VoiceInputDeviceName
        {
            get { return mbVoiceInputDeviceName; }
            set { SetProperty(ref mbVoiceInputDeviceName, value); }
        }

        public bool PromptOnRefreshClose
        {
            get { return mbPromptOnRefreshClose; }
            set { SetProperty(ref mbPromptOnRefreshClose, value); }
        }

        public bool EnableDiscordRichPresence
        {
            get { return mbEnableDiscordRichPresence; }
            set { SetProperty(ref mbEnableDiscordRichPresence, value); }
        }

        public string ServiceBaseURL
        {
            get { return mServiceBaseURL; }
            set { SetProperty(ref mServiceBaseURL, value); }
        }

        public string LastActivePackage
        {
            get { return mLastActivePackage; }
            set { SetProperty(ref mLastActivePackage, value); }
        }

        public string LastActivePackageVariant
        {
            get { return mLastActivePackageVariant; }
            set { SetProperty(ref mLastActivePackageVariant, value); }
        }

        public string CommandLinePackage
        {
            get { return mCommandLinePackage;  }
            set { SetProperty(ref mCommandLinePackage, value); }
        }

        public string CommandLinePackageVariant
        {
            get { return mCommandLinePackageVariant; }
            set { SetProperty(ref mCommandLinePackageVariant, value); }
        }

        /// <summary>
        /// When true, disables async background image pre-caching and forces
        /// synchronous image resolution on the UI thread (the pre-refactor
        /// behavior).  Set via the <c>--no-async-images</c> command-line flag.
        /// </summary>
        public bool NoAsyncImages
        {
            get { return mNoAsyncImages; }
            set { SetProperty(ref mNoAsyncImages, value); }
        }

        public string TwitchChannelName
        {
            get { return mTwitchChannelName; }
            set { SetProperty(ref mTwitchChannelName, value); }
        }
        public double NdiFrameRate
        {
            get { return mNDIFrameRate; }
            set { SetProperty(ref mNDIFrameRate, Math.Max(value, 1.0)); }
        }
        public int NdiOutputScale
        {
            get { return mNDIOutputScale; }
            set { SetProperty(ref mNDIOutputScale, Math.Max(value, 1)); }
        }

        /// <summary>
        /// When enabled (default), the broadcast view renders to a hidden off-screen
        /// window so the NDI source is advertised on the network and frames flow to
        /// receivers whether or not the user has opened the visible broadcast view.
        /// When disabled, NDI is only broadcast while the visible broadcast view
        /// window is open (legacy behaviour).
        /// </summary>
        public bool EnableBackgroundNdi
        {
            get { return mbEnableBackgroundNdi; }
            set { SetProperty(ref mbEnableBackgroundNdi, value); }
        }

        public bool EnableAutoUpdateCheck
        {
            get { return mbEnableAutoUpdateCheck; }
            set { SetProperty(ref mbEnableAutoUpdateCheck, value); }
        }

        public IEnumerable<string> AdditionalRepositories
        {
            get { return mPackageRepositories; }
        }

        public ApplicationSettings()
        {
            LoadSettings();

            this.PropertyChanged += LocalPropertyChanged;
            mPackageRepositories.CollectionChanged += RepositoriesModified;
        }

        private void RepositoriesModified(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
        }

        private void LocalPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            WriteSettings();
        }

        private void LoadSettings()
        {
            try
            {
                string path = Path.Combine(UserDirectory.Path, "application_settings.json");
                if (File.Exists(path))
                {
                    using (StreamReader reader = new StreamReader(File.OpenRead(Path.Combine(UserDirectory.Path, "application_settings.json"))))
                    {
                        JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));

                        InitialX = root.GetValue<double>("initial_x", double.NaN);
                        InitialY = root.GetValue<double>("initial_y", double.NaN);
                        InitialWidth = root.GetValue<double>("initial_width", -1.0);
                        InitialHeight = root.GetValue<double>("initial_height", -1.0);
                        InitialMaximized = root.GetValue<bool>("initial_maximized", false);
                        NdiFrameRate = root.GetValue<double>("ndi_frame_rate", 30.0);
                        NdiOutputScale = root.GetValue<int>("ndi_output_scale", 1);
                        EnableBackgroundNdi = root.GetValue<bool>("enable_background_ndi", true);
                        EnableAutoUpdateCheck = root.GetValue<bool>("enable_auto_update_check", true);
                        AlwaysOnTop = root.GetValue<bool>("always_on_top", false);
                        EnableDiscordRichPresence = root.GetValue<bool>("discord_rich_presence", false);
                        EnableVoiceControl = root.GetValue<bool>("enable_voice_control", true);
                        EnableNoteTaking = root.GetValue<bool>("enable_note_taking", true);
                        EnableVariantSwitcher = root.GetValue<bool>("enable_variant_switcher", false);
                        VoiceInputDeviceName = root.GetValue<string>("voice_input_device_name");
                        PromptOnRefreshClose = root.GetValue<bool>("prompt_on_refresh_close", false);
                        LastActivePackage = root.GetValue<string>("last_active_package");
                        LastActivePackageVariant = root.GetValue<string>("last_active_package_variant");
                        TwitchChannelName = root.GetValue<string>("twitch_channel");
                        ServiceBaseURL = root.GetValue<string>("service_base_url", ServiceBaseURL);

                        IgnoreAllLogic = root.GetValue<bool>("tracking_ignore_all_logic", false);
                        AlwaysAllowClearing = root.GetValue<bool>("tracking_always_allow_clearing_locations", false);
                        DisplayAllLocations = root.GetValue<bool>("tracking_display_all_locations", false);
                        AutoUnpinLocationsOnClear = root.GetValue<bool>("tracking_auto_unpin_locations_on_clear", true);
                        PinLocationsOnItemCapture = root.GetValue<bool>("tracking_pin_locations_on_item_capture", true);

                        FastToolTips = root.GetValue<bool>("assistance_fast_tool_tips", false);
                        SupportLua53VersionChecks = root.GetValue<bool>("lua_support_53_version_checks", false);

                        JArray repositories = root.GetValue<JArray>("package_repositories");
                        if (repositories != null)
                        {
                            foreach (string url in repositories)
                            {
                                if (!string.IsNullOrWhiteSpace(url) && !mPackageRepositories.Contains(url, StringComparer.OrdinalIgnoreCase))
                                    mPackageRepositories.Add(url);
                            }
                        }

                        JObject providerSettings = root.GetValue<JObject>("provider_settings");
                        if (providerSettings != null)
                        {
                            foreach (var kvp in providerSettings)
                            {
                                if (kvp.Value != null && kvp.Value.Type == JTokenType.String)
                                    mProviderSettings[kvp.Key] = kvp.Value.Value<string>();
                            }
                        }
                    }
                }

                string[] cargs = Environment.GetCommandLineArgs();
                for(int n = 0; n < cargs.Length; n++)
                {
                    if (String.Equals(cargs[n], "--pack"))
                    {
                        n++;
                        if (n > cargs.Length-1)
                            { break; }

                        CommandLinePackage = cargs[n];

                    }
                    if (String.Equals(cargs[n], "--variant"))
                    {
                        n++;
                        if (n > cargs.Length - 1)
                        { break; }

                        CommandLinePackageVariant = cargs[n];

                    }

                    if (String.Equals(cargs[n], "--no-async-images"))
                    {
                        NoAsyncImages = true;
                    }

                }
            }
            catch
            {
            }
        }

        private void WriteSettings()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(File.Open(Path.Combine(UserDirectory.Path, "application_settings.json"), FileMode.Create)))
                {
                    using (JsonTextWriter jsonWriter = new JsonTextWriter(writer))
                    {
                        jsonWriter.AutoCompleteOnClose = true;
                        jsonWriter.Formatting = Formatting.Indented;

                        JObject root = new JObject();

                        if (!double.IsNaN(InitialX))
                            root.Add("initial_x", JToken.FromObject(InitialX));

                        if (!double.IsNaN(InitialY))
                            root.Add("initial_y", JToken.FromObject(InitialY));

                        if (InitialWidth >= 0.0)
                            root.Add("initial_width", JToken.FromObject(InitialWidth));

                        if (InitialHeight >= 0.0)
                            root.Add("initial_height", JToken.FromObject(InitialHeight));

                        root.Add("initial_maximized", JToken.FromObject(InitialMaximized));

                        if (NdiFrameRate > 1.0)
                            root.Add("ndi_frame_rate", JToken.FromObject(NdiFrameRate));

                        if (NdiOutputScale > 1)
                            root.Add("ndi_output_scale", JToken.FromObject(NdiOutputScale));

                        root.Add("enable_background_ndi", JToken.FromObject(EnableBackgroundNdi));
                        root.Add("enable_auto_update_check", JToken.FromObject(EnableAutoUpdateCheck));

                        root.Add("always_on_top", JToken.FromObject(AlwaysOnTop));
                        root.Add("discord_rich_presence", JToken.FromObject(EnableDiscordRichPresence));
                        root.Add("enable_voice_control", JToken.FromObject(EnableVoiceControl));
                        root.Add("enable_note_taking", JToken.FromObject(EnableNoteTaking));
                        root.Add("enable_variant_switcher", JToken.FromObject(EnableVariantSwitcher));
                        if (!string.IsNullOrWhiteSpace(VoiceInputDeviceName))
                            root.Add("voice_input_device_name", JToken.FromObject(VoiceInputDeviceName));
                        root.Add("prompt_on_refresh_close", JToken.FromObject(PromptOnRefreshClose));

                        if (!string.IsNullOrWhiteSpace(ServiceBaseURL))
                            root.Add("service_base_url", JToken.FromObject(ServiceBaseURL));

                        if (!string.IsNullOrWhiteSpace(LastActivePackage))
                            root.Add("last_active_package", JToken.FromObject(LastActivePackage));

                        if (!string.IsNullOrWhiteSpace(LastActivePackageVariant))
                            root.Add("last_active_package_variant", JToken.FromObject(LastActivePackageVariant));

                        if (!string.IsNullOrWhiteSpace(TwitchChannelName))
                            root.Add("twitch_channel", JToken.FromObject(TwitchChannelName));

                        root.Add("tracking_ignore_all_logic", JToken.FromObject(IgnoreAllLogic));
                        root.Add("tracking_always_allow_clearing_locations", JToken.FromObject(AlwaysAllowClearing));
                        root.Add("tracking_display_all_locations", JToken.FromObject(DisplayAllLocations));
                        root.Add("tracking_auto_unpin_locations_on_clear", JToken.FromObject(AutoUnpinLocationsOnClear));
                        root.Add("tracking_pin_locations_on_item_capture", JToken.FromObject(PinLocationsOnItemCapture));

                        root.Add("assistance_fast_tool_tips", JToken.FromObject(FastToolTips));
                        root.Add("lua_support_53_version_checks", JToken.FromObject(SupportLua53VersionChecks));

                        JArray reposVal = JArray.FromObject(AdditionalRepositories);
                        if (reposVal != null)
                            root.Add("package_repositories", reposVal);

                        if (mProviderSettings.Count > 0)
                        {
                            var providerObj = new JObject();
                            foreach (var kvp in mProviderSettings)
                                providerObj.Add(kvp.Key, JToken.FromObject(kvp.Value));
                            root.Add("provider_settings", providerObj);
                        }

                        jsonWriter.WriteToken(root.CreateReader());
                    }
                }
            }
            catch
            {
            }
        }
    }
}
