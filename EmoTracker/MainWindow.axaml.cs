using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Layout;
using EmoTracker.Services.Updates;
using System;
using System.ComponentModel;
using System.Linq;

namespace EmoTracker
{
    public partial class MainWindow : Window
    {
        // Set to true when we cancel a close, restore the window, then re-close
        // so we can capture the accurate normal-state bounds before saving.
        private bool mIsRestoreClosing = false;

        /// <summary>
        /// Phase 7.6: per-window context — exposes the OpenStates / ActiveState
        /// for this window. Bound by the bottom-bar state manager (7.7) and tab
        /// strip (7.8). DataContext remains <see cref="ApplicationModel.Instance"/>
        /// for backwards compat with existing XAML bindings; this surface is
        /// reachable via <c>{Binding $self.WindowContext.X}</c> for new
        /// per-window bindings, or via Tag for code-behind helpers.
        /// </summary>
        public WindowContext WindowContext { get; }

        // Owned per-window tracker that watches title-bar drags and merges
        // this window's tabs into another EmoTracker window when the user
        // drops the source window inside the target. Disposed on window
        // close.
        readonly UI.WindowMergeTracker mMergeTracker;

        // Phase 7.9: helper for ApplicationModel.FindTabStripAtScreenPoint —
        // returns this window's tab strip control if hosted, else null.
        public UI.StateTabStripControl GetTabStrip()
            => this.FindControl<UI.StateTabStripControl>("StateTabStrip");

        public MainWindow() : this(seedWithPrimaryState: true) { }

        /// <summary>
        /// Phase 7.6 / 7.9: secondary windows opened via tear-off pass
        /// <paramref name="seedWithPrimaryState"/> = false so the new
        /// window's WindowContext starts empty. The caller (typically
        /// <see cref="ApplicationModel.OpenStateInNewWindow"/>) then
        /// explicitly adds the torn-off state.
        /// </summary>
        public MainWindow(bool seedWithPrimaryState)
        {
            // Phase 7.6: allocate per-window context BEFORE InitializeComponent
            // so XAML bindings via {Binding ElementName} can resolve it.
            WindowContext = new WindowContext("primary") { OwnerWindow = this };

            InitializeComponent();

            ApplicationModel.Instance.Initialize();
            DataContext = ApplicationModel.Instance;
            ApplicationModel.Instance.PropertyChanged += Instance_PropertyChanged;
            ApplicationModel.Instance.PropertyChanged += AllowResizePropertyChanged;

            // Phase 7.6: register with the app's window collection. The
            // first window seeds its WindowContext with the active primary
            // state so existing UI works unchanged. Tear-off windows
            // (seedWithPrimaryState=false) start empty; the caller adds
            // the torn-off state explicitly.
            ApplicationModel.Instance.RegisterWindow(WindowContext);
            if (seedWithPrimaryState)
            {
                var primary = ApplicationModel.Instance.PrimaryState;
                if (primary != null)
                    WindowContext.AddState(primary);
            }
            ApplicationModel.Instance.CurrentlyActiveWindowContext = WindowContext;
            this.Activated += (_, __) => ApplicationModel.Instance.CurrentlyActiveWindowContext = WindowContext;
            this.Closed += (_, __) => ApplicationModel.Instance.UnregisterWindow(WindowContext);

            // Phase 7 XAML migration: drive per-window layout refresh from
            // this window's WindowContext.ActiveState changes (rather than
            // the previously-used global ApplicationModel.TrackerLayout
            // slot, which couldn't differentiate between multiple windows).
            WindowContext.PropertyChanged += OnWindowContextPropertyChanged;
            EmoTracker.Data.Sessions.PackageLoader.OnPackageLoadComplete += OnAnyPackageLoadComplete;

            if (ApplicationSettings.Instance.InitialWidth >= 0.0)
                Width = ApplicationSettings.Instance.InitialWidth;
            if (ApplicationSettings.Instance.InitialHeight >= 0.0)
                Height = ApplicationSettings.Instance.InitialHeight;

            // Restore window position if previously saved
            if (!double.IsNaN(ApplicationSettings.Instance.InitialX) &&
                !double.IsNaN(ApplicationSettings.Instance.InitialY))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(
                    (int)ApplicationSettings.Instance.InitialX,
                    (int)ApplicationSettings.Instance.InitialY);
            }

            this.Loaded += MainWindow_Loaded;
            // Use Tunnel routing to match WPF's PreviewKeyDown — the window
            // handles shortcuts before any child control can consume the key.
            this.AddHandler(KeyDownEvent, MainWindow_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.PointerWheelChanged += MainWindow_PointerWheelChanged;

            // Drag-window-onto-window merge: when the user title-bar-drags
            // this window into another EmoTracker window's bounds and lets
            // go (idle ~300ms), the tracker moves all of this window's
            // tabs into the target and closes this window.
            mMergeTracker = new UI.WindowMergeTracker(this);

            // Per-window broadcast lifecycle. Each MainWindow owns its own
            // visible BroadcastView (lazy, opened via menu / F2) and its
            // own off-screen HiddenBroadcastWindow (auto-managed based on
            // settings + the host's active broadcast layout). Both bind to
            // this window's WindowContext.BroadcastLayout, so each
            // window's broadcast feed follows its own active tab.
            WindowContext.PropertyChanged += OnWindowContextBroadcastChanged;
            ApplicationSettings.Instance.PropertyChanged += OnAppSettingsBroadcastChanged;
            this.Closed += (_, __) =>
            {
                WindowContext.PropertyChanged -= OnWindowContextBroadcastChanged;
                ApplicationSettings.Instance.PropertyChanged -= OnAppSettingsBroadcastChanged;
                CloseBroadcastView();
                DestroyHiddenBroadcast();
            };
            ReconcileHiddenBroadcast();

            // Set initial layout and resize mode
            RefreshTrackerLayout();
            UpdateResizeMode();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Validate restored position is still on a visible screen
            EnsureWindowIsOnScreen();

            // Restore maximized state after position/size are set so the
            // normal-state bounds are established first
            if (ApplicationSettings.Instance.InitialMaximized)
                WindowState = WindowState.Maximized;

            if (this.FindControl<Button>("SettingsButton") is Button settingsBtn)
                settingsBtn.Click += SettingsButton_Click;

            if (this.FindControl<MenuItem>("CheckForUpdatesMenuItem") is MenuItem checkUpdatesItem)
                checkUpdatesItem.Click += CheckForUpdatesMenuItem_Click;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TrackerLayout?.Focus();
            });
        }

        private void EnsureWindowIsOnScreen()
        {
            var screens = Screens;
            if (screens == null || screens.ScreenCount == 0)
                return;

            var pos = Position;
            bool onAnyScreen = false;

            foreach (var screen in screens.All)
            {
                var bounds = screen.WorkingArea;
                // Check that at least part of the title bar (top-left corner + some margin) is visible
                if (pos.X + 50 > bounds.X && pos.X < bounds.X + bounds.Width &&
                    pos.Y >= bounds.Y && pos.Y < bounds.Y + bounds.Height)
                {
                    onAnyScreen = true;
                    break;
                }
            }

            if (!onAnyScreen)
            {
                // Reset to center of primary screen
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                var primary = screens.Primary ?? screens.All[0];
                var wa = primary.WorkingArea;
                var scaling = primary.Scaling;
                Position = new PixelPoint(
                    wa.X + (int)((wa.Width - Width * scaling) / 2),
                    wa.Y + (int)((wa.Height - Height * scaling) / 2));
            }
        }

        private async void CheckForUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await UpdateService.Instance.CheckAndShowUpdateWindowAsync();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                PopulateInstalledPackagesMenu();
                btn.ContextMenu.Open(btn);
            }
        }

        private void PopulateInstalledPackagesMenu()
        {
            if (this.FindControl<Button>("SettingsButton")?.ContextMenu is not ContextMenu menu)
                return;

            var installedMenu = menu.Items.OfType<MenuItem>()
                .FirstOrDefault(m => m.Name == "InstalledPackagesMenu");
            if (installedMenu == null)
                return;

            // Unsubscribe old items from command events before clearing.
            // MenuItem internally subscribes to ICommand.CanExecuteChanged when
            // Command is assigned. Clearing the collection leaves orphaned
            // subscriptions alive on the singleton ActivatePackCommand.
            foreach (var item in installedMenu.Items)
            {
                if (item is MenuItem outerMi)
                {
                    outerMi.Command = null;
                    foreach (var sub in outerMi.Items)
                    {
                        if (sub is MenuItem innerMi)
                            innerMi.Command = null;
                    }
                }
            }
            installedMenu.Items.Clear();

            var groups = ApplicationModel.Instance.InstalledPackagesGroupedView;
            foreach (var group in groups)
            {
                var gameMenuItem = new MenuItem { Header = group.Name };

                foreach (var package in group.Items)
                {
                    var packageMenuItem = new MenuItem
                    {
                        Header = FormatPackageHeader(package),
                        Command = ApplicationModel.Instance.ActivatePackCommand,
                        CommandParameter = package,
                    };

                    // Add variant sub-items
                    var variants = package.AvailableVariants?.ToList();
                    if (variants != null && variants.Count > 0)
                    {
                        foreach (var variant in variants)
                        {
                            var variantMenuItem = new MenuItem
                            {
                                Header = FormatVariantHeader(variant),
                                Command = ApplicationModel.Instance.ActivatePackCommand,
                                CommandParameter = variant,
                            };
                            packageMenuItem.Items.Add(variantMenuItem);
                        }
                    }

                    gameMenuItem.Items.Add(packageMenuItem);
                }

                installedMenu.Items.Add(gameMenuItem);
            }
        }

        private static object FormatPackageHeader(IGamePackage package)
        {
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, MaxWidth = 350 };
            panel.Children.Add(new Avalonia.Controls.TextBlock
            {
                Text = package.DisplayName,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                MaxWidth = 250,
            });

            if (!string.IsNullOrWhiteSpace(package.Author))
            {
                panel.Children.Add(new Avalonia.Controls.TextBlock
                {
                    Text = package.Author,
                    FontStyle = Avalonia.Media.FontStyle.Italic,
                    Margin = new Thickness(15, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = Avalonia.Media.Brushes.Gray,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    MaxWidth = 80,
                });
            }

            if (ApplicationModel.Instance.ActiveGamePackage == package)
            {
                panel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Avalonia.Media.Brushes.Gray,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = new Avalonia.Controls.TextBlock
                    {
                        Text = "Active",
                        FontSize = 9,
                        Foreground = Avalonia.Media.Brushes.Gray,
                        Margin = new Thickness(3, 1),
                    },
                });
            }

            return panel;
        }

        private static object FormatVariantHeader(IGamePackageVariant variant)
        {
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            panel.Children.Add(new Avalonia.Controls.TextBlock { Text = variant.DisplayName, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

            if (ApplicationModel.Instance.ActiveGamePackageVariant == variant)
            {
                panel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Avalonia.Media.Brushes.Gray,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = new Avalonia.Controls.TextBlock
                    {
                        Text = "Active",
                        FontSize = 9,
                        Foreground = Avalonia.Media.Brushes.Gray,
                        Margin = new Thickness(3, 1),
                    },
                });
            }

            return panel;
        }

        /// <summary>
        /// Check for the platform command modifier: Meta (Cmd) on macOS, Control on Windows/Linux.
        /// </summary>
        private static bool HasCmdModifier(KeyModifiers modifiers)
        {
            return modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        }

        private void MainWindow_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (HasCmdModifier(e.KeyModifiers))
            {
                int steps = (int)(e.Delta.Y * 3);
                ApplicationModel.Instance.IncrementMainLayoutScale(steps);
            }
        }

        public event EventHandler<KeyEventArgs> OnGlobalPreviewKeyDown;
        public event EventHandler<KeyEventArgs> OnGlobalPreviewKeyUp;

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F1)
            {
                if (ApplicationModel.Instance.OpenPackageDocumentationCommand.CanExecute(null))
                    ApplicationModel.Instance.OpenPackageDocumentationCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.F5)
            {
                ApplicationModel.Instance.RefreshCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.F2)
            {
                ApplicationModel.Instance.ShowBroadcastViewCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.F11)
            {
                ApplicationSettings.Instance.DisplayAllLocations = !ApplicationSettings.Instance.DisplayAllLocations;
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.S)
            {
                if (HasCmdModifier(e.KeyModifiers) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ApplicationModel.Instance.SaveAsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (HasCmdModifier(e.KeyModifiers))
                {
                    ApplicationModel.Instance.SaveCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.O && HasCmdModifier(e.KeyModifiers))
            {
                ApplicationModel.Instance.OpenCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.T && HasCmdModifier(e.KeyModifiers))
            {
                ApplicationModel.Instance.NewEmptyTabCommand?.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Z && HasCmdModifier(e.KeyModifiers))
            {
                (ApplicationModel.Instance.PrimaryState?.Transactions as IUndoableTransactionProcessor)?.Undo();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.D0 && HasCmdModifier(e.KeyModifiers))
            {
                ApplicationModel.Instance.ResetLayoutScale();
                e.Handled = true;
                return;
            }

            if (!e.Handled)
                OnGlobalPreviewKeyDown?.Invoke(this, e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            OnGlobalPreviewKeyUp?.Invoke(this, e);
            base.OnKeyUp(e);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // If closing while maximized or fullscreen, cancel this close, restore the window
            // to normal so the OS reports accurate bounds, then re-close on the next frame.
            if (!mIsRestoreClosing && WindowState != WindowState.Normal)
            {
                e.Cancel = true;
                mIsRestoreClosing = true;
                ApplicationSettings.Instance.InitialMaximized =
                    WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen;
                WindowState = WindowState.Normal;
                Avalonia.Threading.Dispatcher.UIThread.Post(Close, Avalonia.Threading.DispatcherPriority.Background);
                return;
            }

            // Save window state, position, and size for next launch.
            // At this point WindowState is Normal (either it was already, or we just restored it above).
            if (!mIsRestoreClosing)
                ApplicationSettings.Instance.InitialMaximized = false;
            ApplicationSettings.Instance.InitialX = Position.X;
            ApplicationSettings.Instance.InitialY = Position.Y;
            ApplicationSettings.Instance.InitialWidth = Width;
            ApplicationSettings.Instance.InitialHeight = Height;

            if (ApplicationSettings.Instance.PromptOnRefreshClose)
            {
                // For Phase 6 just close — async dialog in Phase 7
            }

            if (DeveloperConsole != null)
            {
                DeveloperConsole.Close();
                DeveloperConsole = null;
            }

            // Note: this window's per-window BroadcastView and
            // HiddenBroadcastWindow are closed via the Closed lambda
            // wired in the ctor; nothing to do here for them.

            // If this window IS the desktop's MainWindow and there's
            // another live EmoTracker window, promote that window so the
            // app stays alive after this close. Without this, the user
            // closing the original window via the X button would shut
            // down the entire process even when other windows are still
            // open — App.axaml.cs sets ShutdownMode.OnMainWindowClose.
            ApplicationModel.Instance.PromoteAlternativeMainWindowIfNeeded(this);

            base.OnClosing(e);
        }

        private void AllowResizePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationModel.AllowResize)
                || e.PropertyName == nameof(ApplicationModel.PrimaryState))
                UpdateResizeMode();
        }

        private void UpdateResizeMode()
        {
            if (!ApplicationModel.Instance.AllowResize)
            {
                CanResize = false;
                SizeToContent = SizeToContent.WidthAndHeight;
            }
            else
            {
                CanResize = true;
                SizeToContent = SizeToContent.Manual;
            }
        }

        private void Instance_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RefreshTrackerLayout();
        }

        private void RefreshTrackerLayout()
        {
            // At construction time Bounds may be 0×0 (not yet laid out), so fall back
            // to the logical Width/Height which are always set from settings or XAML defaults.
            double h = Bounds.Height > 0 ? Bounds.Height : Height;
            double w = Bounds.Width > 0 ? Bounds.Width : Width;
            bool vertical = h > w;

            // Phase 7 XAML migration: pull layouts from THIS window's
            // active state, not the global ApplicationModel.TrackerXxxLayout
            // slot. This makes per-window content selection work — each
            // window renders the layouts of its own active state, even
            // when multiple windows exist or tabs from different packs
            // are mixed.
            EmoTracker.Data.Layout.Layout layout = null;
            var layouts = WindowContext?.ActiveState?.Layouts;
            if (layouts != null)
            {
                layout = vertical
                    ? layouts.FindLayout("tracker_vertical") ?? layouts.FindLayout("tracker_default")
                    : layouts.FindLayout("tracker_horizontal") ?? layouts.FindLayout("tracker_default");
            }
            // Fallback to ApplicationModel global slot if this window's
            // state isn't fully populated yet (early-startup race).
            if (layout == null)
            {
                layout = vertical
                    ? ApplicationModel.Instance.TrackerVerticalLayout
                    : ApplicationModel.Instance.TrackerHorizontalLayout;
            }
            if (TrackerLayout != null)
            {
                // Phase 7 polish: assigning a different Layout instance to
                // DataContext doesn't always trigger a full visual-subtree
                // rebuild in Avalonia (the existing bound DataTemplates
                // re-use their visual children with the new DataContext).
                // For per-window content swap to actually refresh the
                // displayed items + map markers, we explicitly clear
                // DataContext first to force the inner ContentControl /
                // ItemsControl to release their existing visual children.
                if (!ReferenceEquals(TrackerLayout.DataContext, layout))
                {
                    TrackerLayout.DataContext = null;
                    TrackerLayout.DataContext = layout;
                }
            }
        }

        // Phase 7 XAML migration: forward WindowContext.ActiveState
        // changes into a layout refresh on this window.
        private void OnWindowContextPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowContext.ActiveState))
                RefreshTrackerLayout();
        }

        // When any pack-load completes, if this window's active state was
        // the load target, refresh layout (its Layouts are now populated).
        private void OnAnyPackageLoadComplete(object sender, EmoTracker.Data.Sessions.PackageLoader.PackageLoadEventArgs e)
        {
            if (e?.Target != null && ReferenceEquals(e.Target, WindowContext?.ActiveState))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTrackerLayout);
            }
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            bool bOldAspect = e.PreviousSize.Height > e.PreviousSize.Width;
            bool bNewAspect = e.NewSize.Height > e.NewSize.Width;

            if (bOldAspect != bNewAspect)
                RefreshTrackerLayout();

            base.OnSizeChanged(e);
        }


        public UI.DeveloperConsole DeveloperConsole { get; private set; }

        public void ShowDeveloperConsole()
        {
            if (DeveloperConsole == null)
            {
                DeveloperConsole = new UI.DeveloperConsole();
                DeveloperConsole.Closing += (_, _) => DeveloperConsole = null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DeveloperConsole.Show());
            }
            else
            {
                DeveloperConsole.Activate();
            }
        }

        // =================================================================
        //  Per-window broadcast (visible BroadcastView + hidden NDI window)
        // =================================================================
        //
        // Each MainWindow owns its own pair of broadcast windows so that
        // the user can have multiple tracker windows broadcasting their
        // own active-tab content simultaneously. Both windows bind to
        // this window's WindowContext.BroadcastLayout, so switching tabs
        // in this window automatically updates this window's broadcast
        // feed without affecting any other window's feed.

        private UI.BroadcastView mBroadcastView;
        private Extensions.NDI.HiddenBroadcastWindow mHiddenBroadcast;

        /// <summary>
        /// The visible broadcast window for THIS app window, or null if
        /// the user hasn't opened it. Exposed so the screenshot tooling
        /// and similar can find a per-window broadcast view.
        /// </summary>
        public UI.BroadcastView BroadcastView => mBroadcastView;

        /// <summary>
        /// Lazy-creates and shows this window's <see cref="UI.BroadcastView"/>
        /// (or activates an existing one). The view binds to this window's
        /// <see cref="WindowContext.BroadcastLayout"/> and follows the
        /// active tab in this window.
        /// </summary>
        public void ShowBroadcastView()
        {
            if (mBroadcastView == null)
            {
                mBroadcastView = new UI.BroadcastView(WindowContext);
                mBroadcastView.Closing += (_, _) => mBroadcastView = null;
                // No owner so the broadcast view is an independent
                // top-level window — passing this as owner forces the OS
                // to keep the broadcast view above the host window at
                // all times, which the user almost never wants.
                mBroadcastView.Show();
            }
            else
            {
                mBroadcastView.Activate();
            }
        }

        void CloseBroadcastView()
        {
            if (mBroadcastView != null)
            {
                try { mBroadcastView.Close(); } catch { /* defensive */ }
                mBroadcastView = null;
            }
        }

        // -- Hidden broadcast (per-window background NDI) ----------------

        // Reconciliation triggered by:
        //   * App-wide setting flip (EnableBackgroundNdi)
        //   * Active tab switch in this window (BroadcastLayout fires)
        //   * Pack load completion that populates this window's layout
        //
        // The hidden window is created when the setting is on AND the
        // host's active tab has a broadcast layout with content; destroyed
        // otherwise.
        void ReconcileHiddenBroadcast()
        {
            bool settingEnabled = ApplicationSettings.Instance.EnableBackgroundNdi;
            bool hasContent = WindowContext?.BroadcastLayout?.Root != null;
            bool shouldExist = settingEnabled && hasContent;

            if (shouldExist && mHiddenBroadcast == null)
                CreateHiddenBroadcast();
            else if (!shouldExist && mHiddenBroadcast != null)
                DestroyHiddenBroadcast();
        }

        void CreateHiddenBroadcast()
        {
            try
            {
                mHiddenBroadcast = new Extensions.NDI.HiddenBroadcastWindow(WindowContext, this);
                mHiddenBroadcast.Show();
            }
            catch
            {
                mHiddenBroadcast = null;
            }
        }

        void DestroyHiddenBroadcast()
        {
            if (mHiddenBroadcast == null) return;
            try { mHiddenBroadcast.Close(); } catch { /* defensive */ }
            mHiddenBroadcast = null;
        }

        void OnWindowContextBroadcastChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowContext.BroadcastLayout))
                Avalonia.Threading.Dispatcher.UIThread.Post(ReconcileHiddenBroadcast);
        }

        void OnAppSettingsBroadcastChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationSettings.EnableBackgroundNdi))
                Avalonia.Threading.Dispatcher.UIThread.Post(ReconcileHiddenBroadcast);
        }
    }
}
