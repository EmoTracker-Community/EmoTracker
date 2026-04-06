using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    SetupNativeMenu();

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

        private void SetupNativeMenu()
        {
            var appModel = ApplicationModel.Instance;
            var appSettings = Data.ApplicationSettings.Instance;
            var tracker = Data.Tracker.Instance;
            var iconUtility = UI.Media.Utility.IconUtility.Instance;

            var menu = new NativeMenu();

            // --- File menu ---
            var fileMenu = new NativeMenuItem("File") { Menu = new NativeMenu() };
            fileMenu.Menu.Add(new NativeMenuItem("Open") { Command = appModel.OpenCommand, Gesture = new KeyGesture(Key.O, KeyModifiers.Meta) });
            fileMenu.Menu.Add(new NativeMenuItemSeparator());
            fileMenu.Menu.Add(new NativeMenuItem("Save") { Command = appModel.SaveCommand, Gesture = new KeyGesture(Key.S, KeyModifiers.Meta) });
            fileMenu.Menu.Add(new NativeMenuItem("Save As...") { Command = appModel.SaveAsCommand, Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift) });
            menu.Add(fileMenu);

            // --- View menu ---
            var viewMenu = new NativeMenuItem("View") { Menu = new NativeMenu() };
            var alwaysOnTop = new NativeMenuItem("Always On Top") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.AlwaysOnTop };
            alwaysOnTop.Click += (s, e) => { appSettings.AlwaysOnTop = alwaysOnTop.IsChecked; };
            viewMenu.Menu.Add(alwaysOnTop);
            viewMenu.Menu.Add(new NativeMenuItemSeparator());

            var enableMap = new NativeMenuItem("Enable Map") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = tracker.MapEnabled };
            enableMap.Click += (s, e) => { tracker.MapEnabled = enableMap.IsChecked; };
            viewMenu.Menu.Add(enableMap);

            var swapLR = new NativeMenuItem("Swap Left/Right") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = tracker.SwapLeftRight };
            swapLR.Click += (s, e) => { tracker.SwapLeftRight = swapLR.IsChecked; };
            viewMenu.Menu.Add(swapLR);
            viewMenu.Menu.Add(new NativeMenuItemSeparator());

            var mapDpi = new NativeMenuItem("Map DPI Awareness") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = iconUtility.EnableDpiConversion };
            mapDpi.Click += (s, e) => { iconUtility.EnableDpiConversion = mapDpi.IsChecked; };
            viewMenu.Menu.Add(mapDpi);
            viewMenu.Menu.Add(new NativeMenuItemSeparator());

            viewMenu.Menu.Add(new NativeMenuItem("Reset Layout Scale") { Command = appModel.ResetLayoutScaleCommand, Gesture = new KeyGesture(Key.D0, KeyModifiers.Meta) });
            viewMenu.Menu.Add(new NativeMenuItemSeparator());
            viewMenu.Menu.Add(new NativeMenuItem("Broadcast View") { Command = appModel.ShowBroadcastViewCommand, Gesture = new KeyGesture(Key.F2) });
            menu.Add(viewMenu);

            // --- Tracking menu ---
            var trackingMenu = new NativeMenuItem("Tracking") { Menu = new NativeMenu() };

            var alwaysAllowChest = new NativeMenuItem("Always Allow Chest Manipulation") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.AlwaysAllowClearing };
            alwaysAllowChest.Click += (s, e) => { appSettings.AlwaysAllowClearing = alwaysAllowChest.IsChecked; };
            trackingMenu.Menu.Add(alwaysAllowChest);

            var ignoreLogic = new NativeMenuItem("Ignore All Logic") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.IgnoreAllLogic };
            ignoreLogic.Click += (s, e) => { appSettings.IgnoreAllLogic = ignoreLogic.IsChecked; };
            trackingMenu.Menu.Add(ignoreLogic);

            var promptRefresh = new NativeMenuItem("Prompt When Refreshing/Closing") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.PromptOnRefreshClose };
            promptRefresh.Click += (s, e) => { appSettings.PromptOnRefreshClose = promptRefresh.IsChecked; };
            trackingMenu.Menu.Add(promptRefresh);

            var showAllLocations = new NativeMenuItem("Show All Locations") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.DisplayAllLocations, Gesture = new KeyGesture(Key.F11) };
            showAllLocations.Click += (s, e) => { appSettings.DisplayAllLocations = showAllLocations.IsChecked; };
            trackingMenu.Menu.Add(showAllLocations);
            trackingMenu.Menu.Add(new NativeMenuItemSeparator());

            var pinLocations = new NativeMenuItem("Pin Locations on Item Capture") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.PinLocationsOnItemCapture };
            pinLocations.Click += (s, e) => { appSettings.PinLocationsOnItemCapture = pinLocations.IsChecked; };
            trackingMenu.Menu.Add(pinLocations);

            var unpinLocations = new NativeMenuItem("Unpin Locations when Cleared") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.AutoUnpinLocationsOnClear };
            unpinLocations.Click += (s, e) => { appSettings.AutoUnpinLocationsOnClear = unpinLocations.IsChecked; };
            trackingMenu.Menu.Add(unpinLocations);
            trackingMenu.Menu.Add(new NativeMenuItemSeparator());

            trackingMenu.Menu.Add(new NativeMenuItem("Reload") { Command = appModel.RefreshCommand, Gesture = new KeyGesture(Key.F5) });
            menu.Add(trackingMenu);

            // --- Packages menu ---
            var packagesMenu = new NativeMenuItem("Packages") { Menu = new NativeMenu() };
            packagesMenu.Menu.Add(new NativeMenuItem("Manage Packages") { Command = appModel.ShowPackageManagerCommand });
            packagesMenu.Menu.Add(new NativeMenuItemSeparator());
            packagesMenu.Menu.Add(new NativeMenuItem("Check For Updates") { Command = appModel.CheckForUpdateCommand });
            menu.Add(packagesMenu);

            // --- Advanced menu ---
            var advancedMenu = new NativeMenuItem("Advanced") { Menu = new NativeMenu() };
            advancedMenu.Menu.Add(new NativeMenuItem("Export Overrides") { Command = appModel.ExportPackageOverrideCommand });
            advancedMenu.Menu.Add(new NativeMenuItemSeparator());
            advancedMenu.Menu.Add(new NativeMenuItem("Open Override Folder") { Command = appModel.OpenPackOverrideFolderCommand });
            advancedMenu.Menu.Add(new NativeMenuItem("Clear Overrides") { Command = appModel.ResetUserDataCommand });
            advancedMenu.Menu.Add(new NativeMenuItemSeparator());
            advancedMenu.Menu.Add(new NativeMenuItem("Developer Console") { Command = appModel.ShowDeveloperConsoleCommand });
            menu.Add(advancedMenu);

            // --- Help menu ---
            var helpMenu = new NativeMenuItem("Help") { Menu = new NativeMenu() };
            helpMenu.Menu.Add(new NativeMenuItem("Package Documentation") { Command = appModel.OpenPackageDocumentationCommand, Gesture = new KeyGesture(Key.F1) });
            helpMenu.Menu.Add(new NativeMenuItemSeparator());
            var fastToolTips = new NativeMenuItem("Fast Tool Tips") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = appSettings.FastToolTips };
            fastToolTips.Click += (s, e) => { appSettings.FastToolTips = fastToolTips.IsChecked; };
            helpMenu.Menu.Add(fastToolTips);
            menu.Add(helpMenu);

            NativeMenu.SetMenu(this, menu);
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
