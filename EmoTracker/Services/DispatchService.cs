using EmoTracker.Core.Services.Backends;
using System;
using System.Threading.Tasks;
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

        public T Invoke<T>(Func<T> func)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return func();
            return Dispatcher.UIThread.InvokeAsync(func).GetAwaiter().GetResult();
        }

        public async Task Invoke(Func<Task> func)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await func();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(async () => await func());
            }
        }

        public Task BeginInvoke(Func<Task> func)
        {
            var tcs = new TaskCompletionSource<bool>();
            Dispatcher.UIThread.Post(async () =>
            {
                try { await func(); tcs.SetResult(true); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
