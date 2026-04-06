#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using System;
using System.Windows.Input;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for TrackableItemControl.axaml
    /// </summary>
    public partial class TrackableItemControl : UserControl
    {
        public interface IClickHandler
        {
            bool OnLeftClick(ITrackableItem item);
            bool OnRightClick(ITrackableItem item);
        }

        public TrackableItemControl()
        {
            mProgressCmd = new LeftClickCommand(this);
            mRegressCmd = new RightClickCommand(this);

            InitializeComponent();
        }

        // ---- IconWidth ----
        public static readonly StyledProperty<double> IconWidthProperty =
            AvaloniaProperty.Register<TrackableItemControl, double>(nameof(IconWidth), defaultValue: 32.0);

        public double IconWidth
        {
            get => GetValue(IconWidthProperty);
            set => SetValue(IconWidthProperty, value);
        }

        // ---- IconHeight ----
        public static readonly StyledProperty<double> IconHeightProperty =
            AvaloniaProperty.Register<TrackableItemControl, double>(nameof(IconHeight), defaultValue: 32.0);

        public double IconHeight
        {
            get => GetValue(IconHeightProperty);
            set => SetValue(IconHeightProperty, value);
        }

        // ---- DisplayPotentialIcon (attached, inherits) ----
        public static readonly AttachedProperty<bool> DisplayPotentialIconProperty =
            AvaloniaProperty.RegisterAttached<TrackableItemControl, AvaloniaObject, bool>(
                "DisplayPotentialIcon", defaultValue: false, inherits: true);

        public static bool GetDisplayPotentialIcon(AvaloniaObject obj) =>
            obj.GetValue(DisplayPotentialIconProperty);

        public static void SetDisplayPotentialIcon(AvaloniaObject obj, bool value) =>
            obj.SetValue(DisplayPotentialIconProperty, value);

        // ---- DisplayCapturableOnly (attached, inherits) ----
        public static readonly AttachedProperty<bool> DisplayCapturableOnlyProperty =
            AvaloniaProperty.RegisterAttached<TrackableItemControl, AvaloniaObject, bool>(
                "DisplayCapturableOnly", defaultValue: false, inherits: true);

        public static bool GetDisplayCapturableOnly(AvaloniaObject obj) =>
            obj.GetValue(DisplayCapturableOnlyProperty);

        public static void SetDisplayCapturableOnly(AvaloniaObject obj, bool value) =>
            obj.SetValue(DisplayCapturableOnlyProperty, value);

        // ---- BadgeFontSize (attached, inherits) ----
        public static readonly AttachedProperty<double> BadgeFontSizeProperty =
            AvaloniaProperty.RegisterAttached<TrackableItemControl, AvaloniaObject, double>(
                "BadgeFontSize", defaultValue: 12.0, inherits: true);

        public static double GetBadgeFontSize(AvaloniaObject obj) =>
            obj.GetValue(BadgeFontSizeProperty);

        public static void SetBadgeFontSize(AvaloniaObject obj, double value) =>
            obj.SetValue(BadgeFontSizeProperty, value);

        // ---- ClickHandler (attached, inherits) ----
        public static readonly AttachedProperty<IClickHandler?> ClickHandlerProperty =
            AvaloniaProperty.RegisterAttached<TrackableItemControl, AvaloniaObject, IClickHandler?>(
                "ClickHandler", defaultValue: null, inherits: true);

        public static IClickHandler? GetClickHandler(AvaloniaObject obj) =>
            obj.GetValue(ClickHandlerProperty);

        public static void SetClickHandler(AvaloniaObject obj, IClickHandler? value) =>
            obj.SetValue(ClickHandlerProperty, value);

        #region --- Commands ---

        private class LeftClickCommand : ICommand
        {
            private readonly AvaloniaObject mOwner;

            public LeftClickCommand(AvaloniaObject owner)
            {
                mOwner = owner;
            }

            public event EventHandler? CanExecuteChanged;

            public void NotifyCanExecutedChanged()
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                try
                {
                    LocationDatabase.Instance.SuspendRefresh = true;

                    ITrackableItem? item = parameter as ITrackableItem;
                    if (item != null)
                    {
                        IClickHandler? interrupt = GetClickHandler(mOwner);
                        if (interrupt != null && interrupt.OnLeftClick(item))
                            return;

                        if (!item.IgnoreUserInput)
                        {
                            using (TransactionProcessor.Current.OpenTransaction())
                            {
                                item.OnLeftClick();
                            }
                        }
                    }
                }
                finally
                {
                    LocationDatabase.Instance.SuspendRefresh = false;
                }
            }
        }

        private class RightClickCommand : ICommand
        {
            private readonly AvaloniaObject mOwner;

            public RightClickCommand(AvaloniaObject owner)
            {
                mOwner = owner;
            }

            public event EventHandler? CanExecuteChanged;

            public void NotifyCanExecutedChanged()
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                try
                {
                    LocationDatabase.Instance.SuspendRefresh = true;

                    ITrackableItem? item = parameter as ITrackableItem;
                    if (item != null)
                    {
                        IClickHandler? interrupt = GetClickHandler(mOwner);
                        if (interrupt != null && interrupt.OnRightClick(item))
                            return;

                        if (!item.IgnoreUserInput)
                        {
                            using (TransactionProcessor.Current.OpenTransaction())
                            {
                                item.OnRightClick();
                            }
                        }
                    }
                }
                finally
                {
                    LocationDatabase.Instance.SuspendRefresh = false;
                }
            }
        }

        public ICommand OnLeftClickCommand => mProgressCmd;
        public ICommand OnRightClickCommand => mRegressCmd;

        private readonly LeftClickCommand mProgressCmd;
        private readonly RightClickCommand mRegressCmd;

        #endregion

        private void Grid_PointerReleased(object sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right)
            {
                mRegressCmd.Execute(DataContext);
                e.Handled = true;
            }
        }
    }
}
