using EmoTracker.Core.Services.Backends;
using System;
using Avalonia.Threading;

namespace EmoTracker.Services
{
    public class DispatchService : IDispatchServiceBackend
    {
        public void BeginInvoke(Action action)
        {
            Dispatcher.UIThread.Post(action);
        }

        public void Invoke(Action action)
        {
            Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
        }
    }
}
