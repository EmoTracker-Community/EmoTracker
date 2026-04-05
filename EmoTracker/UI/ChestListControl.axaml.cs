using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for ChestListControl.axaml
    /// </summary>
    public partial class ChestListControl : UserControl
    {
        #region --- Styled Properties ---

        public static readonly StyledProperty<IImage?> ClosedChestProperty =
            AvaloniaProperty.Register<ChestListControl, IImage?>(nameof(ClosedChest));

        public IImage? ClosedChest
        {
            get => GetValue(ClosedChestProperty);
            set => SetValue(ClosedChestProperty, value);
        }

        public static readonly StyledProperty<IImage?> OpenChestProperty =
            AvaloniaProperty.Register<ChestListControl, IImage?>(nameof(OpenChest));

        public IImage? OpenChest
        {
            get => GetValue(OpenChestProperty);
            set => SetValue(OpenChestProperty, value);
        }

        public static readonly StyledProperty<IImage?> UnavailableClosedChestProperty =
            AvaloniaProperty.Register<ChestListControl, IImage?>(nameof(UnavailableClosedChest));

        public IImage? UnavailableClosedChest
        {
            get => GetValue(UnavailableClosedChestProperty);
            set => SetValue(UnavailableClosedChestProperty, value);
        }

        public static readonly StyledProperty<IImage?> UnavailableOpenChestProperty =
            AvaloniaProperty.Register<ChestListControl, IImage?>(nameof(UnavailableOpenChest));

        public IImage? UnavailableOpenChest
        {
            get => GetValue(UnavailableOpenChestProperty);
            set => SetValue(UnavailableOpenChestProperty, value);
        }

        public static readonly StyledProperty<bool> AccessibleProperty =
            AvaloniaProperty.Register<ChestListControl, bool>(nameof(Accessible), defaultValue: true);

        public bool Accessible
        {
            get => GetValue(AccessibleProperty);
            set => SetValue(AccessibleProperty, value);
        }

        public static readonly StyledProperty<uint> CountProperty =
            AvaloniaProperty.Register<ChestListControl, uint>(nameof(Count), defaultValue: 5u);

        public uint Count
        {
            get => GetValue(CountProperty);
            set => SetValue(CountProperty, value);
        }

        public static readonly StyledProperty<uint> AvailableProperty =
            AvaloniaProperty.Register<ChestListControl, uint>(nameof(Available), defaultValue: 3u,
                defaultBindingMode: BindingMode.TwoWay);

        public uint Available
        {
            get => GetValue(AvailableProperty);
            set => SetValue(AvailableProperty, value);
        }

        public static readonly StyledProperty<bool> ClearAsGroupProperty =
            AvaloniaProperty.Register<ChestListControl, bool>(nameof(ClearAsGroup), defaultValue: false);

        public bool ClearAsGroup
        {
            get => GetValue(ClearAsGroupProperty);
            set => SetValue(ClearAsGroupProperty, value);
        }

        public static readonly StyledProperty<bool> CompactProperty =
            AvaloniaProperty.Register<ChestListControl, bool>(nameof(Compact), defaultValue: false);

        public bool Compact
        {
            get => GetValue(CompactProperty);
            set => SetValue(CompactProperty, value);
        }

        #endregion

        // Each entry is the pre-resolved IImage for that chest slot (computed in UpdateChests).
        private readonly ObservableCollection<IImage?> mChestImages = new ObservableCollection<IImage?>();

        public IEnumerable<IImage?> Chests => mChestImages;

        // The single image shown in compact mode.
        public static readonly StyledProperty<IImage?> CurrentCompactImageProperty =
            AvaloniaProperty.Register<ChestListControl, IImage?>(nameof(CurrentCompactImage));

        public IImage? CurrentCompactImage
        {
            get => GetValue(CurrentCompactImageProperty);
            private set => SetValue(CurrentCompactImageProperty, value);
        }

        /// <summary>
        /// Returns the DataTemplate to use for the ContentPresenter.
        /// Replaces the WPF DataTrigger that switched between CompactTemplate and FullTemplate.
        /// </summary>
        public IDataTemplate? CurrentTemplate =>
            Compact
                ? Resources.TryGetValue("CompactTemplate", out var ct) ? ct as IDataTemplate : null
                : Resources.TryGetValue("FullTemplate", out var ft) ? ft as IDataTemplate : null;

        // Cached copies of StyledProperty values used in UpdateChests.
        // Accessing GetValue() during popup teardown can throw because bindings transiently
        // set values to UnsetValue, causing style-resolution traversal on a broken visual tree.
        // We cache the values here so UpdateChests() never calls GetValue() at all.
        private IImage? _closedChest;
        private IImage? _openChest;
        private IImage? _unavailableClosedChest;
        private IImage? _unavailableOpenChest;
        private bool    _accessible = true;  // mirrors AccessibleProperty default
        private uint    _count      = 5u;    // mirrors CountProperty default
        private uint    _available  = 3u;    // mirrors AvailableProperty default

        public ChestListControl()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            // Keep cached values in sync and only call UpdateChests for the properties
            // that actually affect display. Calling it for every property change (including
            // inherited layout/visual properties) causes crashes during popup teardown when
            // Avalonia fires property-changed notifications while the overlay's visual root
            // is already detached from the window.
            if      (change.Property == ClosedChestProperty)            _closedChest            = change.GetNewValue<IImage?>();
            else if (change.Property == OpenChestProperty)              _openChest              = change.GetNewValue<IImage?>();
            else if (change.Property == UnavailableClosedChestProperty) _unavailableClosedChest = change.GetNewValue<IImage?>();
            else if (change.Property == UnavailableOpenChestProperty)   _unavailableOpenChest   = change.GetNewValue<IImage?>();
            else if (change.Property == AccessibleProperty)             _accessible             = change.GetNewValue<bool>();
            else if (change.Property == CountProperty)                  _count                  = change.GetNewValue<uint>();
            else if (change.Property == AvailableProperty)              _available              = change.GetNewValue<uint>();
            else return;

            UpdateChests();
        }

        private void UpdateChests()
        {
            while (mChestImages.Count > _count)
                mChestImages.RemoveAt(0);
            while (mChestImages.Count < _count)
                mChestImages.Add(null);

            for (int i = 0; i < (int)_count; ++i)
            {
                bool available = i < (int)_available;
                mChestImages[i] = available
                    ? (_accessible ? _closedChest            : _unavailableClosedChest)
                    : (_accessible ? _openChest              : _unavailableOpenChest);
            }

            // Compact mode: single image — open chest when all cleared, closed otherwise
            CurrentCompactImage = _available == 0
                ? (_accessible ? _openChest : _unavailableOpenChest)
                : (_accessible ? _closedChest : _unavailableClosedChest);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!IsEnabled)
                return;

            var point = e.GetCurrentPoint(this);

            if (point.Properties.IsLeftButtonPressed)
            {
                if (ClearAsGroup)
                    Available = 0;
                else
                    Available = (uint)Math.Max(0, (int)Available - 1);
                e.Handled = true;
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                if (ClearAsGroup)
                    Available = Count;
                else
                    Available = (uint)Math.Min(Count, (int)Available + 1);
                e.Handled = true;
            }
        }
    }
}
