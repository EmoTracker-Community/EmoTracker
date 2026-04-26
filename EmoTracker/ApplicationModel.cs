#nullable enable annotations
using Avalonia.Input;
using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using EmoTracker.Data.Sessions;
using EmoTracker.Extensions;
using EmoTracker.Notifications;
using EmoTracker.Services;
using EmoTracker.UI;
using EmoTracker.UI.Media;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
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
        public AsyncDelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand ResetUserDataCommand { get; private set; }
        public DelegateCommand OpenPackOverrideFolderCommand { get; private set; }
        public DelegateCommand ActivatePackCommand { get; private set; }
        public DelegateCommand ShowPackageManagerCommand { get; private set; }
        public DelegateCommand ExportPackageOverrideCommand { get; private set; }
        public DelegateCommand ShowBroadcastViewCommand { get; private set; }
        public DelegateCommand ShowDeveloperConsoleCommand { get; private set; }

        public AsyncDelegateCommand SaveCommand { get; private set; }
        public AsyncDelegateCommand SaveAsCommand { get; private set; }
        public AsyncDelegateCommand OpenCommand { get; private set; }

        public DelegateCommand OpenPackageDocumentationCommand { get; private set; }

        public DelegateCommand ResetLayoutScaleCommand { get; private set; }

        public DelegateCommand InstallPackageCommand { get; private set; }

        public DelegateCommand  UninstallPackageCommand { get; private set; }

        private Layout mBroadcastLayout = new Layout();
        private Layout mTrackerLayout;
        private Layout mTrackerHorizontalLayout;
        private Layout mTrackerVerticalLayout;
        private Layout mTrackerCaptureItemLayout;

        // -------- Phase 6 step 7 / Phase 7.5: PackageInstance + PrimaryState --

        // Phase 7.5: collection of all live PackageInstances. Today this
        // typically contains exactly one (the one bound to the active
        // pack); Phase 7.6+ UI work will add the ability to load
        // additional packs without unloading the first.
        readonly System.Collections.ObjectModel.ObservableCollection<PackageInstance> mPackageInstances
            = new System.Collections.ObjectModel.ObservableCollection<PackageInstance>();

        public System.Collections.ObjectModel.ObservableCollection<PackageInstance> PackageInstances => mPackageInstances;

        // Phase 7.6: collection of all live WindowContexts. Each
        // TrackerWindow registers its context on activation; closing the
        // window removes it.
        readonly System.Collections.ObjectModel.ObservableCollection<WindowContext> mWindows
            = new System.Collections.ObjectModel.ObservableCollection<WindowContext>();
        public System.Collections.ObjectModel.ObservableCollection<WindowContext> Windows => mWindows;

        WindowContext mCurrentlyActiveWindowContext;
        /// <summary>
        /// Phase 7.6: the most recently focused window's context, or null
        /// if no window is yet active. Updated by the window's
        /// <c>Activated</c> handler.
        /// </summary>
        public WindowContext CurrentlyActiveWindowContext
        {
            get { return mCurrentlyActiveWindowContext; }
            internal set { SetProperty(ref mCurrentlyActiveWindowContext, value); }
        }

        // Phase 7.6: register a window's context. Called by the
        // TrackerWindow during ctor.
        internal void RegisterWindow(WindowContext ctx)
        {
            if (ctx == null) return;
            if (!mWindows.Contains(ctx))
                mWindows.Add(ctx);
        }

        internal void UnregisterWindow(WindowContext ctx)
        {
            if (ctx == null) return;
            mWindows.Remove(ctx);
            if (ReferenceEquals(mCurrentlyActiveWindowContext, ctx))
                CurrentlyActiveWindowContext = mWindows.Count > 0 ? mWindows[0] : null;
        }

        /// <summary>
        /// Phase 7.6 / 7.9: spawn a new TrackerWindow hosting only
        /// <paramref name="state"/>, moving the state out of
        /// <paramref name="sourceCtx"/>. Returns the new window's context.
        /// </summary>
        public WindowContext OpenStateInNewWindow(WindowContext sourceCtx, TrackerState state)
        {
            if (state == null) return null;
            sourceCtx?.RemoveState(state);
            var newWindow = new MainWindow();
            newWindow.WindowContext.AddState(state);
            newWindow.Show();
            return newWindow.WindowContext;
        }

        private PackageInstance mActivePackageInstance;

        /// <summary>
        /// Phase 6: the currently-active <see cref="PackageInstance"/> —
        /// owns the definitional state and the live primary
        /// <see cref="TrackerState"/> instances. Constructed when a pack
        /// activates (post-load); replaced when a different pack activates;
        /// null before any pack has been loaded.
        ///
        /// <para>
        /// Lifetime is owned by ApplicationModel, NOT by PackageManager
        /// (per plan §6.1) — PackageManager handles pack discovery /
        /// install / resolution; PackageInstance tracks the active
        /// session of a pack. They communicate only via events.
        /// </para>
        /// </summary>
        public PackageInstance ActivePackageInstance
        {
            get { return mActivePackageInstance; }
            private set { SetProperty(ref mActivePackageInstance, value); NotifyPropertyChanged(nameof(PrimaryState)); }
        }

        /// <summary>
        /// Phase 6: the active primary <see cref="TrackerState"/> the UI
        /// is bound to. Null before any pack is loaded; otherwise the
        /// first state in <see cref="ActivePackageInstance"/>.States.
        /// (Phase 6 step 7 establishes one primary; multi-primary support
        /// is a later enhancement.)
        ///
        /// <para>
        /// Today, <c>PrimaryState.Items</c> / <c>PrimaryState.Locations</c>
        /// / etc. are the same instances as <c>ApplicationModel.Instance?.PrimaryState?.Items</c> /
        /// <c>ApplicationModel.Instance?.PrimaryState?.Locations</c> / etc. (the primary state
        /// "adopts" the singletons populated by pack-load). When step 8
        /// introduces coordinated <c>TrackerState.Fork()</c>, additional
        /// states will hold their own catalogs distinct from the primary's.
        /// </para>
        /// </summary>
        public TrackerState PrimaryState
        {
            get
            {
                var pi = mActivePackageInstance;
                if (pi == null) return null;
                foreach (var kvp in pi.States)
                    return kvp.Value;
                return null;
            }
        }

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

            // Phase 7.1: pre-allocate the primary state + PackageInstance
            // BEFORE any pack-load reaches Tracker.Reload. PackageLoader's
            // new "load into a target state" model requires a target to
            // exist; pre-allocation guarantees one is always installed in
            // SessionContext.ActiveState by the time Tracker.Reload fires.
            PreallocatePrimaryState();

            // Notification service install — now that the primary state's
            // ScriptManager exists, give it the back-reference for
            // pack-script-driven notifications.
            PrimaryState?.Scripts?.SetNotificationService(this);

            Tracker.Instance.OnPackageLoadStarting += Tracker_OnPackageLoadStarting;
            Tracker.Instance.OnPackageLoadComplete += Tracker_OnPackageLoadComplete;

            RefreshCommand = new AsyncDelegateCommand(RefreshHandler);
            ResetUserDataCommand = new DelegateCommand(ResetUserDataHandler);
            OpenPackOverrideFolderCommand = new DelegateCommand(OpenPackOverrideFolderHandler);
            ActivatePackCommand = new DelegateCommand(ActivatePackHandler);
            ShowPackageManagerCommand = new DelegateCommand(ShowPackManagerHandler);
            ExportPackageOverrideCommand = new DelegateCommand(ExportPackageOverrideHandler);
            SaveCommand = new AsyncDelegateCommand(SaveHandler, CanSave);
            SaveAsCommand = new AsyncDelegateCommand(SaveAsHandler, CanSave);
            OpenCommand = new AsyncDelegateCommand(OpenHandler);

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

                // Resolve game banner images into ResolvedImage so that
                // bindings ({Binding Game.Image.ResolvedImage}) update.
                // HTTP images bypass the ImageReferenceService pipeline
                // (they download asynchronously into IconUtility.sHttpCache),
                // so we bridge the two systems here.
                foreach (var game in PackageManager.Instance.AvailableGames)
                {
                    if (game.Image != null && game.Image.ResolvedImage == null)
                    {
                        var resolved = EmoTracker.UI.Media.ImageReferenceService.Instance.ResolveImageReference(game.Image);
                        if (resolved != null)
                            game.Image.ResolvedImage = resolved;
                    }
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        public void Initialize()
        {
            //  Start the image resolution service.  When --no-async-images is
            //  set, resolution falls back to synchronous on-demand behaviour.
            ImageReferenceService.Instance.SyncMode = Data.ApplicationSettings.Instance.NoAsyncImages;
            ImageReferenceService.Instance.Start();

            //  Load and start extensions
            Extensions.ExtensionManager.CreateInstance();
            Extensions.ExtensionManager.Instance.Start();

            // Phase 7.4: install the ExtensionManager as the per-state
            // lifecycle observer so per-state IStateScopedExtension
            // instances are attached / detached as states are created
            // / removed on the active PackageInstance.
            Data.Sessions.StateLifecycle.Observer = Extensions.ExtensionManager.Instance;

            //Open up the last active package if set and installed
            bool success;
            string msg;

            (success, msg) = Tracker.Instance.LoadDefaultPackage();

            if (!string.IsNullOrWhiteSpace(msg))
            {
                PushMarkdownNotification(NotificationType.Error, msg);
            }

            // Phase 6 step 7's RebindActivePackageInstanceFromSingletons
            // fires from Tracker_OnPackageLoadComplete (which runs as a
            // side effect of LoadDefaultPackage above). No explicit call
            // needed here.

            NotifyPropertyChanged("MainWindowTitle");
        }

        /// <summary>
        /// Phase 7.1: pre-allocates the primary <see cref="TrackerState"/>
        /// and an empty <see cref="PackageInstance"/> wrapping it. Called
        /// during <see cref="ApplicationModel"/> construction, BEFORE any
        /// pack-load reaches <c>Tracker.Reload</c> via
        /// <see cref="Initialize"/>'s <c>LoadDefaultPackage</c> call.
        ///
        /// <para>
        /// PackageLoader's new "load into a target" contract requires a
        /// target to exist before the pack-load orchestration runs. This
        /// method satisfies that invariant by allocating an empty primary
        /// state and installing it as <c>SessionContext.ActiveState</c>.
        /// Tracker.Reload then routes the load through PackageLoader,
        /// which populates the state's catalogs in-place.
        /// </para>
        /// </summary>
        void PreallocatePrimaryState()
        {
            mActivePackageInstance = new PackageInstance(package: null, activeVariant: null);

            var primary = new TrackerState(
                name: "primary",
                scripts: new ScriptManager(),
                transactions: TransactionProcessor.Current as IUndoableTransactionProcessor,
                items: new ItemDatabase(),
                locations: new LocationDatabase(),
                maps: new MapDatabase(),
                layouts: new LayoutManager());

            mActivePackageInstance.AdoptAsPrimary(primary);

            // Phase 7.5: track in the collection so multi-pack consumers
            // can enumerate every live PackageInstance.
            mPackageInstances.Add(mActivePackageInstance);

            // Install the active state for the in-Data layer's fallback
            // resolver (PrimaryStateModelResolver) and for any in-Data
            // callsites that need to reach the active state without an
            // OwnerState holder. Phase 7.6's WindowContext makes this
            // per-window; for now it's a single global slot.
            EmoTracker.Data.Sessions.SessionContext.ActiveState = primary;
        }

        // -------- Phase 7.5: multi-PackageInstance lifecycle ----------------

        /// <summary>
        /// Phase 7.5: load a pack into a freshly-allocated
        /// <see cref="PackageInstance"/> without disturbing any existing
        /// instance. Returns the new instance's primary state.
        ///
        /// <para>
        /// Today the existing single-pack flow (<c>LoadDefaultPackage</c>
        /// → <c>Tracker.Reload</c> → <c>RebindActivePackageInstanceFromSingletons</c>)
        /// remains the dominant pack-load path because Avalonia UI bindings
        /// and several extensions still assume one active set of catalogs
        /// (<c>ApplicationModel.Instance.PrimaryState.Items</c> etc).
        /// Phase 7.6's <c>WindowContext</c> binding migration unblocks the
        /// multi-pack scenario; this method exposes the API now so 7.6+
        /// UI work can use it.
        /// </para>
        /// </summary>
        public TrackerState LoadNewPack(IGamePackage package, IGamePackageVariant variant)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var pi = new PackageInstance(package, variant);

            // Allocate a fresh primary state with brand-new catalogs.
            var primary = new TrackerState(
                name: package.UniqueID + " #1",
                scripts: new ScriptManager(),
                transactions: new EmoTracker.Data.Core.Transactions.Processors.LocalTransactionProcessorWithUndo(),
                items: new ItemDatabase(),
                locations: new LocationDatabase(),
                maps: new MapDatabase(),
                layouts: new LayoutManager());
            pi.AdoptAsPrimary(primary);

            // Drive the package load into this state.
            EmoTracker.Data.Sessions.PackageLoader.LoadInto(primary, package, variant);

            mPackageInstances.Add(pi);

            return primary;
        }

        /// <summary>
        /// Phase 7.5: fork the given PackageInstance's primary state to
        /// produce an additional state on the same pack. Used by Phase 7.7
        /// "+ create new state from pack" UI.
        /// </summary>
        public TrackerState CreateAdditionalState(PackageInstance pi, string name = null)
        {
            if (pi == null) throw new ArgumentNullException(nameof(pi));
            var sourcePrimary = pi.States.Values.FirstOrDefault();
            if (sourcePrimary == null)
                throw new InvalidOperationException("PackageInstance has no primary state to fork from.");
            var fork = sourcePrimary.Fork(name);
            // The fork is registered in the PackageInstance.States via
            // AdoptAsPrimary so per-state extensions get attached. Multiple
            // states per PI is the headline 7.5 capability.
            pi.AdoptAsPrimary(fork);
            return fork;
        }

        /// <summary>
        /// Phase 7.5: tear down a PackageInstance and remove it from
        /// <see cref="PackageInstances"/>. All states owned by the PI are
        /// disposed; per-state extensions are detached via the lifecycle
        /// observer.
        /// </summary>
        public void ClosePackageInstance(PackageInstance pi)
        {
            if (pi == null) return;
            mPackageInstances.Remove(pi);
            if (ReferenceEquals(pi, mActivePackageInstance))
            {
                ActivePackageInstance = mPackageInstances.Count > 0 ? mPackageInstances[0] : null;
                EmoTracker.Data.Sessions.SessionContext.ActiveState = PrimaryState;
            }
            pi.Dispose();
        }

        /// <summary>
        /// Phase 7.1: replaces <see cref="ActivePackageInstance"/> with a
        /// fresh <see cref="PackageInstance"/> capturing the just-loaded
        /// pack's metadata. The primary <see cref="TrackerState"/> is
        /// preserved across pack-loads (its catalogs were reset + populated
        /// in-place by <see cref="PackageLoader.LoadInto"/>); only the
        /// PackageInstance shell — which holds the pack/variant references
        /// — is replaced.
        ///
        /// <para>
        /// The previous <c>RebindActivePackageInstanceFromSingletons</c>
        /// (Phase 6 §6.10.2) constructed a brand-new TrackerState that
        /// adopted whatever was in the catalog static shims, then ran
        /// <c>StampOwnerStateOnAdoptedModels</c> to populate the resolver.
        /// PackageLoader does both jobs now (the population happens before
        /// this method runs, with OwnerState + resolver registration baked
        /// into the load orchestration), so this method is small.
        /// </para>
        ///
        /// <para>
        /// We do <i>not</i> dispose the old <c>PackageInstance</c> — its
        /// dictionary contains the same <c>TrackerState</c> reference that
        /// the new PI now holds, and disposing the old PI would tear the
        /// state down. The old PI becomes a garbage object whose state
        /// reference is shed; GC reclaims it.
        /// </para>
        /// </summary>
        void RebindActivePackageInstanceFromSingletons()
        {
            // Capture the current primary state (still alive, just had its
            // catalogs reset+populated by PackageLoader.LoadInto).
            var primary = mActivePackageInstance?.States.Values.FirstOrDefault();
            if (primary == null)
            {
                // Defensive: PreallocatePrimaryState should have run during
                // ctor. If we got here without one, allocate now.
                PreallocatePrimaryState();
                primary = mActivePackageInstance.States.Values.First();
            }

            var pi = new PackageInstance(
                package: Tracker.Instance.ActiveGamePackage,
                activeVariant: Tracker.Instance.ActiveGamePackageVariant);
            pi.AdoptAsPrimary(primary);

            // Note: SessionContext.ActiveState is unchanged — same primary
            // state, just a new PackageInstance wrapping it. Per plan
            // §7.6, this becomes per-WindowContext.

            // Phase 7.5: swap the old PI out of the collection for the new
            // one (same primary state, different shell metadata).
            var oldIndex = mActivePackageInstance != null ? mPackageInstances.IndexOf(mActivePackageInstance) : -1;
            if (oldIndex >= 0)
                mPackageInstances[oldIndex] = pi;
            else
                mPackageInstances.Add(pi);

            ActivePackageInstance = pi;
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

        private async void RefreshHandler(object param, TaskCompletionSource<object> tcs)
        {
            WindowService.Instance.SetCursor(new Cursor(StandardCursorType.Wait));
            try
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
            finally
            {
                WindowService.Instance.SetCursor(new Cursor(StandardCursorType.Arrow));
                tcs?.TrySetResult(null);
            }
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

        private async void OpenHandler(object obj, TaskCompletionSource<object> tcs)
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

            tcs?.TrySetResult(null);
        }

        private bool CanSave(object obj)
        {
            return Tracker.Instance.ActiveGamePackage != null;
        }

        private void SaveHandler(object obj, TaskCompletionSource<object> tcs)
        {
            if (!string.IsNullOrWhiteSpace(mCurrentSavePath))
            {
                SaveProgress(mCurrentSavePath);
                tcs?.TrySetResult(null);
            }
            else
            {
                SaveAsHandler(obj, tcs);
            }
        }

        private async void SaveAsHandler(object obj, TaskCompletionSource<object> tcs)
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

            tcs?.TrySetResult(null);
        }

        // Phase 7.10: exposed to BundleService for per-state save round-trips.
        // (Pre-Phase-7 was private; visibility widened, signature unchanged.)
        public bool SaveProgress(string path)
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

        // Phase 7.10: exposed to BundleService for per-state load round-trips.
        public bool LoadProgress(string path)
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
            // Phase 6 step 11: route through PrimaryState for the per-state
            // code lookup. Falls back to singletons for the pre-pack-load
            // window where PrimaryState is null.
            var ps = PrimaryState;
#pragma warning disable CS0618 // legacy fallback before pack-load
            provider = (ICodeProvider)ps?.Items;
#pragma warning restore CS0618

            if (code.StartsWith("@"))
            {
                code = code.Substring(1, code.Length - 1);
#pragma warning disable CS0618 // legacy fallback before pack-load
                provider = (ICodeProvider)ps?.Locations;
#pragma warning restore CS0618
            }
            else if (code.StartsWith("$"))
            {
                code = code.Substring(1, code.Length - 1);
#pragma warning disable CS0618 // legacy fallback before pack-load
                provider = (ICodeProvider)(ps?.Scripts as ScriptManager);
#pragma warning restore CS0618
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

            // Phase 6 step 7: wrap the just-loaded singleton catalogs into a
            // PackageInstance + primary TrackerState. Fires for every pack-load
            // (initial / reload / variant-switch) — anytime the singletons get
            // repopulated, the wrapping primary state needs to be rebuilt. The
            // primary state ADOPTS the active singletons; behavior is unchanged
            // today (PrimaryState.Items == ApplicationModel.Instance?.PrimaryState?.Items, etc.). Step 8's
            // coordinated fork is what makes additional states useful for
            // multi-session-tracking scenarios.
            RebindActivePackageInstanceFromSingletons();

            OpenPackageDocumentationCommand.RaiseCanExecuteChanged();

            WindowService.Instance.FocusMainWindow();
        }
        public void AcquireLayouts()
        {
            // Phase 6 step 11: layouts come through the primary state, with
            // a singleton fallback for the pre-pack-load AcquireLayouts
            // path.
            LayoutManager layouts = PrimaryState?.Layouts;
#pragma warning disable CS0618 // legacy fallback if AcquireLayouts is called pre-RebindActivePackageInstanceFromSingletons
            layouts ??= ApplicationModel.Instance?.PrimaryState?.Layouts;
#pragma warning restore CS0618
            try
            {
                BroadcastLayout = layouts?.FindLayout("tracker_broadcast");
                TrackerLayout = layouts?.FindLayout("tracker_default");
                TrackerHorizontalLayout = layouts?.FindLayout("tracker_horizontal");
                TrackerVerticalLayout = layouts?.FindLayout("tracker_vertical");
                TrackerCaptureItemLayout = layouts?.FindLayout("tracker_capture_item");
            }
            catch (Exception)
            {
            }

            try
            {
                //Added checking for ActiveGamePacakge not being null here. Not sure what this is trying to do maybe be unused at this point
                if ((TrackerLayout == null && TrackerHorizontalLayout == null && TrackerVerticalLayout == null))
                {
                    layouts?.LegacyLoad(Tracker.Instance.ActiveGamePackage);

                    TrackerLayout = layouts?.FindLayout("tracker_default");
                    TrackerHorizontalLayout = layouts?.FindLayout("tracker_horizontal");
                    TrackerVerticalLayout = layouts?.FindLayout("tracker_vertical");
                    TrackerCaptureItemLayout = layouts?.FindLayout("tracker_capture_item");
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
#pragma warning disable CS0618 // logging-only access to ScriptManager singleton; per-state logging not a goal
                    ApplicationModel.Instance?.PrimaryState?.Scripts.OutputWarning("Loading legacy broadcast layout data");
#pragma warning restore CS0618
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
                    .Select(g =>
                    {
                        var game = PackageManager.Instance.FindGame(g.Key);
                        return new PackageGroup(g.Key, g, game);
                    });
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
            public PackageManager.Game Game { get; }
            public PackageGroup(string name, IEnumerable<PackageRepositoryEntry> items, PackageManager.Game game = null)
            {
                Name = name;
                Items = items;
                Game = game;
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

            // Phase 7.1: notification-service install moved out of
            // InitializeNotifications. PreallocatePrimaryState (called
            // shortly after this from the ctor) is what allocates the
            // primary state's ScriptManager — at this point in the ctor
            // it doesn't exist yet. The actual install moves to the
            // bottom of the ctor (after PreallocatePrimaryState runs).
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

        private void OnNotificationForceExpired(object sender, EventArgs e)
        {
            if (sender is Notification n)
            {
                n.ForceExpired -= OnNotificationForceExpired;
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

                    notification.ForceExpired += OnNotificationForceExpired;
                    mPreviousNotifications.Insert(0, notification);
                    mNotifications.Insert(0, notification);
                });
            }
        }

#endregion

#endregion
    }
}
