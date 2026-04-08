using Avalonia.Controls;
using Avalonia.Input;
using EmoTracker.Extensions;
using EmoTracker.Extensions.NDI;
using System.ComponentModel;

namespace EmoTracker.UI
{
    public partial class BroadcastView : Window
    {
        private bool _closed;

        public BroadcastView()
        {
            InitializeComponent();

            NDIHost.PropertyChanged += NDIHost_PropertyChanged;
            UpdateNDIExtensionStatus();
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
            NDIHost.Dispose();
            UpdateNDIExtensionStatus();
            base.OnClosing(e);
        }

        private void NDIHost_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NdiSendContainer.IsSendPaused))
                UpdateNDIExtensionStatus();
        }

        private void UpdateNDIExtensionStatus()
        {
            var extension = ExtensionManager.Instance.FindExtension<NDIExtension>();
            if (extension != null)
                extension.Active = !_closed && !NDIHost.IsSendPaused;
        }
    }
}
