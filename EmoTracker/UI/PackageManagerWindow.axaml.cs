using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using EmoTracker.Data.Packages;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace EmoTracker.UI
{
    public partial class PackageManagerWindow : Window, INotifyPropertyChanged
    {
        // ---------------------------------------------------------------
        // Static converters for DataTemplate bindings
        // ---------------------------------------------------------------

        public static readonly IValueConverter CountGreaterThanZeroConverter =
            new FuncValueConverter<int, bool>(count => count > 0);

        public static readonly IValueConverter PackageStatusToTextConverter =
            new FuncValueConverter<PackageRepositoryEntry.PackageStatus, string>(status => status switch
            {
                PackageRepositoryEntry.PackageStatus.AppUpdateRequired => "Requires App Update",
                PackageRepositoryEntry.PackageStatus.Development       => "Dev",
                PackageRepositoryEntry.PackageStatus.Installed         => "Installed",
                PackageRepositoryEntry.PackageStatus.UpdateAvailable   => "Update Available",
                PackageRepositoryEntry.PackageStatus.DownloadError     => "Download Error",
                _                                                       => "Available",
            });

        public static readonly IValueConverter PackageStatusToColorConverter =
            new FuncValueConverter<PackageRepositoryEntry.PackageStatus, IBrush>(status => status switch
            {
                PackageRepositoryEntry.PackageStatus.AppUpdateRequired => Brushes.LightGray,
                PackageRepositoryEntry.PackageStatus.Development       => new SolidColorBrush(Color.Parse("#afafaf")),
                PackageRepositoryEntry.PackageStatus.Installed         => Brushes.Lime,
                PackageRepositoryEntry.PackageStatus.UpdateAvailable   => Brushes.Yellow,
                PackageRepositoryEntry.PackageStatus.DownloadError     => Brushes.Red,
                _                                                       => Brushes.WhiteSmoke,
            });

        /// <summary>
        /// Returns the button label for the primary (non-shift) action.
        /// Shift-held "Reinstall" / "Uninstall" swaps are handled via key events in code-behind.
        /// </summary>
        public static readonly IValueConverter PackageStatusToButtonTextConverter =
            new FuncValueConverter<PackageRepositoryEntry.PackageStatus, string>(status => status switch
            {
                PackageRepositoryEntry.PackageStatus.AppUpdateRequired => "Upgrade First",
                PackageRepositoryEntry.PackageStatus.Installed         => "Uninstall",
                PackageRepositoryEntry.PackageStatus.UpdateAvailable   => "Update",
                _                                                       => "Install",
            });

        public static readonly IValueConverter PackageStatusToButtonVisibleConverter =
            new FuncValueConverter<PackageRepositoryEntry.PackageStatus, bool>(
                status => status != PackageRepositoryEntry.PackageStatus.Development);

        /// <summary>
        /// Returns the ICommand to bind for the action button's primary (non-shift) action.
        /// </summary>
        public static readonly IValueConverter PackageStatusToCommandConverter =
            new FuncValueConverter<PackageRepositoryEntry.PackageStatus, ICommand>(status => status switch
            {
                PackageRepositoryEntry.PackageStatus.Installed => ApplicationModel.Instance.UninstallPackageCommand,
                _                                              => ApplicationModel.Instance.InstallPackageCommand,
            });

        // ---------------------------------------------------------------
        // IsShiftHeld property — triggers re-evaluation of button content
        // ---------------------------------------------------------------

        public new event PropertyChangedEventHandler PropertyChanged;

        private bool _isShiftHeld;
        public bool IsShiftHeld
        {
            get => _isShiftHeld;
            private set
            {
                if (_isShiftHeld != value)
                {
                    _isShiftHeld = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShiftHeld)));
                }
            }
        }

        // ---------------------------------------------------------------
        // Constructor / lifecycle
        // ---------------------------------------------------------------

        public PackageManagerWindow()
        {
            InitializeComponent();

            // Watch ApplicationModel filter changes to highlight sidebar buttons
            ApplicationModel.Instance.PropertyChanged += ApplicationModel_PropertyChanged;
            UpdateFilterButtonClasses();

            // Watch PackageManager for empty-repository state
            PackageManager.Instance.PropertyChanged += PackageManager_PropertyChanged;
            UpdateNoRepositoriesVisibility();
        }

        private void ApplicationModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationModel.AvailablePackageViewFilter))
                UpdateFilterButtonClasses();
        }

        private void PackageManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Repositories")
                UpdateNoRepositoriesVisibility();
        }

        private void UpdateFilterButtonClasses()
        {
            var filter = ApplicationModel.Instance.AvailablePackageViewFilter.ToString();
            SetFilterActive("FilterAllButton", filter == "All");
            SetFilterActive("FilterInstalledButton", filter == "Installed");
            SetFilterActive("FilterUpdateButton", filter == "InstalledAndHasUpdate");
        }

        private void SetFilterActive(string buttonName, bool active)
        {
            if (this.FindControl<Button>(buttonName) is Button btn)
            {
                if (active)
                {
                    if (!btn.Classes.Contains("active"))
                        btn.Classes.Add("active");
                }
                else
                {
                    btn.Classes.Remove("active");
                }
            }
        }

        private void UpdateNoRepositoriesVisibility()
        {
            if (this.FindControl<TextBlock>("NoRepositoriesText") is TextBlock tb)
                tb.IsVisible = PackageManager.Instance.Repositories?.Count() == 0;
        }

        // ---------------------------------------------------------------
        // Shift-key tracking
        // ---------------------------------------------------------------

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            IsShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            base.OnPointerMoved(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            IsShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            IsShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            base.OnKeyUp(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            ApplicationModel.Instance.PropertyChanged -= ApplicationModel_PropertyChanged;
            PackageManager.Instance.PropertyChanged -= PackageManager_PropertyChanged;
            base.OnClosed(e);
        }
    }
}
