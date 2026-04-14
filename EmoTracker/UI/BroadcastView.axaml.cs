using Avalonia.Controls;
using Avalonia.Input;
using EmoTracker.Data;
using EmoTracker.Extensions;
using EmoTracker.Extensions.NDI;
using System.ComponentModel;
using EmoTracker.Data.Session;

namespace EmoTracker.UI
{
    public partial class BroadcastView : Window
    {
        private bool _closed;

        public BroadcastView()
        {
            InitializeComponent();

            // When background NDI is enabled, the hidden HiddenBroadcastWindow
            // handles NDI broadcasting on all platforms, so the visible container
            // must stay dormant to avoid advertising a duplicate source.  With the
            // setting off, the visible container owns the NDI stream (legacy
            // "NDI only while broadcast view is open" behaviour).
            //
            // NdiEnabled is read once in NdiSendContainer.OnAttachedToVisualTree,
            // so we set it BEFORE the window attaches to the visual tree.
            NDIHost.NdiEnabled = !TrackerSession.Current.Global.EnableBackgroundNdi;

            if (NDIHost.NdiEnabled)
            {
                NDIHost.PropertyChanged += NDIHost_PropertyChanged;
                UpdateNDIExtensionStatus();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                ApplicationModel.Instance.RefreshCommand.Execute(null);
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _closed = true;
            if (NDIHost.NdiEnabled)
            {
                NDIHost.Dispose();
                UpdateNDIExtensionStatus();
            }
            base.OnClosing(e);
        }

        private void NDIHost_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NdiSendContainer.IsSendPaused))
                UpdateNDIExtensionStatus();
        }

        private void UpdateNDIExtensionStatus()
        {
            // When the hidden window is managing NDI, the visible container is
            // dormant (IsSendPaused stays at its default), and the hidden window
            // drives extension.Active.  Skip writes from this path in that case
            // so the two don't fight over the Active state.
            if (!NDIHost.NdiEnabled)
                return;

            var extension = ExtensionManager.Instance.FindExtension<NDIExtension>();
            if (extension != null)
                extension.Active = !_closed && !NDIHost.IsSendPaused;
        }
    }
}
