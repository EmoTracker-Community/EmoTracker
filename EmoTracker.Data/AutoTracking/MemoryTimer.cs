using EmoTracker.Data.Packages;
using NLua;
using System;

namespace EmoTracker.Data.AutoTracking
{
    /// <summary>
    /// A periodic per-state callback driven by the autotracker's polling
    /// loop. Unlike <see cref="MemorySegment"/> it does not read a memory
    /// window — it just fires its callback every <see cref="Period"/> ms
    /// while the autotracker is running.
    ///
    /// <para>
    /// Owned by per-state code (typically the same place as
    /// <see cref="MemorySegment"/> instances); the autotracker extension
    /// pumps each one through <see cref="UpdateWithConnector"/> on each
    /// poll tick.
    /// </para>
    /// </summary>
    public class MemoryTimer : IUpdateWithConnector, IDisposable
    {
        readonly string mName;
        readonly Func<IAutoTrackingProvider, PackageManager.Game, bool> mCallback;
        readonly int mPeriod;
        DateTime mLastUpdate;

        public string Name => mName;
        public int Period => mPeriod;

        public MemoryTimer(string name, Func<IAutoTrackingProvider, PackageManager.Game, bool> callback, int period = 500)
        {
            mName = name;
            mCallback = callback;
            mPeriod = period;
        }

        [LuaHide]
        public bool ShouldUpdate(DateTime now)
        {
            lock (this)
            {
                if (mLastUpdate.ToBinary() != 0)
                {
                    if ((now - mLastUpdate).CompareTo(TimeSpan.FromMilliseconds(Period)) < 0)
                        return false;
                }

                return true;
            }
        }

        [LuaHide]
        public MemoryUpdateResult UpdateWithConnector(IAutoTrackingProvider provider, PackageManager.Game game)
        {
            try
            {
                lock (this)
                {
                    mLastUpdate = DateTime.Now;
                }

                if (mCallback != null)
                    return mCallback(provider, game) ? MemoryUpdateResult.Success : MemoryUpdateResult.Error;
            }
            catch
            {
            }

            return MemoryUpdateResult.Error;
        }

        public void MarkDirty()
        {
        }

        public void Dispose()
        {
        }
    }
}
