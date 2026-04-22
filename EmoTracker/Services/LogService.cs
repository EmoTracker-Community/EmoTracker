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

        // Log messages from these subsystems are internal infrastructure concerns
        // and should not surface in the pack developer console.
        private static readonly string[] sExcludedPrefixes = { "[Voice]", "[NDI]", "[MCP]", "[SNI]", "[NWA]" };

        public DeveloperConsoleSink(IFormatProvider formatProvider)
        {
            mFormatProvider = formatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage(mFormatProvider);

            // Filter out infrastructure subsystem messages that are not relevant
            // to pack developers using the developer console.
            foreach (var prefix in sExcludedPrefixes)
            {
                if (message.StartsWith(prefix, StringComparison.Ordinal))
                    return;
            }

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
        public void Debug(Exception ex, string format, params object[] tokens)
        {
            Log.Debug(ex, format, tokens);
        }
        public void Info(Exception ex, string format, params object[] tokens)
        {
            Log.Information(ex, format, tokens);
        }
        public void Warn(Exception ex, string format, params object[] tokens)
        {
            Log.Warning(ex, format, tokens);
        }
        public void Error(Exception ex, string format, params object[] tokens)
        {
            Log.Error(ex, format, tokens);
        }
    }
}
