using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public class AccessibilityRule
    {
        // Phase 4 of the TrackerSession refactor: the accessibility cache + enable
        // flag moved off this class onto a per-session AccessibilityEvaluator
        // (reachable via TrackerSession.Current.Evaluator). The static accessors
        // below remain as compatibility forwarders so existing call sites compile;
        // they route through the current session's evaluator. A standalone fallback
        // evaluator covers the (vanishingly rare) early-startup case where no
        // session is current yet.
        static readonly AccessibilityEvaluator sFallbackEvaluator = new AccessibilityEvaluator();

        static AccessibilityEvaluator CurrentEvaluator
        {
            get
            {
                var session = Session.TrackerSession.Current;
                return session?.Evaluator ?? sFallbackEvaluator;
            }
        }

        public static bool EnableCache
        {
            get { return CurrentEvaluator.EnableCache; }
            set { CurrentEvaluator.EnableCache = value; }
        }

        public static void ClearCaches()
        {
            CurrentEvaluator.ClearCaches();
        }

        static uint GetProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            // Use the session's tracker as the code provider. Session is always
            // constructed before any rule evaluation; if Current is null we have
            // no meaningful state to evaluate against.
            ICodeProvider provider = Session.TrackerSession.Current?.Tracker;
            return CurrentEvaluator.GetProviderCountForCode(provider, code, out maxAccessibility);
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
