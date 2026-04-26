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
        public partial bool DisplayAllLocations { get; set; }

        [KVMutable]
        public partial bool AlwaysAllowClearing { get; set; }

        [KVMutable]
        public partial bool AutoUnpinLocationsOnClear { get; set; }

        [KVMutable]
        public partial bool PinLocationsOnItemCapture { get; set; }

        [KVMutable]
        public partial bool MapEnabled { get; set; }

        [KVMutable]
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

        protected void OnIgnoreAllLogicChanged()
        {
            // Drive a refresh on the owning state's LocationDatabase so the
            // accessibility cascade reflects the new logic flag. This is
            // the per-state replacement for the legacy callback in
            // ApplicationSettings.IgnoreAllLogic's setter.
            var state = OwnerState as TrackerState;
            state?.Locations.RefeshAccessibility();
        }

        // ----------- Fork support ---------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = new SessionSettings();
            copy.InitializeAsForkOf(this);
            return copy;
        }
    }
}
