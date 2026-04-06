using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EmoTracker.Core;
using EmoTracker.Services;
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
                desktop.MainWindow = new MainWindow();

                desktop.Exit += (s, e) =>
                {
                    try
                    {
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
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        // --- NativeMenu Click handlers ---
        private void NativeMenu_Open(object sender, EventArgs e) => ApplicationModel.Instance.OpenCommand.Execute(null);
        private void NativeMenu_Save(object sender, EventArgs e) => ApplicationModel.Instance.SaveCommand.Execute(null);
        private void NativeMenu_SaveAs(object sender, EventArgs e) => ApplicationModel.Instance.SaveAsCommand.Execute(null);
        private void NativeMenu_BroadcastView(object sender, EventArgs e) => ApplicationModel.Instance.ShowBroadcastViewCommand.Execute(null);
        private void NativeMenu_ResetLayoutScale(object sender, EventArgs e) => ApplicationModel.Instance.ResetLayoutScale();
        private void NativeMenu_Reload(object sender, EventArgs e) => ApplicationModel.Instance.RefreshCommand.Execute(null);
        private void NativeMenu_ManagePackages(object sender, EventArgs e) => ApplicationModel.Instance.ShowPackageManagerCommand.Execute(null);
        private void NativeMenu_CheckForUpdates(object sender, EventArgs e) => ApplicationModel.Instance.CheckForUpdateCommand.Execute(null);
        private void NativeMenu_ExportOverrides(object sender, EventArgs e) => ApplicationModel.Instance.ExportPackageOverrideCommand.Execute(null);
        private void NativeMenu_OpenOverrideFolder(object sender, EventArgs e) => ApplicationModel.Instance.OpenPackOverrideFolderCommand.Execute(null);
        private void NativeMenu_ClearOverrides(object sender, EventArgs e) => ApplicationModel.Instance.ResetUserDataCommand.Execute(null);
        private void NativeMenu_DevConsole(object sender, EventArgs e) => ApplicationModel.Instance.ShowDeveloperConsoleCommand.Execute(null);
        private void NativeMenu_PackageDocs(object sender, EventArgs e) => ApplicationModel.Instance.OpenPackageDocumentationCommand.Execute(null);

        private void NativeMenu_AlwaysOnTop(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.AlwaysOnTop = item.IsChecked;
        }
        private void NativeMenu_EnableMap(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.Tracker.Instance.MapEnabled = item.IsChecked;
        }
        private void NativeMenu_SwapLR(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.Tracker.Instance.SwapLeftRight = item.IsChecked;
        }
        private void NativeMenu_MapDpi(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) UI.Media.Utility.IconUtility.Instance.EnableDpiConversion = item.IsChecked;
        }
        private void NativeMenu_AlwaysAllowChest(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.AlwaysAllowClearing = item.IsChecked;
        }
        private void NativeMenu_IgnoreLogic(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.IgnoreAllLogic = item.IsChecked;
        }
        private void NativeMenu_PromptRefresh(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.PromptOnRefreshClose = item.IsChecked;
        }
        private void NativeMenu_ShowAllLocations(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.DisplayAllLocations = item.IsChecked;
        }
        private void NativeMenu_PinLocations(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.PinLocationsOnItemCapture = item.IsChecked;
        }
        private void NativeMenu_UnpinLocations(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.AutoUnpinLocationsOnClear = item.IsChecked;
        }
        private void NativeMenu_FastToolTips(object sender, EventArgs e)
        {
            if (sender is NativeMenuItem item) Data.ApplicationSettings.Instance.FastToolTips = item.IsChecked;
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
                    AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
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
