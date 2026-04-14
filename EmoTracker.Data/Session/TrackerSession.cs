using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Items;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using System;

namespace EmoTracker.Data.Session
{
    /// <summary>
    /// Aggregates all per-session tracker state. Phase 2: owns the transaction
    /// processor and session-scoped settings; singletons still back the other
    /// components (catalogs, databases) and will move to session ownership in
    /// later phases.
    /// </summary>
    public class TrackerSession : ObservableObject
    {
        public static TrackerSession Current { get; private set; }

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
            // Phase 4: publish ourselves as the Current session up front, before
            // any singleton lazy-init below can construct objects whose
            // transactable property access resolves through TrackerSession.Current
            // (e.g. LocationDatabase.Instance creating the root Location, which
            // inherits LocationVisualProperties.PropertyStore → LocationStates).
            // Without this the first transactable writes would land in a per-
            // instance dict that later reads (now routed to the session store)
            // would never see.
            Current = this;

            // Settings first; it drives persisted flag loading and has no
            // singleton dependencies.
            Global = ApplicationSettings.Instance;

            // Phase 4: location-tree mutable state + accessibility cache must
            // exist before LocationDatabase is touched (its lazy init constructs
            // the root Location, which on first transactable-property access
            // resolves through TrackerSession.Current.LocationStates — that's
            // why we set Current before reading Instance below).
            LocationStates = new LocationStateStore();
            Evaluator = new AccessibilityEvaluator();

            // LocationDatabase must be constructed before the transaction
            // processor so the processor can inject it for Undo(); nothing in
            // LocationDatabase's lazy-init path touches TransactionProcessor.
            Locations = LocationDatabase.Instance;

            // Register the session's processor BEFORE any TransactableObject
            // construction happens via the remaining lazy singleton inits.
            Transactions = new Core.Transactions.Processors.LocalTransactionProcessorWithUndo(Locations);
            TransactionProcessor.SetTransactionProcessor(Transactions);

            Items = ItemDatabase.Instance;
            Maps = MapDatabase.Instance;
            Layouts = LayoutManager.Instance;
            Scripts = ScriptManager.Instance;

            Settings = new SessionSettings(Global);

            Tracker = Tracker.Instance;
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
