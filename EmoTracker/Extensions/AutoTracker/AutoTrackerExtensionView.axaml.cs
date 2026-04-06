using Avalonia.Controls;
using Avalonia.Media;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Settings;
using System;
using System.ComponentModel;

namespace EmoTracker.Extensions.AutoTracker
{
    public partial class AutoTrackerExtensionView : UserControl
    {
        public AutoTrackerExtensionView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private AutoTrackerExtension _extension;

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (_extension != null)
                _extension.PropertyChanged -= Extension_PropertyChanged;

            _extension = DataContext as AutoTrackerExtension;

            if (_extension != null)
                _extension.PropertyChanged += Extension_PropertyChanged;

            UpdateStatusColor();
            AttachContextMenuHandler();
        }

        private void AttachContextMenuHandler()
        {
            var grid = this.Content as Grid;
            if (grid?.ContextMenu != null)
            {
                grid.ContextMenu.Opening -= ContextMenu_Opening;
                grid.ContextMenu.Opening += ContextMenu_Opening;
            }
        }

        private void ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            RebuildContextMenu();
        }

        private void Extension_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AutoTrackerExtension.Connected):
                case nameof(AutoTrackerExtension.Error):
                case nameof(AutoTrackerExtension.ActiveProvider):
                    UpdateStatusColor();
                    break;
            }
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

            if (_extension.Error)
            {
                icon.Foreground = SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Error);
            }
            else if (!_extension.Connected && _extension.ActiveProvider != null)
            {
                icon.Foreground = SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Warning);
            }
            else if (_extension.ActiveProvider == null)
            {
                icon.Foreground = Brushes.WhiteSmoke;
            }
            else
            {
                icon.Foreground = SolidColorBrush.Parse("#35e0b5");
            }
        }

        private void RebuildContextMenu()
        {
            if (_extension == null)
                return;

            var grid = this.Content as Grid;
            if (grid?.ContextMenu == null)
                return;

            foreach (var item in grid.ContextMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (menuItem.Header as string == "Provider")
                    {
                        menuItem.Items.Clear();
                        foreach (var provider in _extension.ApplicableProviders)
                        {
                            var isSelected = provider == _extension.SelectedProvider;
                            var providerItem = new MenuItem
                            {
                                Header = provider.DisplayName,
                                Tag = provider,
                                Icon = isSelected ? new TextBlock { Text = "\u2713" } : null
                            };
                            providerItem.Click += (s, e) =>
                            {
                                if (_extension.SetProviderCommand.CanExecute(provider))
                                    _extension.SetProviderCommand.Execute(provider);
                            };
                            menuItem.Items.Add(providerItem);
                        }
                    }
                    else if (menuItem.Header as string == "Device")
                    {
                        menuItem.Items.Clear();
                        if (_extension.SelectedProvider != null)
                        {
                            foreach (var device in _extension.SelectedProvider.AvailableDevices)
                            {
                                var isDefault = device == _extension.SelectedProvider.DefaultDevice;
                                var deviceItem = new MenuItem
                                {
                                    Header = device.DisplayName,
                                    Tag = device,
                                    Icon = isDefault ? new TextBlock { Text = "\u2713" } : null
                                };
                                deviceItem.Click += (s, e) =>
                                {
                                    if (_extension.SetDeviceCommand.CanExecute(device))
                                        _extension.SetDeviceCommand.Execute(device);
                                };
                                menuItem.Items.Add(deviceItem);
                            }
                        }
                    }
                }
            }
        }
    }
}
