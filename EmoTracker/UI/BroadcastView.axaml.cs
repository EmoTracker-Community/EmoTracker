using Avalonia.Controls;
using Avalonia.Input;
using EmoTracker.Data;
using EmoTracker.Extensions;
using EmoTracker.Extensions.NDI;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EmoTracker.UI
{
    public partial class BroadcastView : Window
    {
        private bool _closed;

        public BroadcastView()
        {
            InitializeComponent();

            // On Windows the hidden HiddenBroadcastWindow handles NDI when
            // EnableBackgroundNdi is on, so the visible container must stay
            // dormant to avoid advertising a duplicate source.  On other
            // platforms (or with the setting off) the visible container owns
            // the NDI stream as before.
            //
            // NdiEnabled is read once in NdiSendContainer.OnAttachedToVisualTree,
            // so we set it BEFORE the window attaches to the visual tree.
            NDIHost.NdiEnabled = !BackgroundNdiIsActive;

            if (NDIHost.NdiEnabled)
            {
                NDIHost.PropertyChanged += NDIHost_PropertyChanged;
                UpdateNDIExtensionStatus();
            }
        }

        private static bool BackgroundNdiIsActive =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            ApplicationSettings.Instance.EnableBackgroundNdi;

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
