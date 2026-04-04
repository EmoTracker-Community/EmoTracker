using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public class AccessibilityRule
    {
        static bool sbEnableCache = true;
        public static bool EnableCache
        {
            get { return sbEnableCache; }
            set { sbEnableCache = value; ClearCaches(); }
        }

        private struct AccessibilityResult
        {
            public AccessibilityLevel Level;
            public uint ProvidedCount;
        }

        static Dictionary<string, AccessibilityResult> mAccessiblityCache = new Dictionary<string, AccessibilityResult>();

        public static void ClearCaches()
        {
            mAccessiblityCache.Clear();
        }

        static uint GetProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            if (sbEnableCache)
            {
                AccessibilityResult cachedResult;
                if (mAccessiblityCache.TryGetValue(code, out cachedResult))
                {
                    maxAccessibility = cachedResult.Level;
                    return cachedResult.ProvidedCount;
                }
            }

            ICodeProvider provider = Tracker.Instance;

            AccessibilityLevel maxAccessibilityForCode;
            uint count = provider.ProviderCountForCode(code, out maxAccessibilityForCode);

            if (sbEnableCache)
            {
                mAccessiblityCache[code] = new AccessibilityResult() { Level = maxAccessibilityForCode, ProvidedCount = count };
            }

            maxAccessibility = maxAccessibilityForCode;
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

        public AccessibilityLevel AccessibilityLevel
        {
            get
            {
                AccessibilityLevel level = AccessibilityLevel.Normal;
                foreach (CodeRule rule in mCodes)
                {
                    AccessibilityLevel maxAccessibilityForCode;
                    uint count = GetProviderCountForCode(rule.mCode, out maxAccessibilityForCode);

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
}
