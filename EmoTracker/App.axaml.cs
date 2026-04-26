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

            // Install the data-model-v2 ambient cross-model reference resolver.
            // Phase 6 step 9: routes ModelReference<T>.Target lookups through
            // SessionContext.ActiveState's per-state IndexedModelResolver
            // (O(1) lookup) — replaces the Phase 2.5 AmbientSingletonModelResolver
            // which linear-scanned the legacy singleton catalogs. ApplicationModel
            // populates SessionContext.ActiveState in RebindActivePackageInstanceFromSingletons
            // after each pack-load.
            Core.DataModel.ModelResolver.Current = new Data.Core.DataModel.PrimaryStateModelResolver();

            // Phase 7.1: register the per-state ScriptManager as the host
            // for ModelTypeBase.GetScriptManager() fallbacks. ApplicationModel
            // pre-allocates the primary state in its ctor, so this is safe
            // to read here.
            Core.DataModel.ScriptManagerHost.Current = ApplicationModel.Instance?.PrimaryState?.Scripts;

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
