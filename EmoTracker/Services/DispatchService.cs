using EmoTracker.Core.Services.Backends;
using System;
#if WINDOWS
using System.Windows;
#else
using Avalonia.Threading;
#endif

namespace EmoTracker.Services
{
    public class DispatchService : IDispatchServiceBackend
    {
        public void BeginInvoke(Action action)
        {
#if WINDOWS
            Application.Current.Dispatcher.BeginInvoke(action);
#else
            Dispatcher.UIThread.Post(action);
#endif
        }

        public void Invoke(Action action)
        {
#if WINDOWS
            Application.Current.Dispatcher.Invoke(action);
#else
            Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
#endif
        }
    }
}
