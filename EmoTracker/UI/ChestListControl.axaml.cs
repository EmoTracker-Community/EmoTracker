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

        private readonly ObservableCollection<bool> mChestStates = new ObservableCollection<bool>();

        public IEnumerable<bool> Chests => mChestStates;

        /// <summary>
        /// Returns the DataTemplate to use for the ContentPresenter.
        /// Replaces the WPF DataTrigger that switched between CompactTemplate and FullTemplate.
        /// </summary>
        public IDataTemplate? CurrentTemplate =>
            Compact
                ? Resources.TryGetValue("CompactTemplate", out var ct) ? ct as IDataTemplate : null
                : Resources.TryGetValue("FullTemplate", out var ft) ? ft as IDataTemplate : null;

        public ChestListControl()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            UpdateChests();
        }

        private void UpdateChests()
        {
            while (mChestStates.Count > Count)
                mChestStates.RemoveAt(0);

            while (mChestStates.Count < Count)
                mChestStates.Add(true);

            for (int i = 0; i < Count; ++i)
                mChestStates[i] = i < Available;
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
