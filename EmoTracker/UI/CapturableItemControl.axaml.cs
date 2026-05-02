#nullable enable annotations
using System;
using System.Windows.Input;
using Avalonia.Controls;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for CapturableItemControl.axaml
    /// </summary>
    public partial class CapturableItemControl : UserControl, TrackableItemControl.IClickHandler
    {
        public CapturableItemControl()
        {
            mProgressCmd = new LeftClickCommand(this);
            mClearCmd = new ClearItemSlotCommand(this);
            InitializeComponent();

            // Set ClickHandler in code-behind because Avalonia Popups create a separate
            // visual tree, so RelativeSource AncestorType bindings can't cross the boundary.
            TrackableItemControl.SetClickHandler(CaptureLayoutControl, this);
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

        public ICommand SelectItemCommand => mProgressCmd;

        public ICommand ClearItemCommand => mClearCmd;

        private class LeftClickCommand : ICommand
        {
            private readonly CapturableItemControl mHost;

            public LeftClickCommand(CapturableItemControl host)
            {
                mHost = host;
            }

            public event EventHandler? CanExecuteChanged;

            public void NotifyCanExecuteChanged()
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                Data.Locations.Section? section = mHost.DataContext as Data.Locations.Section;
                IItemCollection? itemCollection = mHost.DataContext as IItemCollection;

                if (section != null)
                {
                    using (section.OpenTransaction())
                    {
                        Data.ITrackableItem? item = parameter as Data.ITrackableItem;
                        section.CapturedItem = item;

                        if (item != null && ApplicationSettings.Instance.PinLocationsOnItemCapture)
                            section.Owner.Pinned = true;

                        mHost.PopupInstance.IsOpen = false;
                    }
                }
                else if (itemCollection != null)
                {
                    Data.ITrackableItem? item = parameter as Data.ITrackableItem;
                    if (item != null)
                    {
                        // The collection holder isn't necessarily a TransactableModelTypeBase;
                        // reach for its OwnerState's processor when it is a model, otherwise
                        // skip the explicit scope (AddItem itself routes through per-state
                        // processors when the underlying items are transactable).
                        var holder = itemCollection as Core.DataModel.ModelTypeBase;
                        var state = holder?.OwnerState as Data.Sessions.TrackerState;
                        if (state?.Transactions != null)
                        {
                            using (state.Transactions.OpenTransaction())
                            {
                                itemCollection.AddItem(item);
                            }
                        }
                        else
                        {
                            itemCollection.AddItem(item);
                        }
                        mHost.PopupInstance.IsOpen = false;
                    }
                }
                else
                {
                    mHost.PopupInstance.IsOpen = false;
                }
            }
        }

        private class ClearItemSlotCommand : ICommand
        {
            private readonly CapturableItemControl mHost;

            public ClearItemSlotCommand(CapturableItemControl host)
            {
                mHost = host;
            }

            public event EventHandler? CanExecuteChanged;

            public void NotifyCanExecuteChanged()
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            public bool CanExecute(object? parameter) =>
                mHost.DataContext as Data.Locations.Section != null;

            public void Execute(object? parameter)
            {
                Data.Locations.Section? section = mHost.DataContext as Data.Locations.Section;
                if (section != null)
                {
                    using (section.OpenTransaction())
                    {
                        section.CapturedItem = null;
                    }
                }
            }
        }

        private readonly LeftClickCommand mProgressCmd;
        private readonly ClearItemSlotCommand mClearCmd;

        #endregion
    }
}
