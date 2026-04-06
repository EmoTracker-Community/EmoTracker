#nullable enable annotations
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for GroupedLocationListControl.axaml
    /// </summary>
    public partial class GroupedLocationListControl : UserControl, INotifyPropertyChanged
    {
        public new event PropertyChangedEventHandler? PropertyChanged;

        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public GroupedLocationListControl()
        {
            InitializeComponent();
        }

        private int mScale = 100;

        public int Scale
        {
            get => mScale;
            set => mScale = value;
        }

        private double mScaleMultiplier = 1.0;

        public double ScaleMultiplier
        {
            get => mScaleMultiplier;
            set { mScaleMultiplier = value; NotifyPropertyChanged(); }
        }

        /// <summary>
        /// Replaces WPF's OnPreviewMouseWheel.  Ctrl+scroll adjusts the zoom level.
        /// </summary>
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                // Avalonia Delta.Y is in lines (positive = scroll up / zoom in).
                // WPF used e.Delta (120 per notch) / 60 ≈ 2 per notch.
                // Avalonia reports +1 / -1 per notch, so multiply by 2 to match.
                Scale += (int)(e.Delta.Y * 2);
                Scale = Math.Max(50, Math.Min(Scale, 100));
                ScaleMultiplier = Scale / 100.0;
                e.Handled = true;
            }
        }
    }
}
