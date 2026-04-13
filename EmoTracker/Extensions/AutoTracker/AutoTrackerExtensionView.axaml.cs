using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
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

        // Pulse animation state
        private DispatcherTimer _pulseTimer;
        private double _pulsePhase = 0.0;
        private static readonly Color PulseFrom = Color.Parse("#717171");
        private static readonly Color PulseTo = Color.Parse("#53A893");

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

        private void ContextMenu_Opening(object sender, CancelEventArgs e)
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
                case nameof(AutoTrackerExtension.SelectedProvider):
                    UpdateStatusColor();
                    break;
            }
        }

        private bool CanStartAutoTracking =>
            _extension != null &&
            _extension.ActiveProvider == null &&
            _extension.SelectedProvider != null &&
            _extension.SelectedProvider.DefaultDevice != null;

        private void StartPulse()
        {
            if (_pulseTimer != null)
                return;

            _pulsePhase = 0.0;
            _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _pulseTimer.Tick += OnPulseTick;
            _pulseTimer.Start();
        }

        private void StopPulse()
        {
            if (_pulseTimer == null)
                return;

            _pulseTimer.Stop();
            _pulseTimer.Tick -= OnPulseTick;
            _pulseTimer = null;
        }

        private void OnPulseTick(object sender, EventArgs e)
        {
            _pulsePhase += 0.0125; // full grey→cyan→grey cycle in ~4 seconds at 50ms intervals
            if (_pulsePhase >= 1.0)
                _pulsePhase -= 1.0;

            double t = (Math.Sin(_pulsePhase * 2 * Math.PI) + 1.0) / 2.0;

            byte r = (byte)(PulseFrom.R + (PulseTo.R - PulseFrom.R) * t);
            byte g = (byte)(PulseFrom.G + (PulseTo.G - PulseFrom.G) * t);
            byte b = (byte)(PulseFrom.B + (PulseTo.B - PulseFrom.B) * t);

            if (this.FindControl<TextBlock>("StatusIcon") is TextBlock icon)
                icon.Foreground = new SolidColorBrush(new Color(255, r, g, b));
        }

        private void UpdateStatusColor()
        {
            if (this.FindControl<TextBlock>("StatusIcon") is not TextBlock icon)
                return;

            if (_extension == null)
            {
                StopPulse();
                icon.Foreground = SolidColorBrush.Parse("#717171");
                return;
            }

            if (_extension.Error)
            {
                StopPulse();
                icon.Foreground = SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Error);
            }
            else if (!_extension.Connected && _extension.ActiveProvider != null)
            {
                StopPulse();
                icon.Foreground = SolidColorBrush.Parse(ApplicationColors.Instance.Status_Generic_Warning);
            }
            else if (_extension.ActiveProvider != null)
            {
                StopPulse();
                icon.Foreground = new SolidColorBrush(Color.Parse("#35e0b5"));
            }
            else if (CanStartAutoTracking)
            {
                StartPulse();
            }
            else
            {
                StopPulse();
                icon.Foreground = SolidColorBrush.Parse("#717171");
            }
        }

        private void RebuildContextMenu()
        {
            if (_extension == null)
                return;

            var grid = this.Content as Grid;
            if (grid?.ContextMenu == null)
                return;

            var menu = grid.ContextMenu;
            menu.Items.Clear();

            // Start / Stop
            menu.Items.Add(new MenuItem { Header = "Start", Command = _extension.StartCommand });
            menu.Items.Add(new MenuItem { Header = "Stop", Command = _extension.StopCommand });
            menu.Items.Add(new Separator());

            // Provider submenu
            var providerMenu = new MenuItem { Header = "Provider" };
            foreach (var provider in _extension.ApplicableProviders)
            {
                var isSelected = provider == _extension.SelectedProvider;
                var providerItem = new MenuItem
                {
                    Header = provider.DisplayName,
                    Icon = isSelected ? new TextBlock { Text = "\u2713" } : null
                };
                var capturedProvider = provider;
                providerItem.Click += (s, e) =>
                {
                    if (_extension.SetProviderCommand.CanExecute(capturedProvider))
                        _extension.SetProviderCommand.Execute(capturedProvider);
                };
                providerMenu.Items.Add(providerItem);
            }
            menu.Items.Add(providerMenu);

            // Device submenu (with connection status)
            var deviceMenu = new MenuItem { Header = "Device" };
            if (_extension.SelectedProvider != null)
            {
                foreach (var device in _extension.SelectedProvider.AvailableDevices)
                {
                    var isDefault = device == _extension.SelectedProvider.DefaultDevice;
                    string status = device.IsConnected ? " [Connected]" : "";
                    var deviceItem = new MenuItem
                    {
                        Header = device.DisplayName + status,
                        Icon = isDefault ? new TextBlock { Text = "\u2713" } : null
                    };
                    var capturedDevice = device;
                    deviceItem.Click += (s, e) =>
                    {
                        if (_extension.SetDeviceCommand.CanExecute(capturedDevice))
                            _extension.SetDeviceCommand.Execute(capturedDevice);
                    };
                    deviceMenu.Items.Add(deviceItem);
                }
            }
            menu.Items.Add(deviceMenu);

            // Provider options
            if (_extension.SelectedProvider != null && _extension.SelectedProvider.Options.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var option in _extension.SelectedProvider.Options)
                {
                    AddOptionMenuItem(menu, option);
                }
            }

            // Device options (from default device)
            if (_extension.SelectedProvider?.DefaultDevice != null && _extension.SelectedProvider.DefaultDevice.Options.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var option in _extension.SelectedProvider.DefaultDevice.Options)
                {
                    AddOptionMenuItem(menu, option);
                }
            }

            // Device operations (from default device)
            if (_extension.SelectedProvider?.DefaultDevice != null && _extension.SelectedProvider.DefaultDevice.Operations.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var operation in _extension.SelectedProvider.DefaultDevice.Operations)
                {
                    var opItem = new MenuItem
                    {
                        Header = operation.DisplayName,
                        IsEnabled = operation.CanExecute
                    };
                    var capturedOp = operation;
                    opItem.Click += async (s, e) =>
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
