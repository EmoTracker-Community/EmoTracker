using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EmoTracker.Core;
using EmoTracker.Services;
using EmoTracker.Services.Updates;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace EmoTracker
{
    public partial class App : Avalonia.Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Data.Core.Transactions.TransactionProcessor.SetTransactionProcessor(
                new Data.Core.Transactions.Processors.LocalTransactionProcessorWithUndo());

            // Install the data-model-v2 ambient cross-model reference resolver. Bridges
            // ModelReference<T>.Target lookups to the singleton ItemDatabase / Tracker /
            // LocationDatabase graph until per-state resolvers land in the state-lifecycle phase.
            Core.DataModel.ModelResolver.Current = new Data.Core.DataModel.AmbientSingletonModelResolver();

            // Phase 5: register the active script manager so ModelTypeBase.GetScriptManager()
            // (and the holder-aware standard-callback dispatch path) can find it without
            // taking a hard dependency on the concrete ScriptManager type from Core.
            // Phase 6 swaps this out per-state.
#pragma warning disable CS0618 // Phase 6 step 11: bootstrap install of singleton ScriptManager as the host fallback
            Core.DataModel.ScriptManagerHost.Current = Data.ScriptManager.Current;
#pragma warning restore CS0618

            Core.Services.Backends.LogService.SetServiceBackend(new Services.LogService());
            Core.Services.Backends.DispatchService.SetServiceBackend(new Services.DispatchService());

            try
            {
                string logDirectory = Path.Combine(UserDirectory.Path, "logs");
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.FromLogContext()
                    .WriteTo.File(Path.Combine(logDirectory, "emotracker_log.txt"),
                        rollingInterval: RollingInterval.Day,
                        buffered: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(5))
                    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                    .WriteTo.DeveloperConsole()
                    .CreateLogger();
            }
            catch (Exception)
            {
            }

            //  Load application settings
            Data.ApplicationSettings.CreateInstance();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MainWindow();
                UpdateService.Instance.StartBackgroundCheck();

                desktop.Exit += (s, e) =>
                {
                    try
                    {
                        UI.Media.ImageReferenceService.Instance.Stop();
                        UpdateService.Instance.Dispose();

                        if (e.ApplicationExitCode == 0)
                            Extensions.ExtensionManager.Instance.OnApplicationClosing();
                    }
                    catch { }
                    finally
                    {
                        Log.CloseAndFlush();
                    }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}
