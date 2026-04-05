using Avalonia.Controls;
using EmoTracker.Data;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace EmoTracker.Extensions.VariantSwitcher
{
    public partial class VariantSwitcherControl : UserControl
    {
        public VariantSwitcherControl()
        {
            InitializeComponent();
            Tracker.Instance.PropertyChanged += Tracker_PropertyChanged;
            UpdateFolderIconVisibility();
        }

        private void Tracker_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Tracker.ActiveGamePackage))
                UpdateFolderIconVisibility();
        }

        private void UpdateFolderIconVisibility()
        {
            if (this.FindControl<TextBlock>("FolderIcon") is not TextBlock icon)
                return;

            var count = Tracker.Instance.ActiveGamePackage?.AvailableVariants?.Count() ?? 0;
            icon.IsVisible = count > 0;
        }
    }
}
