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
        public interface ILogServiceBackend
        {
            void Debug(string format, params object[] tokens);
            void Info(string format, params object[] tokens);
            void Warn(string format, params object[] tokens);
            void Error(string format, params object[] tokens);

            void Debug(Exception ex, string format, params object[] tokens);
            void Info(Exception ex, string format, params object[] tokens);
            void Warn(Exception ex, string format, params object[] tokens);
            void Error(Exception ex, string format, params object[] tokens);
        }

        public static class LogService
        {
            static ILogServiceBackend mActiveBackend;

            public static void SetServiceBackend(ILogServiceBackend backend)
            {
                var existing = Interlocked.Exchange(ref mActiveBackend, backend);
                if (existing != backend && existing is IDisposable disposable)
                    disposable.Dispose();
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
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Debug(format, tokens);
        }
        public static void Info(string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Info(format, tokens);
        }
        public static void Warn(string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Warn(format, tokens);
        }
        public static void Error(string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Error(format, tokens);
        }
        public static void Debug(Exception ex, string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Debug(ex, format, tokens);
        }
        public static void Info(Exception ex, string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Info(ex, format, tokens);
        }
        public static void Warn(Exception ex, string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Warn(ex, format, tokens);
        }
        public static void Error(Exception ex, string format, params object[] tokens)
        {
            var backend = Backends.LogService.Backend;
            if (backend != null)
                backend.Error(ex, format, tokens);
        }
    }
}
