using EmoTracker.Data.AutoTracking;
using System;
using System.Windows;
using System.Windows.Controls;

namespace EmoTracker.Extensions.AutoTracker
{
    public partial class AutoTrackerExtensionView : UserControl
    {
        public AutoTrackerExtensionView()
        {
            InitializeComponent();

            var grid = FindName("RootGrid") as Grid;
            if (grid?.ContextMenu != null)
            {
                grid.ContextMenu.Opened += ContextMenu_Opened;
            }
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu)
                return;

            var extension = DataContext as AutoTrackerExtension;
            if (extension == null)
                return;

            menu.Items.Clear();

            // Start / Stop
            menu.Items.Add(new MenuItem { Header = "Start", Command = extension.StartCommand });
            menu.Items.Add(new MenuItem { Header = "Stop", Command = extension.StopCommand });
            menu.Items.Add(new Separator());

            // Provider submenu
            var providerMenu = new MenuItem { Header = "Provider" };
            foreach (var provider in extension.ApplicableProviders)
            {
                var isSelected = provider == extension.SelectedProvider;
                var providerItem = new MenuItem
                {
                    Header = provider.DisplayName,
                    IsCheckable = false,
                    Icon = isSelected ? new TextBlock { Text = "\u2713" } : null
                };
                var capturedProvider = provider;
                providerItem.Click += (s, args) =>
                {
                    if (extension.SetProviderCommand.CanExecute(capturedProvider))
                        extension.SetProviderCommand.Execute(capturedProvider);
                };
                providerMenu.Items.Add(providerItem);
            }
            menu.Items.Add(providerMenu);

            // Device submenu (with connection status)
            var deviceMenu = new MenuItem { Header = "Device" };
            if (extension.SelectedProvider != null)
            {
                foreach (var device in extension.SelectedProvider.AvailableDevices)
                {
                    var isDefault = device == extension.SelectedProvider.DefaultDevice;
                    string status = device.IsConnected ? " [Connected]" : "";
                    var deviceItem = new MenuItem
                    {
                        Header = device.DisplayName + status,
                        Icon = isDefault ? new TextBlock { Text = "\u2713" } : null
                    };
                    var capturedDevice = device;
                    deviceItem.Click += (s, args) =>
                    {
                        if (extension.SetDeviceCommand.CanExecute(capturedDevice))
                            extension.SetDeviceCommand.Execute(capturedDevice);
                    };
                    deviceMenu.Items.Add(deviceItem);
                }
            }
            menu.Items.Add(deviceMenu);

            // Provider options
            if (extension.SelectedProvider != null && extension.SelectedProvider.Options.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var option in extension.SelectedProvider.Options)
                {
                    AddOptionMenuItem(menu, option);
                }
            }

            // Device options (from default device)
            if (extension.SelectedProvider?.DefaultDevice != null && extension.SelectedProvider.DefaultDevice.Options.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var option in extension.SelectedProvider.DefaultDevice.Options)
                {
                    AddOptionMenuItem(menu, option);
                }
            }

            // Device operations (from default device)
            if (extension.SelectedProvider?.DefaultDevice != null && extension.SelectedProvider.DefaultDevice.Operations.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var operation in extension.SelectedProvider.DefaultDevice.Operations)
                {
                    var opItem = new MenuItem
                    {
                        Header = operation.DisplayName,
                        IsEnabled = operation.CanExecute
                    };
                    var capturedOp = operation;
                    opItem.Click += async (s, args) =>
                    {
                        if (capturedOp.CanExecute)
                            await capturedOp.ExecuteAsync();
                    };
                    menu.Items.Add(opItem);
                }
            }
        }

        private void AddOptionMenuItem(ContextMenu menu, IProviderOption option)
        {
            if (option.Kind == ProviderOptionKind.Dropdown)
            {
                var optionMenu = new MenuItem { Header = option.DisplayName };
                foreach (var val in option.AvailableValues)
                {
                    var isActive = Equals(option.Value, val);
                    var valItem = new MenuItem
                    {
                        Header = val.ToString(),
                        Icon = isActive ? new TextBlock { Text = "\u2713" } : null
                    };
                    var capturedVal = val;
                    var capturedOption = option;
                    valItem.Click += (s, e) => { capturedOption.Value = capturedVal; };
                    optionMenu.Items.Add(valItem);
                }
                menu.Items.Add(optionMenu);
            }
            else if (option.Kind == ProviderOptionKind.Toggle)
            {
                var toggleItem = new MenuItem
                {
                    Header = option.DisplayName,
                    Icon = Equals(option.Value, true) ? new TextBlock { Text = "\u2713" } : null
                };
                var capturedOption = option;
                toggleItem.Click += (s, e) => { capturedOption.Value = !Equals(capturedOption.Value, true); };
                menu.Items.Add(toggleItem);
            }
        }
    }
}
