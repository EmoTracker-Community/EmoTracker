using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        }

        public static class DispatchService
        {
            static IDispatchServiceBackend mActiveBackend;

            public static void SetServiceBackend(IDispatchServiceBackend backend)
            {
                if (backend != mActiveBackend)
                {
                    IDisposable existingBackendAsDisposable = mActiveBackend as IDisposable;
                    if (existingBackendAsDisposable != null)
                        existingBackendAsDisposable.Dispose();

                    mActiveBackend = backend;
                }
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
            if (Backends.DispatchService.Backend != null)
                Backends.DispatchService.Backend.Invoke(action);
        }   

        //  Invokes an action on the main application thread asynchronously
        public static void BeginInvoke(Action action)
        {
            if (Backends.DispatchService.Backend != null)
                Backends.DispatchService.Backend.BeginInvoke(action);
        }
    }
}
