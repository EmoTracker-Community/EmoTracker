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
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for CapturableItemControl.xaml
    /// </summary>
    public partial class CapturableItemControl : UserControl, TrackableItemControl.IClickHandler
    {
        public CapturableItemControl()
        {
            mProgressCmd = new LeftClickCommand(this);
            mClearCmd = new ClearItemSlotCommand(this);
            InitializeComponent();            
        }

        #region --- TrackableItemControl.IClickHandler ---

        bool TrackableItemControl.IClickHandler.OnLeftClick(ITrackableItem item)
        {
            SelectItemCommand.Execute(item);
            return true;
        }

        bool TrackableItemControl.IClickHandler.OnRightClick(ITrackableItem item)
        {
            return true;
        }

        #endregion

        #region --- Commands ---

        public ICommand SelectItemCommand
        {
            get { return mProgressCmd; }
        }

        public ICommand ClearItemCommand
        {
            get { return mClearCmd; }
        }

        private class LeftClickCommand : ICommand
        {
            CapturableItemControl mHost;

            public LeftClickCommand(CapturableItemControl host)
            {
                mHost = host;
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
                Data.Locations.Section section = mHost.DataContext as Data.Locations.Section;
                IItemCollection itemCollection = mHost.DataContext as IItemCollection;

                if (section != null)
                {
                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        Data.ITrackableItem item = parameter as Data.ITrackableItem;
                        section.CapturedItem = item;

                        if (item != null && ApplicationSettings.Instance.PinLocationsOnItemCapture)
                            section.Owner.Pinned = true;

                        mHost.PopupInstance.IsOpen = false;
                    }
                }
                else if (itemCollection != null)
                {
                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        Data.ITrackableItem item = parameter as Data.ITrackableItem;
                        if (item != null)
                        {
                            itemCollection.AddItem(item);
                            mHost.PopupInstance.IsOpen = false;
                        }
                    }
                }
                else
                {
                    mHost.PopupInstance.IsOpen = false;
                }
            }
        }

        class ClearItemSlotCommand : ICommand
        {
            CapturableItemControl mHost;

            public ClearItemSlotCommand(CapturableItemControl host)
            {
                mHost = host;
            }

            public event EventHandler CanExecuteChanged;

            public void NotifyCanExecutedChanged()
            {
                if (CanExecuteChanged != null)
                    CanExecuteChanged(this, EventArgs.Empty);
            }

            public bool CanExecute(object parameter)
            {
                return mHost.DataContext as Section != null;
            }

            public void Execute(object parameter)
            {
                using (TransactionProcessor.Current.OpenTransaction())
                {
                    Data.Locations.Section section = mHost.DataContext as Data.Locations.Section;
                    if (section != null)
                    {
                        section.CapturedItem = null;
                    }
                }
            }
        }

        LeftClickCommand mProgressCmd;
        ClearItemSlotCommand mClearCmd;

        #endregion
    }
}
