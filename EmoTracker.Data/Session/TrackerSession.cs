using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Items;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using System;

namespace EmoTracker.Data.Session
{
    /// <summary>
    /// Aggregates all per-session tracker state. The session owns the transaction
    /// processor, session-scoped settings facade, item state store, location state
    /// store, accessibility evaluator, and the seven former data-layer singletons
    /// (<c>Tracker</c>, <c>ItemDatabase</c>, <c>LocationDatabase</c>,
    /// <c>MapDatabase</c>, <c>LayoutManager</c>, <c>ScriptManager</c>,
    /// <c>ApplicationSettings</c>).
    ///
    /// Phase 6 retired the <c>.Instance</c> accessors on those seven types: all
    /// callers (C# + XAML) now route through <c>TrackerSession.Current</c>, and
    /// the types themselves are plain <c>ObservableObject</c>s constructed by the
    /// session ctor in dependency order. <see cref="DesignInstance"/> bootstraps
    /// a minimal session for XAML design-time previewers.
    ///
    /// Note: <c>ApplicationModel</c>, <c>PackageManager</c>, and
    /// <c>ExtensionManager</c> remain singletons by design — they live in the UI
    /// / Extensions layer, outside the session's ownership boundary.
    /// </summary>
    public class TrackerSession : ObservableObject
    {
        public static TrackerSession Current { get; private set; }

        /// <summary>
        /// Design-time-only session handle for XAML previewers. Returns the
        /// runtime <c>Current</c> if one exists; otherwise a freshly-built
        /// session. The previewer never tears this down, so we simply leak it
        /// for the lifetime of the design surface.
        /// </summary>
        public static TrackerSession DesignInstance
        {
            get
            {
                if (Current != null)
                    return Current;

                // Bootstrap a minimal session so XAML design-time bindings have
                // something to resolve against. Only safe to call from the
                // designer process; the runtime always has Current set early in
                // App startup.
                return CreateCurrent();
            }
        }

        public Tracker Tracker { get; }
        public ItemDatabase Items { get; }

        /// <summary>
        /// Read-only view of the loaded item set (Phase 3 of the refactor).
        /// Forwards to <c>Items.Catalog</c> for convenience; conceptually the
        /// shared/immutable half of the item state split.
        /// </summary>
        public ItemCatalog ItemCatalog => Items?.Catalog;

        /// <summary>
        /// Per-session mutable property store backing every item's transactable
        /// properties (Phase 3). Future <c>Fork()</c> deep-copies this store to
        /// give a forked session independent item state without recreating the
        /// item objects.
        /// </summary>
        public ItemStateStore ItemStates => Items?.States;

        public LocationDatabase Locations { get; }

        /// <summary>
        /// Per-session mutable property store backing every Location and Section's
        /// transactable properties (Phase 4). Future <c>Fork()</c> deep-copies this
        /// store to give a forked session independent location/section state
        /// without recreating Location or Section objects.
        /// </summary>
        public LocationStateStore LocationStates { get; }

        /// <summary>
        /// Per-session accessibility evaluator (Phase 4). Hosts what was a
        /// process-wide static cache on <see cref="AccessibilityRule"/>; routed
        /// through here so a forked session's evaluation doesn't poison the
        /// parent's cache once item state diverges.
        /// </summary>
        public AccessibilityEvaluator Evaluator { get; }
        public MapDatabase Maps { get; }
        public LayoutManager Layouts { get; }
        public ScriptManager Scripts { get; }

        /// <summary>Global (non-session) settings: persisted UI preferences, app-wide state.</summary>
        public ApplicationSettings Global { get; }

        /// <summary>Session-scoped tracker flags (IgnoreAllLogic, DisplayAllLocations, etc.).</summary>
        public SessionSettings Settings { get; }

        /// <summary>Transaction processor owned by this session. Undo stack is session-local.</summary>
        public ITransactionProcessor Transactions { get; }

        private TrackerSession()
        {
            // Publish ourselves as the Current session up front, before any
            // subsystem ctor below can construct objects whose transactable
            // property access resolves through TrackerSession.Current (e.g.
            // LocationDatabase's root Location, which inherits
            // LocationVisualProperties.PropertyStore → LocationStates). Without
            // this the first transactable writes would land in a per-instance
            // dict that later reads (now routed to the session store) would
            // never see.
            Current = this;

            // Settings first; drives persisted flag loading. ApplicationSettings'
            // parameterless ctor reads application_settings.json from disk.
            Global = new ApplicationSettings();

            // Location-tree mutable state + accessibility cache must exist
            // before LocationDatabase is touched — its ctor constructs the root
            // Location, which on first transactable-property access resolves
            // through TrackerSession.Current.LocationStates.
            LocationStates = new LocationStateStore();
            Evaluator = new AccessibilityEvaluator();

            // LocationDatabase must be constructed before the transaction
            // processor so the processor can inject it for Undo().
            Locations = new LocationDatabase();

            // Register the session's processor BEFORE any TransactableObject
            // construction happens via the remaining subsystem ctors below.
            Transactions = new Core.Transactions.Processors.LocalTransactionProcessorWithUndo(Locations);
            TransactionProcessor.SetTransactionProcessor(Transactions);

            Items = new ItemDatabase();
            Maps = new MapDatabase();
            Layouts = new Layout.LayoutManager();
            Scripts = new ScriptManager();

            Settings = new SessionSettings(Global);

            Tracker = new Tracker();
        }

        /// <summary>
        /// Constructs the process-wide session. Must be called once during
        /// application startup, after settings load.
        /// </summary>
        public static TrackerSession CreateCurrent()
        {
            if (Current != null)
                throw new InvalidOperationException("TrackerSession.Current is already constructed.");

            // The constructor sets Current = this internally (see comment in
            // ctor). We just kick it off and return the handle.
            return new TrackerSession();
        }
    }
}
