using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using NLua;

namespace EmoTracker.Extensions.AutoTracker
{
    internal interface IUpdateWithConnector
    {
        [LuaHide]
        void MarkDirty();

        [LuaHide]
        bool ShouldUpdate(System.DateTime now);

        [LuaHide]
        MemoryUpdateResult UpdateWithConnector(IAutoTrackingProvider provider, PackageManager.Game game);
    }
}
