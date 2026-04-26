using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public class AccessibilityRuleSet
    {
        List<AccessibilityRule> mRules = new List<AccessibilityRule>();

        public bool Empty
        {
            get { return mRules.Count == 0; }
        }

        public IEnumerable<AccessibilityRule> Rules
        {
            get { return mRules; }
        }

        public void AddRule(string spec)
        {
            AccessibilityRule rule = new AccessibilityRule(spec);
            mRules.Add(rule);
        }

        /// <summary>
        /// Phase 7.2: evaluates the rule set in the context of <paramref name="state"/>.
        /// Replaces the previous parameterless <c>Accessibility</c> property which
        /// read through a static cache + <c>Tracker.Instance</c>.
        /// </summary>
        public AccessibilityLevel GetAccessibility(Sessions.TrackerState state)
        {
            if (ApplicationSettings.Instance.IgnoreAllLogic)
                return AccessibilityLevel.Normal;

            if (mRules.Count == 0)
                return AccessibilityLevel.Normal;

            AccessibilityLevel level = AccessibilityLevel.None;
            foreach (AccessibilityRule rule in mRules)
            {
                var ruleLevel = rule.GetAccessibilityLevel(state);
                if (ruleLevel > level)
                    level = ruleLevel;
            }

            return level;
        }

        public AccessibilityLevel GetAccessibilityWithoutModifiers(Sessions.TrackerState state)
        {
            if (ApplicationSettings.Instance.IgnoreAllLogic)
                return AccessibilityLevel.Normal;

            if (mRules.Count == 0)
                return AccessibilityLevel.Normal;

            AccessibilityLevel level = AccessibilityLevel.None;
            foreach (AccessibilityRule rule in mRules)
            {
                AccessibilityLevel local = rule.GetAccessibilityLevel(state);
                if (local != AccessibilityLevel.Inspect && local > level)
                    level = local;
            }

            return level;
        }

        public AccessibilityLevel GetAccessibilityForVisibility(Sessions.TrackerState state)
        {
            if (mRules.Count == 0)
                return AccessibilityLevel.Normal;

            AccessibilityLevel level = AccessibilityLevel.None;
            foreach (AccessibilityRule rule in mRules)
            {
                AccessibilityLevel local = rule.GetAccessibilityLevel(state);
                if (local != AccessibilityLevel.Inspect && local > level)
                    level = local;
            }

            return level;
        }
    }
}
