using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Layout;
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
        public LocationDatabase Locations { get; }
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
            // Settings first; it drives persisted flag loading and has no
            // singleton dependencies.
            Global = ApplicationSettings.Instance;

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

            Current = new TrackerSession();
            return Current;
        }
    }
}
