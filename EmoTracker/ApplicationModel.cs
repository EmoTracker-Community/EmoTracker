#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using EmoTracker.Extensions;
using EmoTracker.Notifications;
using EmoTracker.Services;
using EmoTracker.UI;
using EmoTracker.UI.Media;
using Newtonsoft.Json.Linq;
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace EmoTracker
{
    public class ApplicationModel : ObservableSingleton<ApplicationModel>, ICodeProvider, INotificationService
    {
        public DelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand ResetUserDataCommand { get; private set; }
        public DelegateCommand OpenPackOverrideFolderCommand { get; private set; }
        public DelegateCommand ActivatePackCommand { get; private set; }
        public DelegateCommand ShowPackageManagerCommand { get; private set; }
        public DelegateCommand ExportPackageOverrideCommand { get; private set; }
        public DelegateCommand ShowBroadcastViewCommand { get; private set; }
        public DelegateCommand ShowDeveloperConsoleCommand { get; private set; }

        public DelegateCommand SaveCommand { get; private set; }
        public DelegateCommand SaveAsCommand { get; private set; }
        public DelegateCommand OpenCommand { get; private set; }

        public DelegateCommand OpenPackageDocumentationCommand { get; private set; }

        public DelegateCommand ResetLayoutScaleCommand { get; private set; }

        public DelegateCommand InstallPackageCommand { get; private set; }

        public DelegateCommand  UninstallPackageCommand { get; private set; }

        private Layout mBroadcastLayout = new Layout();
        private Layout mTrackerLayout;
        private Layout mTrackerHorizontalLayout;
        private Layout mTrackerVerticalLayout;
        private Layout mTrackerCaptureItemLayout;

        public string MainWindowTitle
        {
            get
            {
                string title = string.Format("EmoTracker {0}", ApplicationVersion.Current);

                if (Tracker.Instance.ActiveGamePackage != null && Tracker.Instance.ActiveGamePackageVariant != null)
                {
                    title = string.Format("{0}  ::  {1} | {2}", title, Tracker.Instance.ActiveGamePackage.DisplayName, Tracker.Instance.ActiveGamePackageVariant.DisplayName);
                }
                else if (Tracker.Instance.ActiveGamePackage != null)
                {
                    title = string.Format("{0}  ::  {1}", title, Tracker.Instance.ActiveGamePackage.DisplayName);

                }

                return title;
            }
        }

        public Layout BroadcastLayout
        {
            get { return mBroadcastLayout; }
            protected set { SetProperty(ref mBroadcastLayout, value); }
        }

        public Layout TrackerLayout
        {
            get { return mTrackerLayout; }
            protected set { SetProperty(ref mTrackerLayout, value); NotifyPropertyChanged("TrackerHorizontalLayout"); NotifyPropertyChanged("TrackerVerticalLayout"); }
        }

        public Layout TrackerHorizontalLayout
        {
            get
            {
                if (mTrackerHorizontalLayout != null)
                    return mTrackerHorizontalLayout;

                return mTrackerLayout;
            }

            protected set { SetProperty(ref mTrackerHorizontalLayout, value); }
        }

        public Layout TrackerVerticalLayout
        {
            get
            {
                if (mTrackerVerticalLayout != null)
                    return mTrackerVerticalLayout;

                return mTrackerLayout;
            }

            protected set { SetProperty(ref mTrackerVerticalLayout, value); }
        }

        public Layout TrackerCaptureItemLayout
        {
            get { return mTrackerCaptureItemLayout; }
            protected set { SetProperty(ref mTrackerCaptureItemLayout, value); }
        }

        public ApplicationModel()
        {
            InitializeNotifications();

                //  Force initialize core managers
                PackageManager.CreateInstance();
                PackageManager.Instance.Initialize();

                InitializePackageManagerViews();

            Tracker.Instance.OnPackageLoadStarting += Tracker_OnPackageLoadStarting;
            Tracker.Instance.OnPackageLoadComplete += Tracker_OnPackageLoadComplete;

            RefreshCommand = new DelegateCommand(RefreshHandler);
            ResetUserDataCommand = new DelegateCommand(ResetUserDataHandler);
            OpenPackOverrideFolderCommand = new DelegateCommand(OpenPackOverrideFolderHandler);
            ActivatePackCommand = new DelegateCommand(ActivatePackHandler);
            ShowPackageManagerCommand = new DelegateCommand(ShowPackManagerHandler);
            ExportPackageOverrideCommand = new DelegateCommand(ExportPackageOverrideHandler);
            SaveCommand = new DelegateCommand(SaveHandler, CanSave);
            SaveAsCommand = new DelegateCommand(SaveAsHandler, CanSave);
            OpenCommand = new DelegateCommand(OpenHandler);

            OpenPackageDocumentationCommand = new DelegateCommand(OpenPackageDocumentation, CanOpenPackageDocumentation);
            ResetLayoutScaleCommand = new DelegateCommand(ResetLayoutScale);

            ShowBroadcastViewCommand = new DelegateCommand(ShowBroadcastView);
            ShowDeveloperConsoleCommand = new DelegateCommand(ShowDevleoperConsole);

            InstallPackageCommand = new DelegateCommand(InstallPackage);
            UninstallPackageCommand = new DelegateCommand(UninstallPackage, CanUninstallPackage);

            // When HTTP game images finish downloading, refresh the package list once.
            // Multiple images often load near-simultaneously, so we coalesce the refreshes:
            // the first completion schedules a single Background-priority update; subsequent
            // completions that arrive before it runs are folded into that one refresh.
            EmoTracker.UI.Media.Utility.IconUtility.HttpImageLoaded += OnHttpImageLoaded;
        }

        private bool _httpRefreshScheduled = false;

        private void OnHttpImageLoaded(object? sender, EventArgs e)
        {
            // Coalesce multiple near-simultaneous completions into one Background-priority refresh.
            if (_httpRefreshScheduled) return;
            _httpRefreshScheduled = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _httpRefreshScheduled = false;
                EmoTracker.UI.Media.ImageReferenceService.Instance.ClearImageCache();
                NotifyPropertyChanged(nameof(AvailablePackagesGroupedView));
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        public void Initialize()
        {
            //  Load and start extensions
            Extensions.ExtensionManager.CreateInstance();
            Extensions.ExtensionManager.Instance.Start();

            //Open up the last active package if set and installed
            bool success;
            string msg;

            (success, msg) = Tracker.Instance.LoadDefaultPackage();

            if (!string.IsNullOrWhiteSpace(msg))
            {
                PushMarkdownNotification(NotificationType.Error, msg);
            }

            NotifyPropertyChanged("MainWindowTitle");
        }

        private void ShowBroadcastView(object obj)
        {
            if (mBroadcastView == null)
            {
                mBroadcastView = new BroadcastView();
                mBroadcastView.Closing += (_, _) => mBroadcastView = null;

                // Show without an owner so the broadcast view is an independent
                // top-level window.  Passing the main window as owner causes the
                // OS to force the broadcast view above the main window at all times.
                mBroadcastView.Show();
            }
            else
            {
                mBroadcastView.Activate();
            }
        }

        public BroadcastView BroadcastView => mBroadcastView;
        private BroadcastView mBroadcastView;

        private void ShowDevleoperConsole(object obj)
        {
            var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
            mainWindow?.ShowDeveloperConsole();
        }

        private async void InstallPackage(object obj)
        {
            var package = (PackageRepositoryEntry)obj;

            if (package.ExistingPackage != null)
            {
                DirectoryInfo packdir = new DirectoryInfo(package.ExistingPackage.OverridePath);

                if (packdir.Exists)
                {
                    string msg = $"You have user overrides in place for {package.Name} which may cause issues after updating. Do you want to backup and disable your overrides prior to updating?";
                    string caption = "Uninstall Package";
                    bool? res = await DialogService.Instance.ShowYesNoCancelAsync(caption, msg);

                    switch (res)
                    {
                        case null:
                            return;

                        case false:
                            break;

                        case true:
                            BackupOverrideResult bores = package.BackupOverride();

                            switch (bores)
                            {
                                case BackupOverrideResult.Failed:
                                    msg = $"Unable to backup {package.Name} overrides. Check to make sure that no other application is using the folder or you do not have a backup instance already. Canceling update";
                                    caption = "Backup Failed";
                                    await DialogService.Instance.ShowOKAsync(caption, msg);
                                    return;

                                case BackupOverrideResult.Success:
                                    break;
                            }
                            break;
                    }

                }
            }

            package.Install();
        }

        private async void UninstallPackage(object obj)
        {
            var package = (PackageRepositoryEntry)obj;

            string msg = $"You are about to uninstall \"{package.Name}\". This will remove all the files associated with the package as well as the overrides. Do you wish to continue?";
            string caption = "Uninstall Package";
            bool res = await DialogService.Instance.ShowYesNoAsync(caption, msg);

            if(!res) { return; }

            UninstallResult ures = package.Uninstall();
            switch(ures)
            {
                case UninstallResult.Success:
                    break;
                case UninstallResult.FailedUninstall:
                    msg = $"Failed to uninstall \"{package.Name}\"! Please ensure no other applications are using the file and try again.";
                    caption = "Failed to Uninstall";
                    await DialogService.Instance.ShowOKAsync(caption, msg);
                    break;
                case UninstallResult.FailedOverrides:
                    msg = $"Failed to remove \"{package.Name}\" overrides folder. You will need to remove it manually";
                    caption = "Failed to Remove Overrides";
                    await DialogService.Instance.ShowOKAsync(caption, msg);
                    break;
            }
        }

        private bool CanUninstallPackage(object obj)
        {
            var package = (PackageRepositoryEntry)obj;
            if (package != null && package.ExistingPackage != null && package.ExistingPackage.Source != null && package.ExistingPackage.Source as ZipPackageSource != null)
                return true;

            return false;
        }

        private void OpenPackOverrideFolderHandler(object obj)
        {
            if (Tracker.Instance.ActiveGamePackage != null && !string.IsNullOrWhiteSpace(Tracker.Instance.ActiveGamePackage.OverridePath))
            {
                try
                {
                    Directory.CreateDirectory(Tracker.Instance.ActiveGamePackage.OverridePath);
                }
                catch { };

                if (Directory.Exists(Tracker.Instance.ActiveGamePackage.OverridePath))
                    WindowService.Instance.OpenFolder(Tracker.Instance.ActiveGamePackage.OverridePath);
                else
                    PushMarkdownNotification(NotificationType.Error, string.Format(
@"### Cannot open override folder
Failed to find or create the active pack's override folder at `{0}`.

Make sure you have available disk space and permissions for the selected location.",
Tracker.Instance.ActiveGamePackage.OverridePath)
);
            }
        }

        private void ExportPackageOverrideHandler(object obj)
        {
            if (Tracker.Instance.ActiveGamePackage != null)
            {
                string filename = obj as string;
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    GamePackage package = Tracker.Instance.ActiveGamePackage as GamePackage;
                    if (package != null)
                    {
                        package.ExportUserOverride(filename);
                    }
                }
                else
                {
                    OverrideExportDialog dialog = new OverrideExportDialog();
                    _ = dialog.ShowDialog(
                        (Avalonia.Application.Current?.ApplicationLifetime as
                            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow);
                }
            }
        }

        private void ShowPackManagerHandler(object obj)
        {
            UI.PackageManagerWindow window = new UI.PackageManagerWindow();
            _ = window.ShowDialog(
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow);

            WindowService.Instance.FocusMainWindow();
        }

        private async void RefreshHandler(object param)
        {
            if (ApplicationSettings.Instance.PromptOnRefreshClose)
            {
                bool result = await DialogService.Instance.ShowYesNoAsync("Warning!", "Refreshing will cause you to lose all unsaved progress. Are you sure you want to refresh?", defaultYes: false);
                if (!result)
                    return;
            }

            Reload();
            WindowService.Instance.FocusMainWindow();
        }

        private async void ResetUserDataHandler(object param)
        {
            if (Tracker.Instance.ActiveGamePackage != null)
            {
                if (ApplicationSettings.Instance.PromptOnRefreshClose)
                {
                    bool result = await DialogService.Instance.ShowYesNoAsync("Warning!", "Clearing overrides will cause you to lose all unsaved progress. Are you sure you want to continue?", defaultYes: false);
                    if (!result)
                        return;
                }

                Tracker.Instance.ActiveGamePackage.ResetUserOverrides();
                Reload();
            }

            WindowService.Instance.FocusMainWindow();
        }

        private void ActivatePackHandler(object obj)
        {
            Core.Services.Dispatch.BeginInvoke(() =>
            {
                IGamePackage package = obj as IGamePackage;
                IGamePackageVariant variant = obj as IGamePackageVariant;

                if (package != null)
                {
                    Tracker.Instance.ActiveGamePackageVariant = null;
                    Tracker.Instance.ActiveGamePackage = package;
                }
                else if (variant != null)
                {
                    Tracker.Instance.ActiveGamePackageVariant = variant;
                }
            });
        }

        #region -- Visual Adjustments --

        int mMainLayoutScale = 100;
        [DependentProperty("MainLayoutScaleFactor")]
        public int MainLayoutScale
        {
            get { return mMainLayoutScale; }
            set
            {
                int filteredValue = Math.Min(Math.Max(value, 100), 500);
                SetProperty(ref mMainLayoutScale, filteredValue);
            }
        }

        public double MainLayoutScaleFactor
        {
            get { return mMainLayoutScale / 100.0; }
        }

        public void IncrementMainLayoutScale(int steps)
        {
            MainLayoutScale = MainLayoutScale + (steps * 10);
        }

        public void ResetLayoutScale(object obj = null)
        {
            MainLayoutScale = 100;
        }

        #endregion

        #region -- Save/Load --

        string mCurrentSavePath;

        private async void OpenHandler(object obj)
        {
            string defaultSaveDataPath = Path.Combine(UserDirectory.Path, "saves");

            string filename = await DialogService.Instance.OpenFileAsync("EmoTracker Save File (*.json)|*.json", defaultSaveDataPath);
            if (filename != null)
            {
                if (!LoadProgress(filename))
                {
                    Reload();

                    await DialogService.Instance.ShowOKAsync("Failed to load save data...",
                        "Failed to load the requested save file. Possible reasons include:\n\n" +
                        "• The original pack or variant no longer exists\n" +
                        "• The save data has been corruped\n" +
                        "• The pack version is different from the version used to save\n" +
                        "• The pack contents do not match the save data.\n\n" +
                        "Note that certain types of user overrides can affect this, if added/changed since saving.");
                }
                else
                {
                    mCurrentSavePath = filename;
                }
            }
        }

        private bool CanSave(object obj)
        {
            return Tracker.Instance.ActiveGamePackage != null;
        }

        private void SaveHandler(object obj)
        {
            if (!string.IsNullOrWhiteSpace(mCurrentSavePath))
            {
                SaveProgress(mCurrentSavePath);
            }
            else
            {
                SaveAsHandler(obj);
            }
        }

        private async void SaveAsHandler(object obj)
        {
            string defaultSaveDataPath = Path.Combine(UserDirectory.Path, "saves");

            //  Ensure the default save directory exists
            try
            {
                if (!Directory.Exists(defaultSaveDataPath))
                    Directory.CreateDirectory(defaultSaveDataPath);
            }
            catch
            {
                PushMarkdownNotification(NotificationType.Error, string.Format(
@"### Failed to Save Progress
Failed to create the default save directory at ```{0}```. Make sure you have available disk space and permissions for the default location.",
defaultSaveDataPath)
);
            }

            string filename = await DialogService.Instance.SaveFileAsync("EmoTracker Save File (*.json)|*.json", defaultSaveDataPath);
            if (filename != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
                SaveProgress(filename);
            }
        }

        private bool SaveProgress(string path)
        {
            if (!CanSave(null))
                return false;

            try
            {
                bool bResult = Tracker.Instance.SaveProgress(path, (JObject root) =>
                {
                    root["main_window_width"] = WindowService.Instance.MainWindowWidth;
                    root["main_window_height"] = WindowService.Instance.MainWindowHeight;

                    JObject extensionData = new JObject();
                    bool bAddedAny = false;

                    foreach (Extension extension in ExtensionManager.Instance.Extensions)
                    {
                        JToken data = extension.SerializeToJson();
                        if (data != null)
                        {
                            extensionData[extension.UID] = data;
                            bAddedAny = true;
                        }
                    }

                    if (bAddedAny)
                        root["extensions"] = extensionData;
                });

                if (bResult)
                {
                    mCurrentSavePath = path;

                    PushMarkdownNotification(NotificationType.Message, string.Format(
    @"### Progress Saved
Successfully saved progress to ```{0}```",
                    path)
                    );

                    return true;
                }
            }
            catch
            {
            }


            PushMarkdownNotification(NotificationType.Error, string.Format(
@"### Failed to Save Progress
Failed to save progress to ```{0}```. Make sure you have available disk space and permissions for the selected location.",
                path));

            return false;
        }

        private bool LoadProgress(string path)
        {
            if (Tracker.Instance.LoadProgress(path, (JObject root) =>
            {
                WindowService.Instance.MainWindowWidth = root.GetValue<double>("main_window_width", WindowService.Instance.MainWindowWidth);
                WindowService.Instance.MainWindowHeight = root.GetValue<double>("main_window_height", WindowService.Instance.MainWindowHeight);

                JObject extensionData = root.GetValue<JObject>("extensions");
                if (extensionData != null)
                {
                    foreach (JProperty property in extensionData.Properties())
                    {
                        Extension target = ExtensionManager.Instance.FindExtensionByUID(property.Name);
                        if (target != null)
                            target.DeserializeFromJson(property.Value);
                    }
                }
            }))
            {
                AcquireLayouts();
                return true;
            }

            return false;
        }


        #endregion

        #region -- Assistance --

        private bool CanOpenPackageDocumentation(object obj = null)
        {
            if (Tracker.Instance.ActiveGamePackage != null)
            {
                PackageRepositoryEntry entry = PackageManager.Instance.FindRepositoryEntry(Tracker.Instance.ActiveGamePackage.UniqueID);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.DocumentationURL))
                    return true;
            }

            return false;
        }

        private void OpenPackageDocumentation(object obj = null)
        {
            if (Tracker.Instance.ActiveGamePackage != null)
            {
                PackageRepositoryEntry entry = PackageManager.Instance.FindRepositoryEntry(Tracker.Instance.ActiveGamePackage.UniqueID);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.DocumentationURL))
                    WindowService.Instance.OpenUrl(entry.DocumentationURL);
            }
        }

        #endregion

#region -- ICodeProvider --

        private void GetFilteredCodeAndProvider(ref string code, out ICodeProvider provider)
        {
            provider = ItemDatabase.Instance;

            if (code.StartsWith("@"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = LocationDatabase.Instance;
            }
            else if (code.StartsWith("$"))
            {
                code = code.Substring(1, code.Length - 1);
                provider = ScriptManager.Instance;
            }
        }

        public object FindObjectForCode(string code)
        {
            ICodeProvider provider;
            GetFilteredCodeAndProvider(ref code, out provider);

            return provider.FindObjectForCode(code);
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            ICodeProvider provider;
            GetFilteredCodeAndProvider(ref code, out provider);

            return provider.ProviderCountForCode(code, out maxAccessibility);
        }

#endregion

#region -- Package Load --

        public void Reload()
        {
            ExpireAllNotifications();
            Tracker.Instance.Reload();
        }
        private void Tracker_OnPackageLoadStarting(object sender, EventArgs e)
        {
            //  Undo history should not propagate across package reloads, and should also not include
            //  fields being set during load
            IUndoableTransactionProcessor undo = TransactionProcessor.Current as IUndoableTransactionProcessor;
            if (undo != null)
                undo.ClearUndoHistory();

            //  Reset the current save path
            mCurrentSavePath = null;

            ExtensionManager.Instance.OnPackageUnloaded();

            NotifyPropertyChanged("MainWindowTitle");
            ImageReferenceService.Instance.ClearImageCache();
            mPreviousNotifications.Clear();

            OpenPackageDocumentationCommand.RaiseCanExecuteChanged();
        }
        private void Tracker_OnPackageLoadComplete(object sender, EventArgs e)
        {
            ExtensionManager.Instance.OnPackageLoaded();
            AcquireLayouts();

            //  Undo history should not propagate across package reloads, and should also not include
            //  fields being set during load
            IUndoableTransactionProcessor undo = TransactionProcessor.Current as IUndoableTransactionProcessor;
            if (undo != null)
                undo.ClearUndoHistory();

            OpenPackageDocumentationCommand.RaiseCanExecuteChanged();

            // Kick off background pre-caching of all image references collected during pack load
            var collectedRefs = ImageReference.LastCollectedReferences;
            if (collectedRefs != null && collectedRefs.Count > 0)
                _ = ImageReferenceService.Instance.PreCacheImagesAsync(collectedRefs);

            WindowService.Instance.FocusMainWindow();
        }
        public void AcquireLayouts()
        {
            try
            {
                BroadcastLayout = LayoutManager.Instance.FindLayout("tracker_broadcast");
                TrackerLayout = LayoutManager.Instance.FindLayout("tracker_default");
                TrackerHorizontalLayout = LayoutManager.Instance.FindLayout("tracker_horizontal");
                TrackerVerticalLayout = LayoutManager.Instance.FindLayout("tracker_vertical");
                TrackerCaptureItemLayout = LayoutManager.Instance.FindLayout("tracker_capture_item");
            }
            catch (Exception)
            {
            }

            try
            {
                //Added checking for ActiveGamePacakge not being null here. Not sure what this is trying to do maybe be unused at this point
                if ((TrackerLayout == null && TrackerHorizontalLayout == null && TrackerVerticalLayout == null))
                {
                    LayoutManager.Instance.LegacyLoad(Tracker.Instance.ActiveGamePackage);

                    TrackerLayout = LayoutManager.Instance.FindLayout("tracker_default");
                    TrackerHorizontalLayout = LayoutManager.Instance.FindLayout("tracker_horizontal");
                    TrackerVerticalLayout = LayoutManager.Instance.FindLayout("tracker_vertical");
                    TrackerCaptureItemLayout = LayoutManager.Instance.FindLayout("tracker_capture_item");
                }
            }
            catch (Exception)
            {
            }

            try
            {
                if (BroadcastLayout == null)
                {
                    BroadcastLayout = new Layout();
                    ScriptManager.Instance.OutputWarning("Loading legacy broadcast layout data");
                    using (new LoggingBlock())
                    {
                        if (Tracker.Instance.ActiveGamePackage != null)
                            BroadcastLayout.Load(Tracker.Instance.ActiveGamePackage.Open("broadcast_layout.json"), Tracker.Instance.ActiveGamePackage);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

#endregion

#region -- Package Manager Views & Filtering --

        public enum AvailablePackageViewFilterType
        {
            All,
            Installed,
            InstalledAndHasUpdate
        }

        AvailablePackageViewFilterType mAvailablePackagesViewFilter = AvailablePackageViewFilterType.All;
        public AvailablePackageViewFilterType AvailablePackageViewFilter
        {
            get { return mAvailablePackagesViewFilter; }
            set
            {
                if (SetProperty(ref mAvailablePackagesViewFilter, value))
                {
                    NotifyPropertyChanged(nameof(AvailablePackagesGroupedView));
                }
            }
        }

        DelegateCommand mSetAvailablePackageViewFilterCommand;
        public DelegateCommand SetAvailablePackageViewFilterCommand
        {
            get { return mSetAvailablePackageViewFilterCommand; }
        }

        public void SetAvailablePackageViewFilter(object param)
        {
            try
            {
                AvailablePackageViewFilterType result;
                if (Enum.TryParse<AvailablePackageViewFilterType>(param.ToString(), out result))
                {
                    AvailablePackageViewFilter = result;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Groups available packages by game name for display in the Avalonia package manager.
        /// Each entry has a <c>Name</c> (game name) and <c>Items</c> (packages in that group).
        /// Uses the same sorting logic as WPF's RepoEntryGameNameSort and resolves
        /// game display names via PackageManager.FindGame.
        /// </summary>
        public IEnumerable<PackageGroup> AvailablePackagesGroupedView
        {
            get
            {
                var entries = (PackageManager.Instance.AvailablePackages ?? Enumerable.Empty<PackageRepositoryEntry>())
                    .Where(e => PackageFilter(e))
                    .ToList();

                // Sort using the same logic as WPF's RepoEntryGameNameSort
                entries.Sort((a, b) =>
                {
                    var x = PackageManager.Instance.FindGame(a.Game);
                    var y = PackageManager.Instance.FindGame(b.Game);

                    if (x != null && x.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                        return 1;
                    if (y != null && y.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                        return -1;

                    int result;

                    result = (x?.SeriesPriority ?? 0).CompareTo(y?.SeriesPriority ?? 0);
                    if (result != 0) return result;

                    result = CompareStringOrdinal(x?.Series, y?.Series);
                    if (result != 0) return result;

                    result = (x?.Priority ?? 0).CompareTo(y?.Priority ?? 0);
                    if (result != 0) return result;

                    result = CompareStringOrdinal(x?.Name, y?.Name);
                    if (result != 0) return result;

                    result = ComparePreferBool(
                        a.Flags.HasFlag(PackageFlags.Official),
                        b.Flags.HasFlag(PackageFlags.Official));
                    if (result != 0) return result;

                    result = ComparePreferBool(
                        a.Flags.HasFlag(PackageFlags.Featured),
                        b.Flags.HasFlag(PackageFlags.Featured));
                    if (result != 0) return result;

                    return CompareStringOrdinal(a.Author, b.Author);
                });

                // Group by resolved game display name (matches WPF's GroupDescription
                // which uses GameNameToActualGameNameConverter)
                return entries
                    .GroupBy(e =>
                    {
                        var game = PackageManager.Instance.FindGame(e.Game);
                        return game?.Name ?? e.Game;
                    })
                    .Select(g => new PackageGroup(g.Key, g));
            }
        }

        private static int CompareStringOrdinal(string x, string y)
        {
            if (!string.IsNullOrWhiteSpace(x) && string.IsNullOrWhiteSpace(y)) return -1;
            if (string.IsNullOrWhiteSpace(x) && !string.IsNullOrWhiteSpace(y)) return 1;
            return string.CompareOrdinal(x, y);
        }

        private static int ComparePreferBool(bool x, bool y)
        {
            if (x && !y) return -1;
            if (!x && y) return 1;
            return 0;
        }

        public IEnumerable<IGamePackage> InstalledPackagesView =>
            PackageManager.Instance.InstalledPackages ?? Enumerable.Empty<IGamePackage>();

        /// <summary>
        /// Groups installed packages by game name for display in the Avalonia settings menu.
        /// </summary>
        public IEnumerable<InstalledPackageGroup> InstalledPackagesGroupedView
        {
            get
            {
                var packages = (PackageManager.Instance.InstalledPackages ?? Enumerable.Empty<IGamePackage>()).ToList();

                packages.Sort((a, b) =>
                {
                    var x = PackageManager.Instance.FindGame(a.Game);
                    var y = PackageManager.Instance.FindGame(b.Game);

                    if (x != null && x.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                        return 1;
                    if (y != null && y.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                        return -1;

                    int result = CompareStringOrdinal(x?.Series, y?.Series);
                    if (result != 0) return result;

                    result = (x?.SeriesPriority ?? 0).CompareTo(y?.SeriesPriority ?? 0);
                    if (result != 0) return result;

                    result = (x?.Priority ?? 0).CompareTo(y?.Priority ?? 0);
                    if (result != 0) return result;

                    result = CompareStringOrdinal(x?.Name, y?.Name);
                    if (result != 0) return result;

                    return CompareStringOrdinal(a.Author, b.Author);
                });

                return packages
                    .GroupBy(p =>
                    {
                        var game = PackageManager.Instance.FindGame(p.Game);
                        return game?.Name ?? p.Game;
                    })
                    .Select(g => new InstalledPackageGroup(g.Key, g));
            }
        }

        public class InstalledPackageGroup
        {
            public string Name { get; }
            public IEnumerable<IGamePackage> Items { get; }
            public InstalledPackageGroup(string name, IEnumerable<IGamePackage> items)
            {
                Name = name;
                Items = items;
            }
        }

        public class PackageGroup
        {
            public string Name { get; }
            public IEnumerable<PackageRepositoryEntry> Items { get; }
            public PackageGroup(string name, IEnumerable<PackageRepositoryEntry> items)
            {
                Name = name;
                Items = items;
            }
        }

        void InitializePackageManagerViews()
        {
            mSetAvailablePackageViewFilterCommand = new DelegateCommand(new Action<object>(SetAvailablePackageViewFilter));

            PackageManager.Instance.OnGameListDownloaded += PackageManager_OnGameListDownloaded;


            //  Configure auto-refresh for the package manager
            System.Timers.Timer timer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);
            timer.Elapsed += (s, e) => OnRefreshPackageRepositoriesTimer(s, e);
            timer.AutoReset = true;
            timer.Start();

            PackageManager.Instance.OnRepositoryUpdated += PackageManager_OnRepositoryUpdated;
        }

        private void PackageManager_OnRepositoryUpdated(object sender, PackageRepository e)
        {
            OpenPackageDocumentationCommand.RaiseCanExecuteChanged();
        }

        private void OnRefreshPackageRepositoriesTimer(object sender, EventArgs e)
        {
            PackageManager.Instance.RefreshRemoteRepositories();
        }

        private void PackageManager_OnGameListDownloaded(object sender, EventArgs e)
        {
            NotifyPropertyChanged(nameof(AvailablePackagesGroupedView));
            NotifyPropertyChanged(nameof(InstalledPackagesView));
        }

        private string mPackFilterText;
        public string PackFilterText
        {
            get { return mPackFilterText; }
            set
            {
                if (SetProperty(ref mPackFilterText, value))
                {
                    RefreshPackageCollectionView();
                }
            }
        }

        private void RefreshPackageCollectionView()
        {
            NotifyPropertyChanged(nameof(AvailablePackagesGroupedView));
        }

        private bool PackageFilter(object obj)
        {
            PackageRepositoryEntry entry = obj as PackageRepositoryEntry;
            bool bAccept = true;

            if (!string.IsNullOrWhiteSpace(PackFilterText))
            {
                string filterText = PackFilterText.ToLower();
                bAccept = false;

                if (entry != null)
                {
                    PackageManager.Game game = PackageManager.Instance.FindGame(entry.Game);

                    bAccept = (!string.IsNullOrWhiteSpace(entry.Name) && entry.Name.ToLower().Contains(filterText)) ||
                              (!string.IsNullOrWhiteSpace(game.Name) && game.Name.ToLower().Contains(filterText)) ||
                              (!string.IsNullOrWhiteSpace(game.Series) && game.Series.ToLower().Contains(filterText)) ||
                              (!string.IsNullOrWhiteSpace(entry.Author) && entry.Author.ToLower().Contains(filterText)) ||
                              (entry.Flags.ToString().ToLower().Contains(filterText));
                }
            }

            if (bAccept)
            {
                switch (AvailablePackageViewFilter)
                {
                    case AvailablePackageViewFilterType.Installed:
                        {
                            switch (entry.Status)
                            {
                                case PackageRepositoryEntry.PackageStatus.Development:
                                case PackageRepositoryEntry.PackageStatus.Installed:
                                case PackageRepositoryEntry.PackageStatus.UpdateAvailable:
                                    break;

                                default:
                                    bAccept = false;
                                    break;
                            }
                        }
                        break;

                    case AvailablePackageViewFilterType.InstalledAndHasUpdate:
                        {
                            switch (entry.Status)
                            {
                                case PackageRepositoryEntry.PackageStatus.UpdateAvailable:
                                    break;

                                default:
                                    bAccept = false;
                                    break;
                            }
                        }
                        break;
                }
            }

            return bAccept;
        }

#region -- Package Sort Functions --

        private class RepoEntryGameNameSort : IComparer
        {
            public int Compare(object _x, object _y)
            {
                PackageRepositoryEntry xEntry = (PackageRepositoryEntry)_x;
                PackageRepositoryEntry yEntry = (PackageRepositoryEntry)_y;

                PackageManager.Game x = PackageManager.Instance.FindGame(xEntry.Game);
                PackageManager.Game y = PackageManager.Instance.FindGame(yEntry.Game);

                if (x.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    return 1;

                if (y.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    return -1;

                int result;

                result = CompareInt(x.SeriesPriority, y.SeriesPriority);
                if (result != 0)
                    return result;

                result = CompareString(x.Series, y.Series);
                if (result != 0)
                    return result;

                result = CompareInt(x.Priority, y.Priority);
                if (result != 0)
                    return result;

                result = CompareString(x.Name, y.Name);
                if (result != 0)
                    return result;

                result = ComparePreferredBool(xEntry.Flags.HasFlag(PackageFlags.Official), yEntry.Flags.HasFlag(PackageFlags.Official));
                if (result != 0)
                    return result;

                result = ComparePreferredBool(xEntry.Flags.HasFlag(PackageFlags.Featured), yEntry.Flags.HasFlag(PackageFlags.Featured));
                if (result != 0)
                    return result;

                result = CompareString(xEntry.Author, yEntry.Author);
                if (result != 0)
                    return result;

                return 0;
            }

            private int ComparePreferredBool(bool x, bool y)
            {
                if (x && !y)
                    return -1;

                if (!x && y)
                    return 1;

                return 0;
            }

            int CompareInt(int x, int y)
            {
                return x.CompareTo(y);
            }

            int CompareString(string x, string y)
            {
                if (!string.IsNullOrWhiteSpace(x) && string.IsNullOrWhiteSpace(y))
                    return -1;

                if (string.IsNullOrWhiteSpace(x) && !string.IsNullOrWhiteSpace(y))
                    return 1;

                return string.CompareOrdinal(x, y);
            }
        }

        private class GameNameSort : IComparer
        {
            public int Compare(object _x, object _y)
            {
                GamePackage xEntry = (GamePackage)_x;
                GamePackage yEntry = (GamePackage)_y;

                PackageManager.Game x = PackageManager.Instance.FindGame(xEntry.Game);
                PackageManager.Game y = PackageManager.Instance.FindGame(yEntry.Game);

                if (x.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    return 1;

                if (y.Key.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    return -1;

                int result;

                result = CompareString(x.Series, y.Series);
                if (result != 0)
                    return result;

                result = CompareInt(x.SeriesPriority, y.SeriesPriority);
                if (result != 0)
                    return result;

                result = CompareInt(x.Priority, y.Priority);
                if (result != 0)
                    return result;

                result = CompareString(x.Name, y.Name);
                if (result != 0)
                    return result;

                result = CompareString(xEntry.Author, yEntry.Author);
                if (result != 0)
                    return result;

                return x.Name.CompareTo(y.Name);
            }

            int CompareInt(int x, int y)
            {
                return x.CompareTo(y);
            }

            int CompareString(string x, string y)
            {
                if (!string.IsNullOrWhiteSpace(x) && string.IsNullOrWhiteSpace(y))
                    return -1;

                if (string.IsNullOrWhiteSpace(x) && !string.IsNullOrWhiteSpace(y))
                    return 1;

                return string.CompareOrdinal(x, y);
            }
        }

        #endregion

        #region -- Notification Service --

        ObservableCollection<Notification> mPreviousNotifications = new ObservableCollection<Notification>();
        public IEnumerable<Notification> PreviousNotifications
        {
            get { return mPreviousNotifications; }
        }

        ObservableCollection<Notification> mNotifications = new ObservableCollection<Notification>();
        public IEnumerable<Notification> Notifications
        {
            get { return mNotifications; }
        }

        public bool HasPendingNotifications
        {
            get { return mNotifications.Count > 0; }
        }

        System.Timers.Timer mNotificationUpdateTimer;


        void InitializeNotifications()
        {
            mNotificationUpdateTimer = new System.Timers.Timer(500);
            mNotificationUpdateTimer.Elapsed += (s, e) => Core.Services.Dispatch.BeginInvoke(() => NotificationExpirationTimer_Tick(s, e));
            mNotificationUpdateTimer.AutoReset = true;
            mNotificationUpdateTimer.Start();
            mNotifications.CollectionChanged += Notifications_CollectionChanged;

            ScriptManager.Instance.SetNotificationService(this);
        }

        void ExpireAllNotifications()
        {
            foreach (Notification n in mNotifications)
            {
                n.Expired = true;
            }
        }

        private void NotificationExpirationTimer_Tick(object sender, EventArgs e)
        {
            List<Notification> toRemove = new List<Notification>();
            DateTime now = DateTime.Now;

            foreach (Notification n in mNotifications)
            {

                if (n.ExpirationTime <= now || n.Expired)
                {
                    n.Expired = true;

                    if (now - n.ExpirationTime > TimeSpan.FromSeconds(2) && !toRemove.Contains(n))
                    {
                        toRemove.Add(n);
                    }
                }
            }

            foreach (Notification n in toRemove)
            {
                mNotifications.Remove(n);
            }
        }

        private void Notifications_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            NotifyPropertyChanged("HasPendingNotifications");
        }

        public void PushMarkdownNotification(NotificationType type, string markdown, int timeout = -1)
        {
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                //  Use the dispatcher here to make sure we're not eating up expiry time during long blocking operations
                //  this call may be nested within.
                Core.Services.Dispatch.BeginInvoke(() =>
                {
                    MarkdownNotification notification = new MarkdownNotification(timeout)
                    {
                        Markdown = markdown,
                        Type = type
                    };

                    while (mPreviousNotifications.Count >= 10)
                    {
                        mPreviousNotifications.RemoveAt(9);
                    }

                    mPreviousNotifications.Insert(0, notification);
                    mNotifications.Insert(0, notification);
                });
            }
        }

#endregion

#endregion
    }
}
