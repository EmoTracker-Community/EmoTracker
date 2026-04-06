using System;

namespace EmoTracker.Data.AutoTracking
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class AutoTrackingProviderAttribute : Attribute
    {
    }
}
