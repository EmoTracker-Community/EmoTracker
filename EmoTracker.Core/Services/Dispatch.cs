using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Core.Services
{
    #region -- Backend --
    namespace Backends
    {
        public interface IDispatchServiceBackend
        {
            //  Invokes an action on the main application thread synchronously
            void Invoke(Action action);

            //  Invokes an action on the main application thread asynchronously
            void BeginInvoke(Action action);

            //  Invokes a function on the main application thread synchronously, returning the result
            T Invoke<T>(Func<T> func);

            //  Invokes an async function on the main application thread synchronously (waits for completion)
            Task Invoke(Func<Task> func);

            //  Invokes an async function on the main application thread asynchronously
            Task BeginInvoke(Func<Task> func);
        }

        public static class DispatchService
        {
            static IDispatchServiceBackend mActiveBackend;

            public static void SetServiceBackend(IDispatchServiceBackend backend)
            {
                var existing = Interlocked.Exchange(ref mActiveBackend, backend);
                if (existing != backend && existing is IDisposable disposable)
                    disposable.Dispose();
            }

            public static IDispatchServiceBackend Backend
            {
                get { return mActiveBackend; }
            }
        }
    }

    #endregion

    public static class Dispatch
    {
        //  Invokes an action on the main application thread synchronously
        public static void Invoke(Action action)
        {
            var backend = Backends.DispatchService.Backend;
            if (backend != null)
                backend.Invoke(action);
        }

        //  Invokes an action on the main application thread asynchronously
        public static void BeginInvoke(Action action)
        {
            var backend = Backends.DispatchService.Backend;
            if (backend != null)
                backend.BeginInvoke(action);
        }

        //  Invokes a function on the main application thread synchronously, returning the result
        public static T Invoke<T>(Func<T> func)
        {
            var backend = Backends.DispatchService.Backend;
            if (backend != null)
                return backend.Invoke(func);
            return func();
        }

        //  Invokes an async function on the main application thread synchronously (waits for completion)
        public static Task Invoke(Func<Task> func)
        {
            var backend = Backends.DispatchService.Backend;
            if (backend != null)
                return backend.Invoke(func);
            return func();
        }

        //  Invokes an async function on the main application thread asynchronously
        public static Task BeginInvoke(Func<Task> func)
        {
            var backend = Backends.DispatchService.Backend;
            if (backend != null)
                return backend.BeginInvoke(func);
            return func();
        }
    }
}
