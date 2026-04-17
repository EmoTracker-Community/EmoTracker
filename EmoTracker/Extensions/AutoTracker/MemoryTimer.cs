using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using NLua;
using System;

namespace EmoTracker.Extensions.AutoTracker
{
    public class MemoryTimer : IUpdateWithConnector, IDisposable
    {
        string mName;
        Func<IAutoTrackingProvider, PackageManager.Game, bool> mCallback;
        DateTime mLastUpdate;
        int mPeriod = 500;

        public string Name
        {
            get { return mName; }
        }

        public int Period
        {
            get { return mPeriod; }
        }

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
