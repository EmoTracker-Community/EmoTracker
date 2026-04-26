using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    /// <summary>
    /// Phase 7.2: per-state cache for <see cref="AccessibilityRule"/>
    /// evaluations. One instance per <see cref="LocationDatabase"/> (and
    /// therefore one per <see cref="Sessions.TrackerState"/>); replaces the
    /// previous static <c>AccessibilityRule.mAccessiblityCache</c> dictionary.
    ///
    /// <para>
    /// The cache maps an item code → the most recently evaluated
    /// <see cref="AccessibilityLevel"/> + provider count for that code in the
    /// owning state. It is populated lazily during rule evaluation
    /// (<see cref="AccessibilityRule.GetAccessibilityLevel"/>) and cleared
    /// wholesale by <see cref="LocationDatabase.RefeshAccessibility"/> at the
    /// top of each refresh sweep so a single sweep sees a coherent snapshot
    /// of provider counts.
    /// </para>
    ///
    /// <para>
    /// On <c>TrackerState.Fork</c> the cache is deep-copied (per entry) so
    /// the fork starts pre-warmed with the source's evaluations and diverges
    /// independently as forks mutate items.
    /// </para>
    /// </summary>
    internal sealed class AccessibilityRuleCache
    {
        // Cache is enabled by default. The legacy static
        // AccessibilityRule.EnableCache toggle (used by tests + a debug
        // setting) maps to this instance flag — flipping it off clears
        // the dictionary as a side effect, matching pre-Phase-7 behavior.
        bool mEnabled = true;
        public bool Enabled
        {
            get { return mEnabled; }
            set
            {
                mEnabled = value;
                if (!mEnabled) Clear();
            }
        }

        public struct Entry
        {
            public AccessibilityLevel Level;
            public uint ProvidedCount;
        }

        readonly Dictionary<string, Entry> mEntries = new Dictionary<string, Entry>();

        public bool TryGet(string code, out AccessibilityLevel level, out uint count)
        {
            if (!mEnabled)
            {
                level = AccessibilityLevel.None;
                count = 0;
                return false;
            }
            if (mEntries.TryGetValue(code, out var entry))
            {
                level = entry.Level;
                count = entry.ProvidedCount;
                return true;
            }
            level = AccessibilityLevel.None;
            count = 0;
            return false;
        }

        public void Put(string code, AccessibilityLevel level, uint count)
        {
            if (!mEnabled) return;
            mEntries[code] = new Entry { Level = level, ProvidedCount = count };
        }

        public void Clear()
        {
            mEntries.Clear();
        }

        /// <summary>
        /// Phase 7.2: produce a deep copy of this cache for use as the
        /// initial cache of a forked <see cref="Sessions.TrackerState"/>.
        /// Entries are value types so the dictionary copy is sufficient;
        /// fork-side mutations do not affect the source.
        /// </summary>
        public AccessibilityRuleCache CloneForFork()
        {
            var clone = new AccessibilityRuleCache { mEnabled = this.mEnabled };
            foreach (var pair in mEntries)
                clone.mEntries[pair.Key] = pair.Value;
            return clone;
        }

        /// <summary>
        /// Replaces this cache's contents with a snapshot of <paramref name="other"/>.
        /// Used by <see cref="LocationDatabase.SeedRuleCacheFromFork"/>.
        /// </summary>
        internal void AdoptFrom(AccessibilityRuleCache other)
        {
            mEntries.Clear();
            if (other == null) return;
            mEnabled = other.mEnabled;
            foreach (var pair in other.mEntries)
                mEntries[pair.Key] = pair.Value;
        }

        // Test affordance: number of cached entries.
        internal int Count => mEntries.Count;
    }
}
