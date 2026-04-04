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

        public AccessibilityLevel Accessibility
        {
            get
            {
                if (ApplicationSettings.Instance.IgnoreAllLogic)
                    return AccessibilityLevel.Normal;

                if (mRules.Count == 0)
                    return AccessibilityLevel.Normal;

                AccessibilityLevel level = AccessibilityLevel.None;
                foreach (AccessibilityRule rule in mRules)
                {
                    if (rule.AccessibilityLevel > level)
                        level = rule.AccessibilityLevel;
                }

                return level;
            }
        }

        public AccessibilityLevel AccessibilityWithoutModifiers
        {
            get
            {
                if (ApplicationSettings.Instance.IgnoreAllLogic)
                    return AccessibilityLevel.Normal;

                if (mRules.Count == 0)
                    return AccessibilityLevel.Normal;

                AccessibilityLevel level = AccessibilityLevel.None;
                foreach (AccessibilityRule rule in mRules)
                {
                    AccessibilityLevel local = rule.AccessibilityLevel;
                    /*
                    if (local == AccessibilityLevel.Unlockable)
                        local = AccessibilityLevel.Normal;
*/
                    if (local != AccessibilityLevel.Inspect && local > level)
                        level = local;
                }

                return level;
            }
        }

        public AccessibilityLevel AccessibilityForVisibility
        {
            get
            {
                if (mRules.Count == 0)
                    return AccessibilityLevel.Normal;

                AccessibilityLevel level = AccessibilityLevel.None;
                foreach (AccessibilityRule rule in mRules)
                {
                    AccessibilityLevel local = rule.AccessibilityLevel;
                    /*
                    if (local == AccessibilityLevel.Unlockable)
                        local = AccessibilityLevel.Normal;
*/
                    if (local != AccessibilityLevel.Inspect && local > level)
                        level = local;
                }

                return level;
            }
        }
    }
}
