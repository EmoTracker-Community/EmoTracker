using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public class AccessibilityRule
    {
        // Phase 7.2: the static cache + EnableCache toggle moved off this
        // type onto a per-state AccessibilityRuleCache held by each
        // LocationDatabase. EnableCache is preserved as a process-wide
        // override that propagates to every state's cache on next access
        // (so unit tests that disable caching see consistent behavior).
        static bool sbEnableCache = true;

        /// <summary>
        /// Phase 7.2: the cache itself is per-state, but this app-wide flag
        /// is consulted at the top of each rule evaluation. When false, the
        /// rule bypasses the per-state cache entirely (read-through every
        /// call). Setting it to false also clears every active state's
        /// cache via the <see cref="EnableCacheChanged"/> hook installed by
        /// <see cref="LocationDatabase"/>.
        /// </summary>
        public static bool EnableCache
        {
            get { return sbEnableCache; }
            set
            {
                if (sbEnableCache == value) return;
                sbEnableCache = value;
                EnableCacheChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// Phase 7.2 hook: <see cref="LocationDatabase"/> subscribes so a
        /// process-wide <see cref="EnableCache"/> flip propagates to every
        /// per-state cache.
        /// </summary>
        internal static event Action<bool> EnableCacheChanged;

        static uint GetProviderCountForCode(string code, Sessions.TrackerState state, out AccessibilityLevel maxAccessibility)
        {
            // Phase 7.2: lookup goes through the per-state cache first, then
            // falls through to the state's ItemDatabase. Cache misses are
            // populated on the way out.
            var cache = state?.Locations?.RuleCache;

            if (sbEnableCache && cache != null && cache.TryGet(code, out var cachedLevel, out var cachedCount))
            {
                maxAccessibility = cachedLevel;
                return cachedCount;
            }

            // ICodeProvider for the lookup is the state's items. State may
            // be null in edge cases (rule evaluation outside any live
            // state), in which case we treat the code as unprovided.
            ICodeProvider provider = state?.Items;
            if (provider == null)
            {
                maxAccessibility = AccessibilityLevel.None;
                return 0;
            }

            uint count = provider.ProviderCountForCode(code, out maxAccessibility);

            if (sbEnableCache && cache != null)
                cache.Put(code, maxAccessibility, count);

            return count;
        }

        private static AccessibilityLevel Min(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a < b) ? a : b;
        }

        private static AccessibilityLevel Max(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a > b) ? a : b;
        }

        public class CodeRule
        {
            public string mCode;
            public bool mbIsSequenceBreakable = false;
            public bool mbIsGlitch = false;
            public uint mRequiredCount = 1;
        }

        HashSet<CodeRule> mCodes = new HashSet<CodeRule>();
        bool mbIsInspectable = false;

        public IEnumerable<CodeRule> Codes
        {
            get { return mCodes; }
        }

        public AccessibilityRule(string rule)
        {
            if (!string.IsNullOrWhiteSpace(rule))
            {
                if (rule.StartsWith("{") && rule.EndsWith("}"))
                {
                    mbIsInspectable = true;
                    rule = rule.Substring(1, rule.Length - 2);
                }

                string[] tokens = rule.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    CodeRule localRule = new CodeRule();

                    string localToken = token.Trim(' ', '\t', '\n');
                    if (localToken.StartsWith("[") && localToken.EndsWith("]"))
                    {
                        localRule.mbIsSequenceBreakable = true;
                        localToken = localToken.Substring(1, localToken.Length - 2);

                        if (localToken.StartsWith("[") && localToken.EndsWith("]"))
                        {
                            localRule.mbIsGlitch = true;
                            localToken = localToken.Substring(1, localToken.Length - 2);
                        }
                    }

                    string[] components = localToken.Split(':');

                    localRule.mCode = components[0].Trim();

                    if (components.Length > 1)
                    {
                        uint requiredCount = 1;
                        if (uint.TryParse(components[1], out requiredCount))
                            localRule.mRequiredCount = requiredCount;
                    }

                    mCodes.Add(localRule);
                }
            }
        }

        /// <summary>
        /// Phase 7.2: evaluates this rule against the given state's item
        /// catalog and per-state accessibility cache. Replaces the previous
        /// parameterless <c>AccessibilityLevel</c> property which read from
        /// a static cache + the singleton <c>Tracker.Instance</c>.
        /// </summary>
        public AccessibilityLevel GetAccessibilityLevel(Sessions.TrackerState state)
        {
            AccessibilityLevel level = AccessibilityLevel.Normal;
            foreach (CodeRule rule in mCodes)
            {
                AccessibilityLevel maxAccessibilityForCode;
                uint count = GetProviderCountForCode(rule.mCode, state, out maxAccessibilityForCode);

                if (!rule.mbIsSequenceBreakable && count < rule.mRequiredCount)
                {
                    level = AccessibilityLevel.None;
                    break;
                }

                if (rule.mbIsSequenceBreakable && count < rule.mRequiredCount)
                {
#if ENABLE_GLITCH
                    if (rule.mbIsGlitch)
                        level = Min(AccessibilityLevel.Glitch, level);
                    else
                        level = Min(AccessibilityLevel.SequenceBreak, level);
#else
                    level = AccessibilityLevel.SequenceBreak;
#endif
                }
                else
                {
                    level = Min(maxAccessibilityForCode, level);
                }
            }

            if (mbIsInspectable && level >= AccessibilityLevel.Glitch)
                level = AccessibilityLevel.Inspect;

            return level;
        }
    }
}
