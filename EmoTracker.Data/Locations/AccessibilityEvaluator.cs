using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    /// <summary>
    /// Per-session accessibility evaluator. Phase 4 of the TrackerSession refactor:
    /// hosts what was previously a process-wide static cache on
    /// <see cref="AccessibilityRule"/> (the <c>mAccessiblityCache</c> dict and the
    /// <c>EnableCache</c> flag).
    ///
    /// Owning the cache per-session means a forked session sees its own evaluation
    /// results (and, more importantly, doesn't poison the cache of the parent
    /// session if its item state diverges). The cache is cleared whenever the
    /// session-owned <c>LocationDatabase.RefeshAccessibility()</c> runs.
    /// </summary>
    public class AccessibilityEvaluator
    {
        public struct Result
        {
            public AccessibilityLevel Level;
            public uint ProvidedCount;
        }

        readonly Dictionary<string, Result> mCache = new Dictionary<string, Result>();
        bool mbEnableCache = true;

        public bool EnableCache
        {
            get { return mbEnableCache; }
            set
            {
                mbEnableCache = value;
                ClearCaches();
            }
        }

        public void ClearCaches()
        {
            mCache.Clear();
        }

        /// <summary>
        /// Resolve the provider count for a code, consulting the cache when
        /// enabled. The provider is supplied by the caller so the evaluator stays
        /// independent of any global singleton ladder; in practice
        /// callers pass the session's <c>Tracker</c>.
        /// </summary>
        public uint GetProviderCountForCode(ICodeProvider provider, string code, out AccessibilityLevel maxAccessibility)
        {
            if (mbEnableCache)
            {
                if (mCache.TryGetValue(code, out Result cached))
                {
                    maxAccessibility = cached.Level;
                    return cached.ProvidedCount;
                }
            }

            uint count = provider.ProviderCountForCode(code, out AccessibilityLevel level);

            if (mbEnableCache)
            {
                mCache[code] = new Result { Level = level, ProvidedCount = count };
            }

            maxAccessibility = level;
            return count;
        }
    }
}
