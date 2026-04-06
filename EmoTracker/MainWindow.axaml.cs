using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Layout;
using System;
using System.ComponentModel;

namespace EmoTracker
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            ApplicationModel.Instance.Initialize();
            DataContext = ApplicationModel.Instance;
            ApplicationModel.Instance.PropertyChanged += Instance_PropertyChanged;
            Tracker.Instance.PropertyChanged += Tracker_PropertyChanged;

            if (ApplicationSettings.Instance.InitialWidth >= 0.0)
                Width = ApplicationSettings.Instance.InitialWidth;
            if (ApplicationSettings.Instance.InitialHeight >= 0.0)
                Height = ApplicationSettings.Instance.InitialHeight;

            this.Loaded += MainWindow_Loaded;
            // Use Tunnel routing to match WPF's PreviewKeyDown — the window
            // handles shortcuts before any child control can consume the key.
            this.AddHandler(KeyDownEvent, MainWindow_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.PointerWheelChanged += MainWindow_PointerWheelChanged;

            // Set initial layout and resize mode
            RefreshTrackerLayout();
            UpdateResizeMode();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.FindControl<Button>("SettingsButton") is Button settingsBtn)
                settingsBtn.Click += SettingsButton_Click;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CheckForUpdates();
                TrackerLayout?.Focus();
            });
        }

        private void CheckForUpdates()
        {
            var updateWindow = new UI.AppUpdateWindow(true);
            updateWindow.ShowDialog(this);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.Open(btn);
            }
        }

        private void MainWindow_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control | KeyModifiers.Shift))
                {
                    ApplicationModel.Instance.SaveAsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    ApplicationModel.Instance.SaveCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ApplicationModel.Instance.OpenCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (TransactionProcessor.Current is IUndoableTransactionProcessor undo)
                    undo.Undo();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.D0 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
            if (ApplicationSettings.Instance.PromptOnRefreshClose)
            {
                // For Phase 6 just close — async dialog in Phase 7
            }

            if (DeveloperConsole != null)
            {
                DeveloperConsole.Close();
                DeveloperConsole = null;
            }

            base.OnClosing(e);
        }

        private void Tracker_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Tracker.AllowResize))
                UpdateResizeMode();
        }

        private void UpdateResizeMode()
        {
            if (!Tracker.Instance.AllowResize)
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
            var layout = vertical
                ? ApplicationModel.Instance.TrackerVerticalLayout
                : ApplicationModel.Instance.TrackerHorizontalLayout;
            if (TrackerLayout != null)
                TrackerLayout.DataContext = layout;
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
    }
}
