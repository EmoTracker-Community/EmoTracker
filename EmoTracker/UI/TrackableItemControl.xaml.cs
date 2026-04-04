using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for TrackableItemControl.xaml
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

        public double IconWidth
        {
            get { return (double)GetValue(IconWidthProperty); }
            set { SetValue(IconWidthProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IconWidth.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IconWidthProperty =
            DependencyProperty.Register("IconWidth", typeof(double), typeof(TrackableItemControl), new PropertyMetadata(32.0));

        public double IconHeight
        {
            get { return (double)GetValue(IconHeightProperty); }
            set { SetValue(IconHeightProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IconHeight.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IconHeightProperty =
            DependencyProperty.Register("IconHeight", typeof(double), typeof(TrackableItemControl), new PropertyMetadata(32.0));

        public static bool GetDisplayPotentialIcon(DependencyObject obj)
        {
            return (bool)obj.GetValue(DisplayPotentialIconProperty);
        }

        public static void SetDisplayPotentialIcon(DependencyObject obj, bool value)
        {
            obj.SetValue(DisplayPotentialIconProperty, value);
        }

        // Using a DependencyProperty as the backing store for DisplayPotentialIcon.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DisplayPotentialIconProperty =
            DependencyProperty.RegisterAttached("DisplayPotentialIcon", typeof(bool), typeof(TrackableItemControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));



        public static bool GetDisplayCapturableOnly(DependencyObject obj)
        {
            return (bool)obj.GetValue(DisplayCapturableOnlyProperty);
        }

        public static void SetDisplayCapturableOnly(DependencyObject obj, bool value)
        {
            obj.SetValue(DisplayCapturableOnlyProperty, value);
        }

        // Using a DependencyProperty as the backing store for DisplayCapturableOnly.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DisplayCapturableOnlyProperty =
            DependencyProperty.RegisterAttached("DisplayCapturableOnly", typeof(bool), typeof(TrackableItemControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));




        public double BadgeFontSize
        {
            get { return (double)GetValue(BadgeFontSizeProperty); }
            set { SetValue(BadgeFontSizeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for BadgeFontSize.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty BadgeFontSizeProperty =
            DependencyProperty.RegisterAttached("BadgeFontSize", typeof(double), typeof(TrackableItemControl), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.Inherits));


        public static IClickHandler GetClickHandler(DependencyObject obj)
        {
            return (IClickHandler)obj.GetValue(ClickHandlerProperty);
        }

        public static void SetClickHandler(DependencyObject obj, IClickHandler value)
        {
            obj.SetValue(ClickHandlerProperty, value);
        }

        // Using a DependencyProperty as the backing store for ClickHandler.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ClickHandlerProperty =
            DependencyProperty.RegisterAttached("ClickHandler", typeof(IClickHandler), typeof(TrackableItemControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        #region --- Commands ---

        private class LeftClickCommand : ICommand
        {
            UIElement mOwner;

            public LeftClickCommand(UIElement owner)
            {
                mOwner = owner;
            }

            public event EventHandler CanExecuteChanged;

            public void NotifyCanExecutedChanged()
            {
                if (CanExecuteChanged != null)
                    CanExecuteChanged(this, EventArgs.Empty);
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public void Execute(object parameter)
            {
                try
                {
                    LocationDatabase.Instance.SuspendRefresh = true;

                    ITrackableItem item = parameter as ITrackableItem;
                    if (item != null)
                    {
                        IClickHandler interrupt = GetClickHandler(mOwner);
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
            UIElement mOwner;

            public RightClickCommand(UIElement owner)
            {
                mOwner = owner;
            }

            public event EventHandler CanExecuteChanged;

            public void NotifyCanExecutedChanged()
            {
                if (CanExecuteChanged != null)
                    CanExecuteChanged(this, EventArgs.Empty);
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public void Execute(object parameter)
            {
                try
                {
                    LocationDatabase.Instance.SuspendRefresh = true;

                    ITrackableItem item = parameter as ITrackableItem;
                    if (item != null)
                    {
                        IClickHandler interrupt = GetClickHandler(mOwner);
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

        public ICommand OnLeftClickCommand
        {
            get { return mProgressCmd; }
        }

        public ICommand OnRightClickCommand
        {
            get { return mRegressCmd; }
        }

        LeftClickCommand mProgressCmd;
        RightClickCommand mRegressCmd;

        #endregion

    }
}
