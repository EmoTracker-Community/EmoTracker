namespace EmoTracker.Data.Locations
{
    public enum AccessibilityLevel : uint
    {
        None = 0,
        Partial = 1,
        Unlockable = 2,
        Inspect = 3,
        Glitch = 4,
        SequenceBreak = 5,
        Normal = 6,
        Cleared = 7
    }
}
