using EmoTracker.Core.Services.Backends;
using EmoTracker.Data;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;

namespace EmoTracker.Services
{
    class DeveloperConsoleSink : ILogEventSink
    {
        private readonly IFormatProvider mFormatProvider;

        public DeveloperConsoleSink(IFormatProvider formatProvider)
        {
            mFormatProvider = formatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage(mFormatProvider);

            switch (logEvent.Level)
            {
                case LogEventLevel.Information:
                    Core.Services.Dispatch.BeginInvoke(() =>
                    {
                        ScriptManager.Instance.Output(message);
                    });
                    break;

                case LogEventLevel.Warning:
                    Core.Services.Dispatch.BeginInvoke(() =>
                    {
                        ScriptManager.Instance.OutputWarning(message);
                    });
                    break;

                case LogEventLevel.Error:
                case LogEventLevel.Fatal:
                    Core.Services.Dispatch.BeginInvoke(() =>
                    {
                        ScriptManager.Instance.OutputError(message);
                    });
                    break;
            }

            if (logEvent.Exception != null)
            {
                Core.Services.Dispatch.BeginInvoke(() =>
                {
                    ScriptManager.Instance.OutputException(logEvent.Exception);
                });
            }
        }
    }
    public static class DeveloperConsoleSinkExtensions
    {
        public static LoggerConfiguration DeveloperConsole(this LoggerSinkConfiguration loggerConfiguration, IFormatProvider formatProvider = null)
        {
            return loggerConfiguration.Sink(new DeveloperConsoleSink(formatProvider));
        }
    }

    public class LogService : ILogServiceBackend
    {
        public void Debug(string format, params object[] tokens)
        {
            Log.Debug(format, tokens);
        }
        public void Info(string format, params object[] tokens)
        {
            Log.Information(format, tokens);
        }
        public void Warn(string format, params object[] tokens)
        {
            Log.Warning(format, tokens);
        }
        public void Error(string format, params object[] tokens)
        {
            Log.Error(format, tokens);
        }
    }
}
