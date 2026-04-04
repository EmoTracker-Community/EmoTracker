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
        public interface ILogServiceBackend
        {
            void Debug(string format, params object[] tokens);
            void Info(string format, params object[] tokens);
            void Warn(string format, params object[] tokens);
            void Error(string format, params object[] tokens);
        }

        public static class LogService
        {
            static ILogServiceBackend mActiveBackend;

            public static void SetServiceBackend(ILogServiceBackend backend)
            {
                if (backend != mActiveBackend)
                {
                    IDisposable existingBackendAsDisposable = mActiveBackend as IDisposable;
                    if (existingBackendAsDisposable != null)
                        existingBackendAsDisposable.Dispose();

                    mActiveBackend = backend;
                }
            }

            public static ILogServiceBackend Backend
            {
                get { return mActiveBackend; }
            }
        }
    }

    #endregion

    public static class Log
    {
        public static void Debug(string format, params object[] tokens)
        {
            if (Backends.LogService.Backend != null)
                Backends.LogService.Backend.Debug(format, tokens);
        }
        public static void Info(string format, params object[] tokens)
        {
            if (Backends.LogService.Backend != null)
                Backends.LogService.Backend.Info(format, tokens);
        }
        public static void Warn(string format, params object[] tokens)
        {
            if (Backends.LogService.Backend != null)
                Backends.LogService.Backend.Warn(format, tokens);
        }
        public static void Error(string format, params object[] tokens)
        {
            if (Backends.LogService.Backend != null)
                Backends.LogService.Backend.Error(format, tokens);
        }
    }
}
