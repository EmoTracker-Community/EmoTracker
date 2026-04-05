using Avalonia.Controls;
using Avalonia.Media;
using EmoTracker.Data.Settings;
using System;
using System.ComponentModel;

namespace EmoTracker.Extensions.Twitch
{
    public partial class TwitchStatusIndicator : UserControl
    {
        public TwitchStatusIndicator()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private TwitchExtension _extension;

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (_extension != null)
                _extension.PropertyChanged -= Extension_PropertyChanged;

            _extension = DataContext as TwitchExtension;

            if (_extension != null)
                _extension.PropertyChanged += Extension_PropertyChanged;

            UpdateStatusColor();
        }

        private void Extension_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TwitchExtension.ConnectionState))
                UpdateStatusColor();
        }

        private void UpdateStatusColor()
        {
            if (this.FindControl<TextBlock>("StatusIcon") is not TextBlock icon)
                return;

            if (_extension == null)
            {
                icon.Foreground = Brushes.WhiteSmoke;
                return;
            }

            switch (_extension.ConnectionState)
            {
                case ConnectionState.Connected:
                    icon.Foreground = SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Success);
                    break;
                case ConnectionState.Connecting:
                    icon.Foreground = SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Warning);
                    break;
                default:
                    icon.Foreground = Brushes.WhiteSmoke;
                    break;
            }
        }
    }
}
