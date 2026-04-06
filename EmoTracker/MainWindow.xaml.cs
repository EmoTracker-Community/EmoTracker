using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Layout;
using EmoTracker.UI;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EmoTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            this.SourceInitialized += new EventHandler(win_SourceInitialized);

            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                //  Set application context
                ApplicationModel.Instance.Initialize();
                DataContext = ApplicationModel.Instance;
                ApplicationModel.Instance.PropertyChanged += Instance_PropertyChanged;
            }

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                if (ApplicationSettings.Instance.InitialWidth >= 0.0)
                    Width = ApplicationSettings.Instance.InitialWidth;

                if (ApplicationSettings.Instance.InitialHeight >= 0.0)
                    Height = ApplicationSettings.Instance.InitialHeight;
            }

            this.Loaded += MainWindow_Loaded;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            this.MouseWheel += MainWindow_MouseWheel;
            this.ContentRendered += MainWindow_ContentRendered;
            //this.SizeChanged += MainWindow_SizeChanged;
        }

        private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                int steps = e.Delta / 30;
                ApplicationModel.Instance.IncrementMainLayoutScale(steps);
            }
        }

        bool bInManualSet = false;
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!bInManualSet)
            {
                try
                {
                    bInManualSet = true;

                    Width = Math.Max(e.NewSize.Width, MinWidth);
                    Height = Math.Max(e.NewSize.Height, MinHeight);
                }
                finally
                {
                    bInManualSet = false;
                }
            }
        }

        public event KeyEventHandler OnGlobalPreviewKeyDown;
        public event KeyEventHandler OnGlobalPreviewKeyUp;

        void win_SourceInitialized(object sender, System.EventArgs e)
        {
            System.IntPtr handle = (new System.Windows.Interop.WindowInteropHelper(this)).Handle;
            System.Windows.Interop.HwndSource.FromHwnd(handle).AddHook(new System.Windows.Interop.HwndSourceHook(WindowProc));
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:/* WM_GETMINMAXINFO */
                    WmGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;
            }

            return (IntPtr)0;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {

            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            WpfScreenHelper.Screen screen = WpfScreenHelper.Screen.PrimaryScreen;
            {
                WpfScreenHelper.Screen appScreen = WpfScreenHelper.Screen.FromHandle(hwnd);
                if (appScreen != null)
                    screen = appScreen;
            }

            Rect monitorArea = screen.Bounds;
            Rect workArea = screen.WorkingArea;

            mmi.ptMinTrackSize.x = (int)Application.Current.MainWindow.MinWidth;
            mmi.ptMinTrackSize.y = (int)Application.Current.MainWindow.MinHeight;

            // Adjust the maximized size and position to fit the work area of the correct monitor
            mmi.ptMaxPosition.x = Math.Abs((int)workArea.Left - (int)monitorArea.Left);
            mmi.ptMaxPosition.y = Math.Abs((int)workArea.Top - (int)monitorArea.Top);
            mmi.ptMaxSize.x = Math.Abs((int)workArea.Right - (int)workArea.Left);
            mmi.ptMaxSize.y = Math.Abs((int)workArea.Bottom - (int)workArea.Top);
            
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                TrackerLayout.Focus();
            }));
        }

        public BroadcastView BroadcastView
        {
            get { return mBroadcastView; }
        }

        public DeveloperConsole DeveloperConsole
        {
            get { return mDeveloperConsole; }
        }

        public double FixedContentWidth
        {
            get
            {
                if (ActiveLayout != null && ActiveLayout.Root != null && ActiveLayout.Root.OverrideWidth)
                    return ActiveLayout.Root.Width * ApplicationModel.Instance.MainLayoutScaleFactor;

                return 0.0;
            }
        }
        public bool UseFixedContentWidth
        {
            get
            {
                return FixedContentWidth > 0.0 && !Tracker.Instance.AllowResize;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Button systemButton = Template.FindName("SystemButton", this) as Button;
            if (systemButton != null)
                systemButton.Click += SystemButton_Click;

            Button settingsButton = Template.FindName("SettingsButton", this) as Button;
            if (settingsButton != null)
                settingsButton.Click += SettingsButton_Click;

            Button minimizeButton = Template.FindName("MinimizeButton", this) as Button;
            if (minimizeButton != null)
                minimizeButton.Click += MinimizeButton_Click; ;

            Button maximizeButton = Template.FindName("MaximizeButton", this) as Button;
            if (maximizeButton != null)
                maximizeButton.Click += MaximizeButton_Click;

            Button closeButton = Template.FindName("CloseButton", this) as Button;
            if (closeButton != null)
                closeButton.Click += CloseButton_Click;
        }

        Point ConvertToScreenCoordinates(Point p)
        {
            var t = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            return t.Transform(PointToScreen(p));
        }

        private void SystemButton_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement src = sender as FrameworkElement;
            SystemCommands.ShowSystemMenu(this, ConvertToScreenCoordinates(new Point(0, 30)));
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.Focus(Application.Current.MainWindow);

            FrameworkElement src = sender as FrameworkElement;
            if (src != null)
            {
                src.ContextMenu.PlacementTarget = src;
                src.ContextMenu.IsOpen = true;
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (this.WindowState != WindowState.Minimized)
                    Keyboard.Focus(Application.Current.MainWindow);
            }));

            base.OnStateChanged(e);
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tracker.Instance.AllowResize)
            {
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
                else
                    WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Instance_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            NotifyPropertyChanged("ActiveLayout");
            NotifyPropertyChanged("FixedContentWidth");
            NotifyPropertyChanged("UseFixedContentWidth");
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            bool bOldAspect = sizeInfo.PreviousSize.Height > sizeInfo.PreviousSize.Width;
            bool bNewAspect = sizeInfo.NewSize.Height > sizeInfo.NewSize.Width;

            if (bOldAspect != bNewAspect)
            {
                NotifyPropertyChanged("UseVerticalOrientation");
                NotifyPropertyChanged("ActiveLayout");
            }

            base.OnRenderSizeChanged(sizeInfo);
        }

        public bool UseVerticalOrientation
        {
            get
            {
                return ActualHeight > ActualWidth;
            }
        }

        public Data.Layout.Layout ActiveLayout
        {
            get
            {
                if (UseVerticalOrientation)
                    return ApplicationModel.Instance.TrackerVerticalLayout;

                return ApplicationModel.Instance.TrackerHorizontalLayout;
            }
        }

        private BroadcastView mBroadcastView;
        private DeveloperConsole mDeveloperConsole;

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
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
                ShowBroadcastView();
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
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
                {
                    ApplicationModel.Instance.SaveAsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ApplicationModel.Instance.SaveCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.O)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ApplicationModel.Instance.OpenCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.Z)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    IUndoableTransactionProcessor undo = TransactionProcessor.Current as IUndoableTransactionProcessor;
                    if (undo != null)
                        undo.Undo();

                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.D0)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ApplicationModel.Instance.ResetLayoutScale();
                    e.Handled = true;
                    return;
                }
            }

            if (!e.Handled && OnGlobalPreviewKeyDown != null)
                OnGlobalPreviewKeyDown(this, e);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (OnGlobalPreviewKeyUp != null)
                OnGlobalPreviewKeyUp(this, e);

            base.OnPreviewKeyUp(e);
        }

        public void ShowDeveloperConsole()
        {
            if (mDeveloperConsole == null)
            {
                mDeveloperConsole = new DeveloperConsole();
                mDeveloperConsole.Closing += DeveloperConsole_Closing;

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    mDeveloperConsole.Show();
                }));
            }
            else
            {
                mDeveloperConsole.Activate();
            }
        }

        public void ShowBroadcastView()
        {
            if (mBroadcastView == null)
            {
                mBroadcastView = new BroadcastView();
                mBroadcastView.Closing += BroadcastView_Closing;

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    mBroadcastView.Show();
                }));
            }
            else
            {
                mBroadcastView.Activate();
            }
        }

        bool mbHasIssuedSubWindowDispose = false;

        protected override void OnClosing(CancelEventArgs e)
        {
            if (ApplicationSettings.Instance.PromptOnRefreshClose)
            {
                MessageBoxResult result = MessageBox.Show(this, "Closing the application will cause you to lose all unsaved progress. Are you sure you want to continue?", "Warning!", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (!mbHasIssuedSubWindowDispose)
            {
                mbHasIssuedSubWindowDispose = true;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (mBroadcastView != null)
                    {
                        mBroadcastView.Close();
                        mBroadcastView = null;
                    }

                    if (mDeveloperConsole != null)
                    {
                        mDeveloperConsole.Close();
                        mDeveloperConsole = null;
                    }
                }));
            }

            base.OnClosing(e);
        }

        private void BroadcastView_Closing(object sender, CancelEventArgs e)
        {
            mBroadcastView = null;
        }

        private void DeveloperConsole_Closing(object sender, CancelEventArgs e)
        {
            mDeveloperConsole = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        /// <summary>
        /// x coordinate of point.
        /// </summary>
        public int x;
        /// <summary>
        /// y coordinate of point.
        /// </summary>
        public int y;

        /// <summary>
        /// Construct a point of coordinates (x,y).
        /// </summary>
        public POINT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    };
}
