using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Layout;
using System;

namespace EmoTracker.Data.Session
{
    /// <summary>
    /// Aggregates all per-session tracker state. Phase 1: passive aggregator —
    /// holds references to existing singletons without changing their ownership.
    /// Later phases move authoritative ownership here and split immutable data
    /// out into shared catalogs.
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
        public ApplicationSettings Settings { get; }
        public ITransactionProcessor Transactions => TransactionProcessor.Current;

        private TrackerSession()
        {
            // Capture references in the same dependency order that existing
            // singleton lazy-init produces. Settings first (other singletons
            // may read settings during construction), then databases, then
            // Tracker which orchestrates them.
            Settings = ApplicationSettings.Instance;
            Items = ItemDatabase.Instance;
            Locations = LocationDatabase.Instance;
            Maps = MapDatabase.Instance;
            Layouts = LayoutManager.Instance;
            Scripts = ScriptManager.Instance;
            Tracker = Tracker.Instance;
        }

        /// <summary>
        /// Constructs the process-wide session. Must be called once during
        /// application startup, after transaction processor registration and
        /// settings load.
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
