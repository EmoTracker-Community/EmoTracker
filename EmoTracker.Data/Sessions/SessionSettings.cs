using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 7.3: per-state user-toggleable accessibility / UI behavior
    /// settings, split off from <see cref="ApplicationSettings"/>. Owned
    /// by each <see cref="TrackerState"/>; mutations route through the
    /// state's transaction processor (KV-mutable + OnChanged hooks where
    /// needed). Fork-time COW gives each fork its own settings snapshot.
    ///
    /// <para>
    /// The seven settings moved here from <see cref="ApplicationSettings"/>:
    /// <list type="bullet">
    ///   <item><c>IgnoreAllLogic</c> — disables logic for every accessibility evaluation.</item>
    ///   <item><c>DisplayAllLocations</c> — show all locations regardless of accessibility.</item>
    ///   <item><c>AlwaysAllowClearing</c> — allow chest clearing even on inaccessible sections.</item>
    ///   <item><c>AutoUnpinLocationsOnClear</c> — unpin a location when its sections clear.</item>
    ///   <item><c>PinLocationsOnItemCapture</c> — auto-pin a location on item capture.</item>
    ///   <item><c>MapEnabled</c> — show the map panel.</item>
    ///   <item><c>SwapLeftRight</c> — mirror layout (dock + margin) horizontally.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// During Phase 7.3, <see cref="ApplicationSettings.Instance"/>'s
    /// equivalent properties act as forwarders to the active state's
    /// SessionSettings (with a backing-field fallback for the no-active-
    /// state seed used when constructing a fresh state). This preserves
    /// existing UI binding and code-behind code paths until Phase 7.6
    /// switches them to a per-window <c>WindowContext.ActiveState.Settings</c>
    /// binding.
    /// </para>
    /// </summary>
    public partial class SessionSettings : TransactableModelTypeBase
    {
        // ----------- Defaults (match pre-Phase-7 ApplicationSettings) ---------
        // These initialize the seed values consumed by SessionSettings.NewWithDefaults
        // and used as fallbacks by ApplicationSettings forwarders before any
        // state is active.
        internal const bool DefaultIgnoreAllLogic = false;
        internal const bool DefaultDisplayAllLocations = false;
        internal const bool DefaultAlwaysAllowClearing = false;
        internal const bool DefaultAutoUnpinLocationsOnClear = true;
        internal const bool DefaultPinLocationsOnItemCapture = true;
        internal const bool DefaultMapEnabled = true;
        internal const bool DefaultSwapLeftRight = false;

        // ----------- KV-mutable properties ------------------------------------

        [KVMutable]
        [OnChanged(nameof(OnIgnoreAllLogicChanged))]
        public partial bool IgnoreAllLogic { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnDisplayAllLocationsChanged))]
        public partial bool DisplayAllLocations { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnAlwaysAllowClearingChanged))]
        public partial bool AlwaysAllowClearing { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnAutoUnpinLocationsOnClearChanged))]
        public partial bool AutoUnpinLocationsOnClear { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnPinLocationsOnItemCaptureChanged))]
        public partial bool PinLocationsOnItemCapture { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnMapEnabledChanged))]
        public partial bool MapEnabled { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnSwapLeftRightChanged))]
        public partial bool SwapLeftRight { get; set; }

        // ----------- Construction ---------------------------------------------

        public SessionSettings()
        {
            // Seed defaults on construction. The KV-mutable backing dict
            // starts empty; setting through the partial property writes
            // into MutableData and raises PropertyChanging / PropertyChanged.
            // We bypass the property setters here to avoid spurious
            // PropertyChanged events on a brand-new instance — the partial
            // property getters will still return the default value through
            // MutableData.GetValue<bool>'s defaultValue parameter.
            //
            // NOTE: the source generator emits the partial property
            // implementation reading MutableData.GetValue<T>(key, default(T)).
            // For our defaults to take effect we DO need the values
            // populated in MutableData on construction; otherwise
            // bool defaults to false everywhere and AutoUnpinLocationsOnClear
            // / PinLocationsOnItemCapture / MapEnabled would silently flip
            // from `true` to `false`. Drive each through its setter so the
            // values land in MutableData; PropertyChanged fires on a brand-
            // new instance (no observers yet) is harmless.
            IgnoreAllLogic = DefaultIgnoreAllLogic;
            DisplayAllLocations = DefaultDisplayAllLocations;
            AlwaysAllowClearing = DefaultAlwaysAllowClearing;
            AutoUnpinLocationsOnClear = DefaultAutoUnpinLocationsOnClear;
            PinLocationsOnItemCapture = DefaultPinLocationsOnItemCapture;
            MapEnabled = DefaultMapEnabled;
            SwapLeftRight = DefaultSwapLeftRight;
        }

        // ----------- OnChanged hooks ------------------------------------------

        /// <summary>
        /// Set by <see cref="TrackerState.Fork"/> around its settings
        /// bulk-copy so propagating values into a fork doesn't fire
        /// side-effectful OnChanged hooks (e.g. a pack reload) against the
        /// half-built fork state.
        /// </summary>
        internal bool SuppressOnChangedHooks;

        protected void OnSwapLeftRightChanged() => ReloadOwnerState();
        protected void OnMapEnabledChanged() => ReloadOwnerState();

        protected void OnDisplayAllLocationsChanged() => SyncForwarder(nameof(DisplayAllLocations));
        protected void OnAlwaysAllowClearingChanged() => SyncForwarder(nameof(AlwaysAllowClearing));
        protected void OnAutoUnpinLocationsOnClearChanged() => SyncForwarder(nameof(AutoUnpinLocationsOnClear));
        protected void OnPinLocationsOnItemCaptureChanged() => SyncForwarder(nameof(PinLocationsOnItemCapture));

        protected void OnIgnoreAllLogicChanged()
        {
            // Drive a refresh on the owning state's LocationDatabase so the
            // accessibility cascade reflects the new logic flag. This is
            // the per-state replacement for the legacy callback in
            // ApplicationSettings.IgnoreAllLogic's setter.
            var state = OwnerState as TrackerState;
            state?.Locations.RefreshAccessibility();
            SyncForwarder(nameof(IgnoreAllLogic));
        }

        /// <summary>
        /// SwapLeftRight and MapEnabled are consumed at pack parse time
        /// (<c>LayoutItem.TryParse</c> mirrors dock/margin definition
        /// values; <c>MapPanel.TryParseInternal</c> bails when the map is
        /// disabled), so a new value only takes effect after re-parsing
        /// the pack — the legacy <c>Tracker</c> setters called
        /// <c>Reload()</c> for the same reason. No pack loaded means
        /// nothing to re-parse, so skip rather than pointlessly resetting
        /// empty catalogs.
        /// </summary>
        void ReloadOwnerState()
        {
            if (SuppressOnChangedHooks) return;
            var state = OwnerState as TrackerState;
            if (state?.PackageInstance?.GamePackage == null) return;
            state.Reload();
        }

        /// <summary>
        /// <see cref="ApplicationSettings"/> keeps forwarder properties
        /// (plus seeds persisted to ApplicationSettings.json) for code and
        /// XAML bound to the process-wide singleton rather than a state's
        /// Settings (e.g. LocationMapControl's DisplayAllLocations
        /// multibinding). Writes through this state's setters must fan out
        /// to the singleton or those bindings go stale — that asymmetry is
        /// why the "Show All Locations" menu toggle (bound per-state) had
        /// no visible effect while the F11 hotkey (routed through the
        /// forwarder) worked.
        /// </summary>
        void SyncForwarder(string propertyName)
        {
            if (SuppressOnChangedHooks) return;
            ApplicationSettings.Instance.SyncSeedsFromSession(this, propertyName);
        }

        // ----------- Fork support ---------------------------------------------

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = new SessionSettings();
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            return copy;
        }
    }
}
