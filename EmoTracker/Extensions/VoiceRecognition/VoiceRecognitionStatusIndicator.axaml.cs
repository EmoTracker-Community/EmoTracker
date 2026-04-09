using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using EmoTracker.Data.Settings;
using System;
using System.ComponentModel;
using System.Linq;

namespace EmoTracker.Extensions.VoiceRecognition
{
    public partial class VoiceRecognitionStatusIndicator : UserControl
    {
        public VoiceRecognitionStatusIndicator()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private VoiceRecognitionExtension _extension;

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (_extension != null)
                _extension.PropertyChanged -= Extension_PropertyChanged;

            _extension = DataContext as VoiceRecognitionExtension;

            if (_extension != null)
                _extension.PropertyChanged += Extension_PropertyChanged;

            UpdateStatusIcon();
        }

        private void Extension_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VoiceRecognitionExtension.Active) ||
                e.PropertyName == nameof(VoiceRecognitionExtension.Listening))
                UpdateStatusIcon();
        }

        private void UpdateStatusIcon()
        {
            if (this.FindControl<TextBlock>("StatusIcon") is not TextBlock icon) return;

            if (_extension == null || !_extension.Active)
            {
                icon.Text = "\uf131"; // mic-slash
                icon.Foreground = SolidColorBrush.Parse("#717171");
                return;
            }

            icon.Text = "\uf130"; // mic
            icon.Foreground = _extension.Listening
                ? SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Active)
                : SolidColorBrush.Parse("WhiteSmoke");
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu || _extension == null) return;

            var deviceSubMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "DeviceSubMenu");
            if (deviceSubMenu == null) return;

            deviceSubMenu.Items.Clear();

            if (!_extension.AudioDevices.Any())
            {
                deviceSubMenu.Items.Add(new MenuItem { Header = "(No input devices found)", IsEnabled = false });
                return;
            }

            foreach (var device in _extension.AudioDevices)
            {
                var d = device;
                var item = new MenuItem
                {
                    Header = device.ToString(),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = device == _extension.SelectedDevice
                };
                item.Click += (_, _) => _extension.SelectedDevice = d;
                deviceSubMenu.Items.Add(item);
            }
        }
    }
}
