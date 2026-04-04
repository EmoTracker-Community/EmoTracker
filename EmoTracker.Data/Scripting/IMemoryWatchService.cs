using System;

namespace EmoTracker.Data.Scripting
{
    public interface IMemoryWatchService
    {
        IMemorySegment AddMemoryWatch(string name, ulong startAddress, ulong length, Func<IMemorySegment, bool> callback, Action<IMemorySegment> disposeCallback, int period);
        void RemoveMemoryWatch(IMemorySegment segment);
    }
}
