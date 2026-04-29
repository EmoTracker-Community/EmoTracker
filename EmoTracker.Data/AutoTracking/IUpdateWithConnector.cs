using EmoTracker.Data.Packages;
using NLua;

namespace EmoTracker.Data.AutoTracking
{
    /// <summary>
    /// A unit the autotracker's polling loop can pump on each tick. Implemented
    /// by <see cref="MemorySegment"/> (read-window-against-memory),
    /// <see cref="MemoryTimer"/> (periodic Lua callback), and any future
    /// per-state polling primitive that the autotracker should drive.
    ///
    /// <para>
    /// Phase 7.13: this interface (and the <see cref="MemorySegment"/> /
    /// <see cref="MemoryTimer"/> classes that implement it) lives in
    /// <c>EmoTracker.Data</c> so per-state model objects (TrackerState +
    /// ScriptManager) can hold them directly. Previously the autotracker
    /// extension owned these instances and had to replay registrations
    /// across forks via a separate IMemoryWatchService bridge — making
    /// segments first-class data-model types removes that fork-time dance.
    /// </para>
    /// </summary>
    public interface IUpdateWithConnector
    {
        [LuaHide]
        void MarkDirty();

        [LuaHide]
        bool ShouldUpdate(System.DateTime now);

        [LuaHide]
        MemoryUpdateResult UpdateWithConnector(IAutoTrackingProvider provider, PackageManager.Game game);
    }
}
