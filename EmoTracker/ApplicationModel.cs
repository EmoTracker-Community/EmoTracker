#nullable enable annotations
using Avalonia.Input;
using Avalonia.VisualTree;
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
    public class ApplicationModel : ObservableSingleton<ApplicationModel>, ICodeProvider, INotificationService, EmoTracker.Extensions.IApplicationContext
    {
        public AsyncDelegateCommand RefreshCommand { get; private set; }
        public DelegateCommand ResetUserDataCommand { get; private set; }
        public DelegateCommand OpenPackOverrideFolderCommand { get; private set; }
        public DelegateCommand ActivatePackCommand { get; private set; }
        public DelegateCommand NewEmptyTabCommand { get; private set; }
        public DelegateCommand ShowPackageManagerCommand { get; private set; }
        public DelegateCommand ExportPackageOverrideCommand { get; private set; }
        public DelegateCommand ShowBroadcastViewCommand { get; private set; }
        public DelegateCommand ShowDeveloperConsoleCommand { get; private set; }

        public AsyncDelegateCommand SaveCommand { get; private set; }
        public AsyncDelegateCommand SaveAsCommand { get; private set; }
        public AsyncDelegateCommand OpenCommand { get; private set; }

        // Multi-state workspace persistence: one JSON envelope captures
        // every open primary state across every window plus the window /
        // tab arrangement. SaveAll always prompts (no current-bundle path
        // tracked); the existing OpenCommand detects the workspace
        // envelope (by its `type` marker) and routes to the workspace
        // restore path automatically — single-pack save files continue
        // to load into the current tab.
        public AsyncDelegateCommand SaveAllCommand { get; private set; }

        public DelegateCommand OpenPackageDocumentationCommand { get; private set; }

        public DelegateCommand ResetLayoutScaleCommand { get; private set; }

        public DelegateCommand InstallPackageCommand { get; private set; }

        public DelegateCommand  UninstallPackageCommand { get; private set; }

        // BroadcastLayout is lazy-initialized in AcquireLayouts so its
        // OwnerState can be stamped to the live primary state at the time
        // of construction — rather than constructed null-state at field-
        // initializer time before any state exists.
        private Layout mBroadcastLayout;
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
            internal set
            {
                var prev = mCurrentlyActiveWindowContext;
                if (SetProperty(ref mCurrentlyActiveWindowContext, value))
                {
                    // Phase 7 XAML migration: PrimaryState is dynamic (it
                    // tracks CurrentlyActiveWindowContext.ActiveState). When
                    // the active window changes, PrimaryState changes too —
                    // fire PropertyChanged so XAML bindings against
                    // {Binding PrimaryState.X, Source={x:Static AppModel}}
                    // rebind. Subscribe to the new context's ActiveState
                    // changes so tab switches within the active window
                    // also fire PrimaryState changed.
                    if (prev != null)
                        prev.PropertyChanged -= OnActiveWindowContextPropertyChanged;
                    if (value != null)
                        value.PropertyChanged += OnActiveWindowContextPropertyChanged;
                    NotifyPropertyChanged(nameof(PrimaryState));
                }
            }
        }

        void OnActiveWindowContextPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowContext.ActiveState))
                NotifyPropertyChanged(nameof(PrimaryState));
        }

        // Phase 7.6: register a window's context. Called by the
        // TrackerWindow during ctor. Triggers per-window extension
        // attach via ExtensionManager.
        internal void RegisterWindow(WindowContext ctx)
        {
            if (ctx == null) return;
            if (!mWindows.Contains(ctx))
            {
                mWindows.Add(ctx);
                // Allocate this window's IWindowExtension instances.
                Extensions.ExtensionManager.Instance.OnWindowRegistered(ctx);
            }
        }

        internal void UnregisterWindow(WindowContext ctx)
        {
            if (ctx == null) return;
            // Detach window-scoped extensions before removing the context
            // so OnDetachedFromWindow can still observe its host.
            Extensions.ExtensionManager.Instance.OnWindowUnregistered(ctx);
            mWindows.Remove(ctx);
            if (ReferenceEquals(mCurrentlyActiveWindowContext, ctx))
                CurrentlyActiveWindowContext = mWindows.Count > 0 ? mWindows[0] : null;
        }

        // ---- IApplicationContext --------------------------------------
        IReadOnlyList<PackageInstance> Extensions.IApplicationContext.PackageInstances => mPackageInstances;
        IReadOnlyList<WindowContext> Extensions.IApplicationContext.Windows => mWindows;

        /// <summary>
        /// Phase 7.6 / 7.9: spawn a new TrackerWindow hosting only
        /// <paramref name="state"/>, moving the state out of
        /// <paramref name="sourceCtx"/>. Returns the new window's context.
        ///
        /// <para>
        /// <paramref name="screenPosition"/> (optional) places the new
        /// window at a specific screen point — used by the tab tear-off
        /// flow so the new window appears under the user's cursor.
        /// When null, the window uses the platform default.
        /// </para>
        /// </summary>
        public WindowContext OpenStateInNewWindow(WindowContext sourceCtx, TrackerState state, Avalonia.PixelPoint? screenPosition = null)
        {
            if (state == null) return null;
            sourceCtx?.RemoveState(state);

            // seedWithPrimaryState=false: tear-off windows start empty;
            // we add only the torn-off state below.
            var newWindow = new MainWindow(seedWithPrimaryState: false);
            if (screenPosition.HasValue)
            {
                newWindow.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
                newWindow.Position = screenPosition.Value;
            }
            newWindow.WindowContext.AddState(state);
            newWindow.Show();

            // Activate so the new window comes to the foreground after a
            // tear-off — without this, on some platforms the just-spawned
            // window can end up behind the source window (Z-order is
            // determined by the OS at Show time, and the source window has
            // focus from the pointer-released event).
            try { newWindow.Activate(); } catch { /* defensive */ }
            return newWindow.WindowContext;
        }

        /// <summary>
        /// Promotes another live window to be the desktop's
        /// <see cref="Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime.MainWindow"/>
        /// before <paramref name="closing"/> shuts.
        ///
        /// <para>
        /// Why: <c>App.axaml.cs</c> sets
        /// <see cref="Avalonia.Controls.ShutdownMode.OnMainWindowClose"/>
        /// — closing the lifetime's MainWindow tears down the entire
        /// process. When the user merges the original (= main) window
        /// into a tear-off, or closes/empties out the original via tab
        /// drag, we want the OTHER live window(s) to keep the app alive.
        /// Reassigning <c>desktop.MainWindow</c> to one of them before
        /// the original closes converts the close into a normal window
        /// close instead of an app shutdown.
        /// </para>
        ///
        /// <para>
        /// No-op if <paramref name="closing"/> isn't the current
        /// <c>MainWindow</c>, or if no other live window exists (in
        /// which case shutting down on the close is correct).
        /// </para>
        /// </summary>
        public void PromoteAlternativeMainWindowIfNeeded(Avalonia.Controls.Window closing)
        {
            if (closing == null) return;
            try
            {
                if (!(Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop))
                    return;
                if (!ReferenceEquals(desktop.MainWindow, closing))
                    return;

                foreach (var ctx in mWindows)
                {
                    if (ctx.OwnerWindow is Avalonia.Controls.Window other
                        && !ReferenceEquals(other, closing))
                    {
                        desktop.MainWindow = other;
                        return;
                    }
                }
            }
            catch { /* defensive */ }
        }

        /// <summary>
        /// Tear-off helper: returns the <see cref="WindowContext"/> whose
        /// owning window's screen-space bounds contain
        /// <paramref name="screenPoint"/>, or null if the point is outside
        /// every live EmoTracker window. Used by the tab strip's
        /// release-time drop logic to decide between "dock into existing
        /// window" and "spawn a new window at this point".
        /// </summary>
        public WindowContext FindWindowContextAtScreenPoint(Avalonia.PixelPoint screenPoint)
        {
            foreach (var ctx in mWindows)
            {
                if (ctx.OwnerWindow is Avalonia.Controls.Window w)
                {
                    try
                    {
                        var pos = w.Position;
                        var width = (int)w.Bounds.Width;
                        var height = (int)w.Bounds.Height;
                        if (screenPoint.X >= pos.X && screenPoint.X <= pos.X + width
                            && screenPoint.Y >= pos.Y && screenPoint.Y <= pos.Y + height)
                            return ctx;
                    }
                    catch { /* defensive */ }
                }
            }
            return null;
        }

        /// <summary>
        /// Phase 7.8 polish: called when a tab strip switches the active
        /// state on the currently-focused window. Updates the in-Data
        /// layer's <c>SessionContext.ActiveState</c> and, if the target
        /// state lives in a different <see cref="PackageInstance"/> than
        /// the currently-loaded pack, drives <c>Tracker.Reload</c> to
        /// re-establish XAML bindings against the new pack.
        ///
        /// <para>
        /// <b>Same-PackageInstance forks:</b> for tabs from the same
        /// PackageInstance (i.e. forks of the active pack), updating
        /// SessionContext alone is insufficient because XAML bindings
        /// against <c>{x:Static Tracker.Instance.X}</c> resolved at load
        /// time don't refire on a state swap. This is documented as a
        /// limitation pending the deferred Phase 7.6 polish XAML
        /// migration to <c>WindowContext.ActiveState.X</c> bindings.
        /// </para>
        /// </summary>
        public void OnActiveStateSwitched(TrackerState newState)
        {
            if (newState == null) return;

            // Find the PackageInstance owning the new state.
            PackageInstance owningPI = null;
            foreach (var pi in mPackageInstances)
            {
                if (pi.States.ContainsKey(newState.Id))
                {
                    owningPI = pi;
                    break;
                }
            }
            if (owningPI == null) return;

            // If the active pack is already this PackageInstance's pack,
            // we're switching between forks of the same pack. Drive a
            // layout refresh so TrackerLayout / TrackerHorizontalLayout
            // PropertyChanged fires, and the LayoutControl re-binds
            // against the new state's (forked) layouts. Items, locations,
            // sections in those forked layouts have OwnerState=newState
            // (Phase 7 polish stamps this during fork) so cross-state
            // resolution lands correctly.
            if (ReferenceEquals(PrimaryState?.PackageInstance?.GamePackage, newState.PackageInstance?.GamePackage)
                && ReferenceEquals(PrimaryState?.PackageInstance?.ActiveVariant, newState.PackageInstance?.ActiveVariant))
            {
                ActivePackageInstance = owningPI;
                NotifyPropertyChanged(nameof(PrimaryState));
                // Re-acquire layouts from the new state and fire
                // PropertyChanged on the layout properties so the
                // LayoutControl rebuilds against the fork's layout tree.
                AcquireLayouts();
                return;
            }

            // Cross-PI switch: update Tracker's pack metadata WITHOUT
            // triggering Reload — the destination PackageInstance is
            // already populated. Reloading would clear the image cache,
            // wipe the destination state's catalogs, and re-run
            // init.lua against the singletons; none of that is what we
            // want for a tab swap. Instead, just point ambient slots at
            // the destination's pack so XAML bindings against
            // ActiveGamePackage refire.
            ActivePackageInstance = owningPI;
            Core.Services.Dispatch.BeginInvoke(() =>
            {
                // Pack metadata travels with the state; nothing to update on
                // the app side beyond fanning out PropertyChanged.
                AcquireLayouts();
            });
        }

        /// <summary>
        /// Phase 7.9: find the StateTabStripControl whose visual bounds
        /// contain <paramref name="screenPoint"/>, walking every live
        /// MainWindow. Used by the tab strip's drop logic to determine
        /// whether a drag-release should dock the state into another
        /// window's strip.
        /// </summary>
        public UI.StateTabStripControl FindTabStripAtScreenPoint(Avalonia.PixelPoint screenPoint)
        {
            // Phase 7.9: walk every live window's tab strip and check
            // whether screenPoint lands within its screen-space bounds.
            // We compute the strip's screen position by combining the
            // window's Position (a PixelPoint in screen-px) with the
            // strip's offset within the window (computed by accumulating
            // Bounds.Position up the visual tree until the window root).
            foreach (var ctx in mWindows)
            {
                if (ctx.OwnerWindow is MainWindow mw)
                {
                    var strip = mw.GetTabStrip();
                    if (strip == null) continue;
                    try
                    {
                        // Walk parents to compute offset from window origin.
                        double offX = 0, offY = 0;
                        Avalonia.Visual cur = strip;
                        while (cur != null && !ReferenceEquals(cur, mw))
                        {
                            offX += cur.Bounds.X;
                            offY += cur.Bounds.Y;
                            cur = cur.GetVisualParent();
                        }
                        var winScreenX = mw.Position.X + (int)offX;
                        var winScreenY = mw.Position.Y + (int)offY;
                        var widthPx = (int)strip.Bounds.Width;
                        var heightPx = (int)strip.Bounds.Height;
                        if (screenPoint.X >= winScreenX && screenPoint.X <= winScreenX + widthPx
                            && screenPoint.Y >= winScreenY && screenPoint.Y <= winScreenY + heightPx)
                            return strip;
                    }
                    catch
                    {
                        // Defensive: skip any window where the math throws.
                    }
                }
            }
            return null;
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
                // Phase 7 polish: prefer the currently-active window's
                // ActiveState so consumers (AcquireLayouts, MCP tools,
                // the script bridge) automatically pick up the user's
                // tab selection. Fall back to the first state in the
                // active PackageInstance if no window is bound yet
                // (e.g. during initial pack-load before any UI is up).
                var ctxState = mCurrentlyActiveWindowContext?.ActiveState;
                if (ctxState != null)
                    return ctxState;
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

                if (ActiveGamePackage != null && ActiveGamePackageVariant != null)
                {
                    title = string.Format("{0}  ::  {1} | {2}", title, ActiveGamePackage.DisplayName, ActiveGamePackageVariant.DisplayName);
                }
                else if (ActiveGamePackage != null)
                {
                    title = string.Format("{0}  ::  {1}", title, ActiveGamePackage.DisplayName);

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

            // Phase 7.1.h: no preallocated PrimaryState. The first
            // ApplicationModel.ActivatePackage call constructs the initial
            // PackageInstance against the (pack, variant) being activated
            // and forks its DefinitionalState to produce a primary state.

            // Install the cross-assembly resolver hooks so EmoTracker.Data
            // and EmoTracker.UI code can reach the currently-active primary
            // state without consulting an ambient slot. Each resolver
            // evaluates the relevant state on call rather than caching one.
            EmoTracker.Data.Sessions.ActiveSession.PrimaryStateResolver = () => PrimaryState;
            EmoTracker.Data.Sessions.ActiveSession.PackageInstanceForPackageResolver = pkg =>
                mPackageInstances.FirstOrDefault(p => ReferenceEquals(p.GamePackage, pkg));
            EmoTracker.Data.ApplicationSettings.ActiveSessionSettingsResolver = () => PrimaryState?.Settings;
            EmoTracker.UI.Converters.LayoutReferenceConverter.ActiveLayoutsResolver = () => PrimaryState?.Layouts;

            // Notification service install — now that the primary state's
            // ScriptManager exists, give it the back-reference for
            // pack-script-driven notifications.
            PrimaryState?.Scripts?.SetNotificationService(this);

            // Subscribe to PackageLoader's static events so the app-wide
            // side effects (image cache clear, layout refresh, extension
            // notifications) fire on every pack-load against any state.
            PackageLoader.OnPackageLoadStarting += OnPackageLoadStartingHandler;
            PackageLoader.OnPackageLoadComplete += OnPackageLoadCompleteHandler;

            RefreshCommand = new AsyncDelegateCommand(RefreshHandler);
            ResetUserDataCommand = new DelegateCommand(ResetUserDataHandler);
            OpenPackOverrideFolderCommand = new DelegateCommand(OpenPackOverrideFolderHandler);
            ActivatePackCommand = new DelegateCommand(ActivatePackHandler);
            NewEmptyTabCommand = new DelegateCommand(_ => NewEmptyTab());
            ShowPackageManagerCommand = new DelegateCommand(ShowPackManagerHandler);
            ExportPackageOverrideCommand = new DelegateCommand(ExportPackageOverrideHandler);
            SaveCommand = new AsyncDelegateCommand(SaveHandler, CanSave);
            SaveAsCommand = new AsyncDelegateCommand(SaveAsHandler, CanSave);
            OpenCommand = new AsyncDelegateCommand(OpenHandler);
            SaveAllCommand = new AsyncDelegateCommand(SaveAllHandler, CanSaveAll);

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

        bool mInitialized;
        public void Initialize()
        {
            // Phase 7.6: Initialize is idempotent — additional MainWindow
            // instances (spawned by tear-off in Phase 7.9) call Initialize
            // in their ctor; we run the heavy setup once.
            if (mInitialized) return;
            mInitialized = true;

            //  Start the image resolution service.  When --no-async-images is
            //  set, resolution falls back to synchronous on-demand behaviour.
            ImageReferenceService.Instance.SyncMode = Data.ApplicationSettings.Instance.NoAsyncImages;
            ImageReferenceService.Instance.Start();

            //  Load and start extensions. Install the ExtensionManager
            //  as the lifecycle observer FIRST so package- and tracker-
            //  scoped extensions are attached as PackageInstances /
            //  TrackerStates are created during pack load.
            Data.Sessions.StateLifecycle.Observer = Extensions.ExtensionManager.Instance;
            Extensions.ExtensionManager.CreateInstance();
            Extensions.ExtensionManager.Instance.Start(this);

            //Open up the last active package if set and installed
            bool success;
            string msg;

            (success, msg) = LoadDefaultPackage();

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
        // -------- Phase 7.5 / 7.1.h: multi-PackageInstance lifecycle ---------

        /// <summary>
        /// Phase 7.1.h: load a pack into a freshly-allocated (or reused)
        /// <see cref="PackageInstance"/> and return a forked primary
        /// <see cref="TrackerState"/> for it. Equivalent to
        /// <see cref="ActivatePackage"/> minus the "add-to-window" side
        /// effect — used by callers (e.g. Bundle restore, MCP debug
        /// tooling) that want to control state placement themselves.
        /// </summary>
        public TrackerState LoadNewPack(IGamePackage package, IGamePackageVariant variant)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var pi = GetOrCreatePackageInstance(package, variant);
            string forkName = package.UniqueID + " #" + (pi.States.Count + 1);
            var primary = pi.DefinitionalState.Fork(forkName);
            pi.AdoptAsPrimary(primary);
            return primary;
        }

        /// <summary>
        /// Forks <paramref name="pi"/>'s
        /// <see cref="PackageInstance.DefinitionalState"/> to produce an
        /// additional primary state. Each fork is independent — a fresh
        /// snapshot of the pack-loaded definitional state.
        /// </summary>
        public TrackerState CreateAdditionalState(PackageInstance pi, string name = null)
        {
            if (pi == null) throw new ArgumentNullException(nameof(pi));
            if (pi.DefinitionalState == null)
                throw new InvalidOperationException("PackageInstance has no definitional state to fork from.");
            var fork = pi.DefinitionalState.Fork(name);
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
            }
            pi.Dispose();
        }

        /// <summary>
        /// Notifies UI bindings that pack metadata on the active state may
        /// have changed. Pack identity now lives on the state's
        /// <see cref="TrackerState.PackageInstance"/> back-reference; this
        /// method just fans out a <c>NotifyPropertyChanged(PrimaryState)</c>
        /// for any binding that reads through the active-pack getters.
        /// </summary>
        void OnActivePackageMetadataChanged()
        {
            NotifyPropertyChanged(nameof(PrimaryState));
        }

        // ShowBroadcastView routes to the currently-active MainWindow,
        // each of which owns its own per-window BroadcastView (see
        // MainWindow.ShowBroadcastView). This is the F2 / menu entry
        // point: the user gets a broadcast feed for the window they're
        // currently looking at, following its active tab.
        private void ShowBroadcastView(object obj)
        {
            var ctx = mCurrentlyActiveWindowContext ?? mWindows.FirstOrDefault();
            if (ctx?.OwnerWindow is MainWindow mw)
                mw.ShowBroadcastView();
        }

        // Surfaces "the broadcast view to use" for callsites that don't
        // know which window to ask (e.g., the screenshot tools). Returns
        // the active window's broadcast view if open, null otherwise.
        public BroadcastView BroadcastView
        {
            get
            {
                var ctx = mCurrentlyActiveWindowContext ?? mWindows.FirstOrDefault();
                return (ctx?.OwnerWindow as MainWindow)?.BroadcastView;
            }
        }

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
            if (ActiveGamePackage != null && !string.IsNullOrWhiteSpace(ActiveGamePackage.OverridePath))
            {
                try
                {
                    Directory.CreateDirectory(ActiveGamePackage.OverridePath);
                }
                catch { };

                if (Directory.Exists(ActiveGamePackage.OverridePath))
                    WindowService.Instance.OpenFolder(ActiveGamePackage.OverridePath);
                else
                    PushMarkdownNotification(NotificationType.Error, string.Format(
@"### Cannot open override folder
Failed to find or create the active pack's override folder at `{0}`.

Make sure you have available disk space and permissions for the selected location.",
ActiveGamePackage.OverridePath)
);
            }
        }

        private void ExportPackageOverrideHandler(object obj)
        {
            if (ActiveGamePackage != null)
            {
                string filename = obj as string;
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    GamePackage package = ActiveGamePackage as GamePackage;
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
            if (ActiveGamePackage != null)
            {
                if (ApplicationSettings.Instance.PromptOnRefreshClose)
                {
                    bool result = await DialogService.Instance.ShowYesNoAsync("Warning!", "Clearing overrides will cause you to lose all unsaved progress. Are you sure you want to continue?", defaultYes: false);
                    if (!result)
                        return;
                }

                ActiveGamePackage.ResetUserOverrides();
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

                if (variant != null)
                {
                    ActivatePackage(variant.Package, variant);
                }
                else if (package != null)
                {
                    ActivatePackage(package, null);
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
                // Sniff the JSON envelope: a multi-window workspace file
                // declares `type = "emotracker_workspace"`. Anything else
                // (including legacy save files which don't have a `type`
                // key at all) is treated as a single-pack save and loaded
                // into the current tab.
                bool isWorkspace = TrySniffWorkspaceFile(filename);
                if (isWorkspace)
                {
                    try
                    {
                        JObject root;
                        using (var reader = new System.IO.StreamReader(filename))
                            root = (JObject)JToken.ReadFrom(new Newtonsoft.Json.JsonTextReader(reader));

                        RestoreWorkspaceFromJObject(root);

                        PushMarkdownNotification(NotificationType.Message, string.Format(
@"### Workspace Loaded
Successfully restored workspace from ```{0}```",
                            filename));
                    }
                    catch (System.Exception ex)
                    {
                        await DialogService.Instance.ShowOKAsync("Failed to load workspace…",
                            "Failed to load the requested workspace save file.\n\n" + ex.Message);
                    }
                }
                else if (!LoadProgress(filename))
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

        // Quick sniff: read just enough of the JSON to determine whether
        // the file is a workspace envelope. Returns false on any IO /
        // parse error; the caller falls through to the single-pack path
        // and surfaces an error there if the file is genuinely broken.
        static bool TrySniffWorkspaceFile(string path)
        {
            try
            {
                using (var reader = new System.IO.StreamReader(path))
                using (var jr = new Newtonsoft.Json.JsonTextReader(reader))
                {
                    while (jr.Read())
                    {
                        if (jr.TokenType == Newtonsoft.Json.JsonToken.PropertyName && (string)jr.Value == "type")
                        {
                            if (jr.Read() && jr.TokenType == Newtonsoft.Json.JsonToken.String)
                                return (string)jr.Value == WorkspaceTypeMarker;
                            return false;
                        }
                        // Don't recurse into nested objects/arrays — the
                        // workspace marker is at the top level. Skip past
                        // any nested values we encounter on the way to the
                        // next top-level property.
                        if (jr.TokenType == Newtonsoft.Json.JsonToken.StartObject && jr.Depth > 1) jr.Skip();
                        else if (jr.TokenType == Newtonsoft.Json.JsonToken.StartArray && jr.Depth > 0) jr.Skip();
                    }
                }
            }
            catch { /* not parseable as JSON, or IO error — caller handles */ }
            return false;
        }

        private bool CanSave(object obj)
        {
            return ActiveGamePackage != null;
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

            var target = PrimaryState;
            if (target == null) return false;

            try
            {
                bool bResult = target.SaveProgress(path, (JObject root) =>
                {
                    root["main_window_width"] = WindowService.Instance.MainWindowWidth;
                    root["main_window_height"] = WindowService.Instance.MainWindowHeight;

                    JObject extensionData = new JObject();
                    bool bAddedAny = false;

                    // Persist app-wide extension state (the per-window /
                    // per-package / per-tracker scopes have lifecycle tied
                    // to their owners and don't roundtrip through this
                    // primary-state save).
                    foreach (var extension in ExtensionManager.Instance.ApplicationExtensions)
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
                    // Phase 7.11 polish: clear the modified marker on the
                    // active state once the save succeeds.
                    PrimaryState?.MarkClean();

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

        // ----- Workspace (multi-state, multi-window) save/load -----------

        // Workspace JSON envelope identifier — versioned so future format
        // changes can be detected and migrated. Bumped only when the
        // envelope shape itself changes incompatibly.
        const string WorkspaceTypeMarker = "emotracker_workspace";
        const int WorkspaceFormatVersion = 1;

        bool CanSaveAll(object obj)
        {
            // Saving requires at least one window with at least one
            // pack-loaded state. Otherwise the workspace would be empty.
            foreach (var ctx in mWindows)
            {
                foreach (var s in ctx.OpenStates)
                {
                    if (s.PackageInstance?.GamePackage != null)
                        return true;
                }
            }
            return false;
        }

        async void SaveAllHandler(object obj, TaskCompletionSource<object> tcs)
        {
            string defaultSaveDataPath = System.IO.Path.Combine(UserDirectory.Path, "saves");
            try
            {
                if (!System.IO.Directory.Exists(defaultSaveDataPath))
                    System.IO.Directory.CreateDirectory(defaultSaveDataPath);
            }
            catch { /* defensive */ }

            string filename = await DialogService.Instance.SaveFileAsync(
                "EmoTracker Workspace (*.json)|*.json", defaultSaveDataPath);
            if (string.IsNullOrWhiteSpace(filename))
            {
                tcs?.TrySetResult(null);
                return;
            }

            try
            {
                JObject root = BuildWorkspaceJObject();
                using (System.IO.TextWriter textWriter = new System.IO.StreamWriter(filename))
                using (Newtonsoft.Json.JsonTextWriter writer = new Newtonsoft.Json.JsonTextWriter(textWriter))
                {
                    writer.Formatting = Newtonsoft.Json.Formatting.Indented;
                    root.WriteTo(writer);
                }

                // Workspace save satisfies dirty markers across every state.
                foreach (var ctx in mWindows)
                    foreach (var s in ctx.OpenStates)
                        s.MarkClean();

                PushMarkdownNotification(NotificationType.Message, string.Format(
@"### Workspace Saved
Successfully saved {0} window(s) to ```{1}```",
                    mWindows.Count, filename));
            }
            catch (System.Exception ex)
            {
                PushMarkdownNotification(NotificationType.Error, string.Format(
@"### Failed to Save Workspace
Failed to save workspace to ```{0}```.

{1}",
                    filename, ex.Message));
            }

            tcs?.TrySetResult(null);
        }

        // Builds the JSON envelope: one entry per WindowContext, each
        // carrying the host window's screen geometry + an array of tabs
        // (state name + per-state save JObject + last-active flag).
        JObject BuildWorkspaceJObject()
        {
            var root = new JObject();
            root["type"] = WorkspaceTypeMarker;
            root["version"] = WorkspaceFormatVersion;
            root["creation_time"] = System.DateTime.Now.ToString();

            var windowsArr = new JArray();
            foreach (var ctx in mWindows)
            {
                var winObj = new JObject();
                winObj["sequence"] = ctx.Sequence;
                winObj["name"] = ctx.Name ?? string.Empty;

                if (ctx.OwnerWindow is Avalonia.Controls.Window w)
                {
                    try
                    {
                        winObj["x"] = w.Position.X;
                        winObj["y"] = w.Position.Y;
                        winObj["width"] = double.IsFinite(w.Width) ? w.Width : 0.0;
                        winObj["height"] = double.IsFinite(w.Height) ? w.Height : 0.0;
                        winObj["maximized"] = w.WindowState == Avalonia.Controls.WindowState.Maximized;
                    }
                    catch { /* defensive */ }
                }

                int activeIdx = ctx.ActiveState != null
                    ? ctx.OpenStates.IndexOf(ctx.ActiveState)
                    : -1;
                winObj["active_tab_index"] = activeIdx;

                var tabsArr = new JArray();
                foreach (var state in ctx.OpenStates)
                {
                    var tabObj = new JObject();
                    tabObj["name"] = state.Name ?? string.Empty;
                    var stateData = state.SaveProgressToJObject();
                    if (stateData != null)
                        tabObj["state"] = stateData;
                    // States with no pack (empty Ctrl+T tabs) still
                    // serialise their slot so the tab count survives;
                    // restore creates an empty TrackerState for them.
                    tabsArr.Add(tabObj);
                }
                winObj["tabs"] = tabsArr;

                windowsArr.Add(winObj);
            }
            root["windows"] = windowsArr;
            return root;
        }

        // Tears down the current arrangement and rebuilds every window /
        // tab from <paramref name="root"/>. The first saved window adopts
        // the existing primary MainWindow (so we don't leak it); each
        // additional saved window spawns a fresh MainWindow.
        void RestoreWorkspaceFromJObject(JObject root)
        {
            var windowsArr = root.GetValue<JArray>("windows");
            if (windowsArr == null || windowsArr.Count == 0) return;

            // 1. Clear current arrangement: remove all states from all
            //    windows and dispose their owning PackageInstances.
            foreach (var ctx in new System.Collections.Generic.List<WindowContext>(mWindows))
            {
                foreach (var s in new System.Collections.Generic.List<Data.Sessions.TrackerState>(ctx.OpenStates))
                    ctx.RemoveState(s);
            }
            foreach (var pi in new System.Collections.Generic.List<PackageInstance>(mPackageInstances))
            {
                try { pi.Dispose(); } catch { /* defensive */ }
            }
            mPackageInstances.Clear();

            // 2. Trim down to a single window — the existing primary —
            //    then add new windows as needed for additional saved
            //    entries. Closing extras here also clears their
            //    WindowMergeTracker / per-window broadcast lifecycles.
            var primaryWindow = mWindows.FirstOrDefault();
            if (primaryWindow == null) return; // app shutting down — bail.

            for (int i = mWindows.Count - 1; i >= 1; i--)
            {
                if (mWindows[i].OwnerWindow is Avalonia.Controls.Window w)
                {
                    PromoteAlternativeMainWindowIfNeeded(w);
                    try { w.Close(); } catch { /* defensive */ }
                }
            }

            // 3. Walk saved windows. Index 0 reuses the primary; further
            //    indices spawn new tear-off-shaped windows (their lifetime
            //    matches a regular tab strip tear-off, including
            //    per-window WindowMergeTracker + broadcast wiring).
            for (int i = 0; i < windowsArr.Count; i++)
            {
                var winObj = windowsArr[i] as JObject;
                if (winObj == null) continue;

                WindowContext targetCtx;
                MainWindow targetWindow;
                if (i == 0)
                {
                    targetCtx = primaryWindow;
                    targetWindow = primaryWindow.OwnerWindow as MainWindow;
                }
                else
                {
                    targetWindow = new MainWindow(seedWithPrimaryState: false);
                    targetCtx = targetWindow.WindowContext;
                    targetWindow.Show();
                }
                if (targetWindow == null) continue;

                // Window geometry — applied AFTER the new window has been
                // shown so the platform has finalised its size constraints.
                try
                {
                    bool maximized = winObj.GetValue<bool>("maximized", false);
                    int x = (int)winObj.GetValue<double>("x", targetWindow.Position.X);
                    int y = (int)winObj.GetValue<double>("y", targetWindow.Position.Y);
                    double width = winObj.GetValue<double>("width", 0.0);
                    double height = winObj.GetValue<double>("height", 0.0);

                    if (!maximized)
                    {
                        targetWindow.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
                        targetWindow.Position = new Avalonia.PixelPoint(x, y);
                        if (width > 0) targetWindow.Width = width;
                        if (height > 0) targetWindow.Height = height;
                    }
                    else
                    {
                        targetWindow.WindowState = Avalonia.Controls.WindowState.Maximized;
                    }
                }
                catch { /* defensive */ }

                // Tabs — for each saved tab, rebuild the state and attach
                // to this window's OpenStates.
                var tabsArr = winObj.GetValue<JArray>("tabs");
                if (tabsArr != null)
                {
                    foreach (var tabTok in tabsArr)
                    {
                        if (!(tabTok is JObject tabObj)) continue;

                        Data.Sessions.TrackerState state = RestoreStateFromTabJObject(tabObj);
                        if (state != null)
                            targetCtx.AddState(state, makeActive: false);
                    }
                }

                // Active tab.
                int activeIdx = winObj.GetValue<int>("active_tab_index", -1);
                if (activeIdx >= 0 && activeIdx < targetCtx.OpenStates.Count)
                    targetCtx.ActiveState = targetCtx.OpenStates[activeIdx];

                // Workspace restoration shouldn't leave dirty markers.
                foreach (var s in targetCtx.OpenStates)
                    s.MarkClean();
            }

            // 4. Refresh app-wide state derived from the (now-different)
            //    primary state.
            ActivePackageInstance = mWindows.FirstOrDefault()?.ActiveState?.PackageInstance;
            NotifyPropertyChanged(nameof(PrimaryState));
            FirePackageLoadedFanout();
        }

        // Builds a TrackerState from one tab's JObject. Returns null if the
        // tab can't be restored (e.g. its pack is no longer installed); the
        // caller decides whether to skip or surface a warning.
        Data.Sessions.TrackerState RestoreStateFromTabJObject(JObject tabObj)
        {
            string stateName = tabObj.GetValue<string>("name");
            var stateData = tabObj.GetValue<JObject>("state");
            if (stateData == null)
            {
                // Empty tab (Ctrl+T placeholder) — restore as such.
                return new Data.Sessions.TrackerState(stateName ?? "New Tab");
            }

            string packageUID = stateData.GetValue<string>("package_uid");
            string packageVariantUID = stateData.GetValue<string>("package_variant_uid");
            if (string.IsNullOrWhiteSpace(packageUID)) return null;

            IGamePackage package = PackageManager.Instance.FindInstalledPackage(packageUID);
            if (package == null) return null;
            IGamePackageVariant variant = !string.IsNullOrWhiteSpace(packageVariantUID)
                ? package.FindVariant(packageVariantUID)
                : package.AvailableVariants?.FirstOrDefault();

            // Get-or-create PI (multiple tabs from the same pack share one).
            var pi = GetOrCreatePackageInstance(package, variant);
            string forkName = !string.IsNullOrWhiteSpace(stateName) ? stateName : (package.UniqueID + " #" + (pi.States.Count + 1));
            var primary = pi.DefinitionalState.Fork(forkName);
            pi.AdoptAsPrimary(primary);

            if (!primary.LoadProgressFromJObject(stateData))
            {
                // Restoration failed — drop the orphan state from the PI so
                // we don't leak it into subsequent state lookups.
                try { pi.RemoveState(primary.Id); } catch { /* defensive */ }
                return null;
            }

            return primary;
        }

        // Phase 7.10: exposed to BundleService for per-state load round-trips.
        public bool LoadProgress(string path)
        {
            var target = PrimaryState;
            if (target == null) return false;

            if (target.LoadProgress(path, (JObject root) =>
            {
                WindowService.Instance.MainWindowWidth = root.GetValue<double>("main_window_width", WindowService.Instance.MainWindowWidth);
                WindowService.Instance.MainWindowHeight = root.GetValue<double>("main_window_height", WindowService.Instance.MainWindowHeight);

                JObject extensionData = root.GetValue<JObject>("extensions");
                if (extensionData != null)
                {
                    foreach (JProperty property in extensionData.Properties())
                    {
                        var ext = ExtensionManager.Instance.FindApplicationExtensionByUID(property.Name);
                        if (ext != null)
                            ext.DeserializeFromJson(property.Value);
                    }
                }
            }))
            {
                AcquireLayouts();
                // Phase 7.11 polish: a freshly-loaded state isn't dirty
                // — clear the modified marker for the active state.
                PrimaryState?.MarkClean();
                return true;
            }

            return false;
        }


        #endregion

        #region -- Assistance --

        private bool CanOpenPackageDocumentation(object obj = null)
        {
            if (ActiveGamePackage != null)
            {
                PackageRepositoryEntry entry = PackageManager.Instance.FindRepositoryEntry(ActiveGamePackage.UniqueID);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.DocumentationURL))
                    return true;
            }

            return false;
        }

        private void OpenPackageDocumentation(object obj = null)
        {
            if (ActiveGamePackage != null)
            {
                PackageRepositoryEntry entry = PackageManager.Instance.FindRepositoryEntry(ActiveGamePackage.UniqueID);
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
            PrimaryState?.Reload();
        }

        // ---- Phase 7.1.g: app-wide pack-load orchestration ----------------
        // Tracker singleton is gone; ApplicationModel is the entry point for
        // pack activation against the primary state.

        /// <summary>
        /// Activates <paramref name="package"/> (with optional
        /// <paramref name="variant"/>): finds an existing
        /// <see cref="PackageInstance"/> for the (pack, variant) pair or
        /// constructs a fresh one (running <see cref="PackageLoader.LoadInto"/>
        /// against its <see cref="PackageInstance.DefinitionalState"/>),
        /// then forks the definitional state to create a new primary
        /// <see cref="TrackerState"/> and adds it to the active window.
        /// </summary>
        public void ActivatePackage(IGamePackage package, IGamePackageVariant variant = null)
        {
            if (package == null) return;

            // Validate variant; fall back to first available.
            if (variant != null && package.AvailableVariants != null
                && !package.AvailableVariants.Contains(variant))
            {
                variant = null;
            }
            if (variant == null && package.AvailableVariants != null)
                variant = package.AvailableVariants.FirstOrDefault();

            var pi = GetOrCreatePackageInstance(package, variant);

            ApplicationSettings.Instance.LastActivePackage = package.UniqueID;
            ApplicationSettings.Instance.LastActivePackageVariant = variant?.UniqueID;
            PackageManager.Instance.RefreshActiveState();

            // Fork the PI's DefinitionalState to produce a fresh primary
            // state for this activation. The DefinitionalState retains the
            // canonical pack-loaded snapshot; primary states layer per-state
            // mutation on top via Phase-1 fork mechanics.
            string forkName = package.UniqueID + " #" + (pi.States.Count + 1);
            var primary = pi.DefinitionalState.Fork(forkName);
            pi.AdoptAsPrimary(primary);

            ActivePackageInstance = pi;

            // Open the new primary in the currently selected tab — clicking
            // a pack/variant from the installed-packs menu replaces whatever
            // was in the active tab rather than appending a new tab. The
            // user explicitly creates new tabs via Ctrl+T (NewEmptyTab).
            // During app startup no window is registered yet; the first
            // MainWindow's seedWithPrimaryState path picks the state up
            // via the PrimaryState getter fallback. Once a window exists
            // but its tab strip is empty (no ActiveState), ReplaceActiveState
            // falls through to AddState, so the first activation in a
            // freshly-spawned-empty window also lands a tab.
            var ctx = mCurrentlyActiveWindowContext ?? mWindows.FirstOrDefault();
            if (ctx != null)
            {
                var oldState = ctx.ReplaceActiveState(primary);
                if (oldState != null)
                {
                    // The replaced tab's state belongs to its
                    // PackageInstance — remove it through the PI so the
                    // per-state extension lifecycle observer is notified
                    // and the state's catalogs are disposed cleanly.
                    // States created via the empty-tab flow have no PI,
                    // in which case dispose directly.
                    var oldPi = oldState.PackageInstance;
                    if (oldPi != null)
                        oldPi.RemoveState(oldState.Id);
                    else
                        oldState.Dispose();
                }
            }

            NotifyPropertyChanged(nameof(PrimaryState));

            // Now that PrimaryState is non-null, run the deferred app-level
            // package-loaded side effects (extensions, layouts, etc.).
            // Pass the freshly-forked primary so its dirty flag is the
            // only one that gets cleared — sibling tabs hosting other
            // states keep their modifications intact.
            FirePackageLoadedFanout(primary);
        }

        /// <summary>
        /// Creates a new "empty" tab (a <see cref="TrackerState"/> with no
        /// pack loaded) on the currently-active window. Used by the
        /// <c>Ctrl+T</c> keyboard shortcut: an empty tab is the user's
        /// staging area for selecting which pack to load via the
        /// installed-packs menu (which then opens the pack into that
        /// tab via <see cref="WindowContext.ReplaceActiveState"/>).
        ///
        /// <para>
        /// Returns the newly-allocated state, or null if no window is
        /// available to host the tab.
        /// </para>
        /// </summary>
        public TrackerState NewEmptyTab()
        {
            var ctx = mCurrentlyActiveWindowContext ?? mWindows.FirstOrDefault();
            if (ctx == null) return null;

            var state = new TrackerState("New Tab");
            ctx.AddState(state, makeActive: true);
            return state;
        }

        /// <summary>
        /// Finds the existing <see cref="PackageInstance"/> matching
        /// <paramref name="package"/> / <paramref name="variant"/>, or
        /// constructs a new one and runs <see cref="PackageLoader.LoadInto"/>
        /// against its <see cref="PackageInstance.DefinitionalState"/>.
        /// </summary>
        public PackageInstance GetOrCreatePackageInstance(IGamePackage package, IGamePackageVariant variant)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var existing = mPackageInstances.FirstOrDefault(p =>
                ReferenceEquals(p.GamePackage, package)
                && ReferenceEquals(p.ActiveVariant, variant));
            if (existing != null) return existing;

            var pi = new PackageInstance(package, variant);

            // Phase 7.1.h: register the PI BEFORE the load so the
            // PackageInstanceForPackageResolver can find it during pack
            // parse — ImageReference factory methods stamp the PI back-ref
            // by walking this collection.
            mPackageInstances.Add(pi);

            // Load the pack into the PI's DefinitionalState. This is the
            // single canonical pack-load per (pack, variant) pair —
            // subsequent activations fork from here rather than re-loading.
            EmoTracker.Data.Sessions.PackageLoader.LoadInto(pi.DefinitionalState, package, variant);

            return pi;
        }

        /// <summary>True iff the primary state's active pack has the given UID.</summary>
        public bool IsActivePackage(string uniqueId) => PrimaryState?.IsActivePackage(uniqueId) ?? false;

        /// <summary>The active pack on the primary state.</summary>
        public IGamePackage ActiveGamePackage => PrimaryState?.PackageInstance?.GamePackage;

        /// <summary>The active variant on the primary state.</summary>
        public IGamePackageVariant ActiveGamePackageVariant => PrimaryState?.PackageInstance?.ActiveVariant;

        /// <summary>The pack's <c>DisabledImageFilterSpec</c> on the primary state.</summary>
        public string DisabledImageFilterSpec => PrimaryState?.DisabledImageFilterSpec ?? "grayscale, dim";

        /// <summary>The pack's <c>AllowResize</c> on the primary state.</summary>
        public bool AllowResize => PrimaryState?.AllowResize ?? true;

        /// <summary>
        /// App-wide event fired before any pack-load (against any state).
        /// Subscribers do NOT need to filter by target state — the legacy
        /// behaviour was app-wide.
        /// </summary>
        public event EventHandler PackageLoadStarting;

        /// <summary>
        /// App-wide event fired after any pack-load (against any state)
        /// completes.
        /// </summary>
        public event EventHandler PackageLoadComplete;

        /// <summary>
        /// Loads the user's last-used pack (or command-line override),
        /// falling back to defaults when the requested variant doesn't
        /// exist. Returns success + an optional warning message about
        /// fallback behaviour.
        /// </summary>
        public (bool, string) LoadDefaultPackage()
        {
            string loadpack = string.IsNullOrEmpty(ApplicationSettings.Instance.CommandLinePackage)
                ? ApplicationSettings.Instance.LastActivePackage
                : ApplicationSettings.Instance.CommandLinePackage;
            string loadvar = string.IsNullOrEmpty(ApplicationSettings.Instance.CommandLinePackageVariant)
                ? ApplicationSettings.Instance.LastActivePackageVariant
                : ApplicationSettings.Instance.CommandLinePackageVariant;

            var package = PackageManager.Instance.FindInstalledPackage(loadpack);
            if (package == null)
                return (true, string.Empty);

            IGamePackageVariant variant = null;
            bool found = false;
            if (package.AvailableVariants != null)
            {
                if (!string.IsNullOrWhiteSpace(loadvar))
                {
                    foreach (var v in package.AvailableVariants)
                    {
                        if (string.Equals(v.UniqueID, loadvar, StringComparison.Ordinal))
                        {
                            variant = v;
                            found = true;
                            break;
                        }
                    }
                }
                if (variant == null)
                    variant = package.AvailableVariants.FirstOrDefault();
            }

            ActivatePackage(package, variant);

            if (!found && !string.IsNullOrWhiteSpace(loadvar))
            {
                if (variant != null)
                    return (false, $"### Package Variant {loadvar} not found, loading default variant `{variant.UniqueID}` for {loadpack}");
                return (false, $"### Package Variant {loadvar} not found, loading default pack for {loadpack}");
            }
            return (true, string.Empty);
        }

        private void OnPackageLoadStartingHandler(object sender, PackageLoader.PackageLoadEventArgs e)
        {
            //  Undo history should not propagate across package reloads, and should also not include
            //  fields being set during load
            (PrimaryState?.Transactions as IUndoableTransactionProcessor)?.ClearUndoHistory();

            //  Reset the current save path
            mCurrentSavePath = null;

            // Per-package extensions are torn down via PackageInstance.Dispose
            // (which fires StateLifecycle.Observer.OnPackageInstanceDisposed
            // → ExtensionManager.OnPackageInstanceDisposed). No app-level
            // OnPackageUnloaded fan-out is needed; extensions that previously
            // implemented OnPackageUnloaded subscribed to PackageLoader's
            // event stream directly (see VoiceRecognitionExtension.Start).

            NotifyPropertyChanged("MainWindowTitle");
            // Phase 7.1.h: image caches are owned by PackageInstance now;
            // a pack-load starting on one PI doesn't disturb others, and
            // each PI's cache is freed by its Dispose. The service-level
            // ClearImageCache is no longer called here — only when an
            // entire PI tears down.
            mPreviousNotifications.Clear();

            OpenPackageDocumentationCommand.RaiseCanExecuteChanged();

            PackageLoadStarting?.Invoke(this, EventArgs.Empty);
        }
        private void OnPackageLoadCompleteHandler(object sender, PackageLoader.PackageLoadEventArgs e)
        {
            // Phase 7.1.h: when the load target is a PackageInstance's
            // DefinitionalState, no primary state exists yet — the caller
            // (ApplicationModel.ActivatePackage) is responsible for forking
            // a primary state and then driving FirePackageLoadedFanout.
            // Skip the app-level fan-out here in that case so extensions
            // (VoiceRecognition / AutoTracker / NoteTaking) and AcquireLayouts
            // don't see a null PrimaryState.
            var target = e.Target;
            if (target != null && ReferenceEquals(target, target.PackageInstance?.DefinitionalState))
                return;

            FirePackageLoadedFanout(target);
        }

        /// <summary>
        /// Phase 7.1.h: app-level package-loaded side effects — extension
        /// notification, layout acquisition, dirty-marker reset, focus
        /// restore, app-level <see cref="PackageLoadComplete"/> event.
        /// Invoked by <see cref="OnPackageLoadCompleteHandler"/> for
        /// primary-state loads (Reload / LoadProgress) and explicitly by
        /// <see cref="ActivatePackage"/> after the definitional-state load
        /// + fork has produced the new primary.
        ///
        /// <para>
        /// <paramref name="loadedState"/> is the state whose pack was just
        /// loaded (or null when the fan-out fires for a workspace
        /// restore covering many states; in that case the workspace
        /// restore path has already cleared the dirty flag on the
        /// states it touched). Only this state's dirty marker is
        /// cleared; other tabs' modified flags are left intact, so
        /// loading the same pack into a fresh tab doesn't surprise the
        /// user by clearing modifications on existing tabs.
        /// </para>
        /// </summary>
        void FirePackageLoadedFanout(TrackerState loadedState = null)
        {
            // App-level OnPackageLoaded fan-out is no longer needed — the
            // four-scope ExtensionManager already attached per-package and
            // per-state extensions when the PackageInstance / TrackerState
            // were created. Extensions that need to react to the load
            // event itself subscribe to PackageLoader.OnPackageLoadComplete
            // directly (see VoiceRecognitionExtension.Start, AutoTrackerExtension).
            AcquireLayouts();

            //  Undo history should not propagate across package reloads, and should also not include
            //  fields being set during load
            (PrimaryState?.Transactions as IUndoableTransactionProcessor)?.ClearUndoHistory();

            OnActivePackageMetadataChanged();

            // A freshly-loaded pack isn't dirty — but only clear the
            // marker on the specific state that was loaded. Loading the
            // same pack into a new tab must NOT clobber sibling tabs'
            // dirty flags (their state hasn't changed; only this tab's
            // catalogs were just rebuilt).
            loadedState?.MarkClean();

            OpenPackageDocumentationCommand.RaiseCanExecuteChanged();

            WindowService.Instance.FocusMainWindow();

            PackageLoadComplete?.Invoke(this, EventArgs.Empty);
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
                    layouts?.LegacyLoad(ActiveGamePackage);

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
                    var primary = PrimaryState;
                    BroadcastLayout = new Layout();
                    BroadcastLayout.OwnerState = primary;
                    primary?.RegisterModel(BroadcastLayout);
                    var primaryScripts = primary?.Scripts;
                    primaryScripts?.OutputWarning("Loading legacy broadcast layout data");
                    using (new LoggingBlock(primaryScripts))
                    {
                        if (ActiveGamePackage != null)
                            BroadcastLayout.Load(ActiveGamePackage.Open("broadcast_layout.json", PrimaryState?.PackageInstance?.ActiveVariant), ActiveGamePackage);
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
