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
using System.Runtime.InteropServices;

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
#if WINDOWS
            ConfigurePlatformDllPaths();
#endif

            Data.Core.Transactions.TransactionProcessor.SetTransactionProcessor(
                new Data.Core.Transactions.Processors.LocalTransactionProcessorWithUndo());

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

#if WINDOWS
                        if (Data.ApplicationSettings.Instance.EnableDiscordRichPresence)
                        {
                            try { DiscordRpc.ClearPresence(); DiscordRpc.Shutdown(); }
                            catch { }
                        }
#endif
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

#if WINDOWS
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static void ConfigurePlatformDllPaths()
        {
            try
            {
                string processorAssemblyPath = Environment.Is64BitProcess ? "x64" : "x86";
                string privateBinPath = Path.Combine(
                    AppContext.BaseDirectory,
                    processorAssemblyPath + "\\");
                SetDllDirectory(privateBinPath);
            }
            catch
            {
                throw new InvalidOperationException("Failed to set platform DLL search directory.");
            }
        }
#endif
    }
}
