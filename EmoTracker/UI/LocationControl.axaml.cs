#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using EmoTracker.Data;
using EmoTracker.UI.Controls;
using DataLocation = EmoTracker.Data.Locations.Location;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for LocationControl.axaml
    /// </summary>
    public partial class LocationControl : ObservableUserControl
    {
        // Static panel templates reused across all LocationControl instances.
        private static readonly ITemplate<Panel?> sCompactSectionsPanel =
            new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        private static readonly ITemplate<Panel?> sFullSectionsPanel =
            new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right });

        public LocationControl()
        {
            InitializeComponent();
        }

        // ---- Compact ----
        public static readonly StyledProperty<bool> CompactProperty =
            AvaloniaProperty.Register<LocationControl, bool>(nameof(Compact), defaultValue: false);

        public bool Compact
        {
            get => GetValue(CompactProperty);
            set => SetValue(CompactProperty, value);
        }

        // ---- PreserveDimension ----
        public static readonly StyledProperty<PreserveDimension> PreserveDimensionProperty =
            AvaloniaProperty.Register<LocationControl, PreserveDimension>(
                nameof(PreserveDimension), defaultValue: PreserveDimension.None);

        public PreserveDimension PreserveDimension
        {
            get => GetValue(PreserveDimensionProperty);
            set => SetValue(PreserveDimensionProperty, value);
        }

        // ---- Compact-dependent layout helpers (used by AXAML bindings) ----

        public ITemplate<Panel?> SectionsItemsPanel =>
            Compact ? sCompactSectionsPanel : sFullSectionsPanel;

        public HorizontalAlignment SectionHorizontalAlignment =>
            Compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        public Thickness SectionItemMargin =>
            Compact ? new Thickness(5, 0, 5, 0) : new Thickness(0);

        public string? TitleText =>
            Compact
                ? (DataContext as DataLocation)?.ShortName
                : (DataContext as DataLocation)?.Name;

        // ---- PreserveDimension-dependent constraints ----
        // WPF used MultiDataTriggers to set MaxWidth=120 when PreserveDimension=Width (not compact)
        // and MaxHeight=90 when PreserveDimension=Height (not compact).
        // In Avalonia we expose these as computed properties bound from AXAML.

        public double ComputedMaxWidth =>
            !Compact && PreserveDimension == PreserveDimension.Width ? 120 : double.PositiveInfinity;

        public double ComputedMaxHeight =>
            !Compact && PreserveDimension == PreserveDimension.Height ? 90 : double.PositiveInfinity;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == CompactProperty)
            {
                NotifyPropertyChanged(nameof(SectionsItemsPanel));
                NotifyPropertyChanged(nameof(SectionHorizontalAlignment));
                NotifyPropertyChanged(nameof(SectionItemMargin));
                NotifyPropertyChanged(nameof(TitleText));
                NotifyPropertyChanged(nameof(ComputedMaxWidth));
                NotifyPropertyChanged(nameof(ComputedMaxHeight));
            }
            else if (change.Property == PreserveDimensionProperty)
            {
                NotifyPropertyChanged(nameof(ComputedMaxWidth));
                NotifyPropertyChanged(nameof(ComputedMaxHeight));
            }
            else if (change.Property == DataContextProperty)
            {
                NotifyPropertyChanged(nameof(TitleText));
            }
        }

        private void DeleteNoteButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is StyledElement elem)
            {
                if (elem.DataContext is Data.Notes.Note note)
                {
                    if (DataContext is INoteTaking noteContainer)
                        noteContainer.RemoveNote(note);
                }
            }
        }

        private void AddNoteButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is INoteTaking noteContainer)
                noteContainer.AddNote(new Data.Notes.MarkdownTextWithItemsNote());
        }

        private T? FindParentDataContextOfType<T>(Visual? child) where T : class
        {
            if (child == null)
                return null;

            if (child is StyledElement styled)
            {
                if (styled.DataContext is T contextAsT)
                    return contextAsT;
            }

            Visual? parent = child.GetVisualParent();
            if (parent != null)
                return FindParentDataContextOfType<T>(parent);

            return null;
        }

        private void RemoveNoteItemButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is StyledElement elem)
            {
                if (elem.DataContext is ITrackableItem item)
                {
                    IItemCollection? items = FindParentDataContextOfType<IItemCollection>(elem as Visual);
                    if (items != null)
                        items.RemoveItem(item);
                }
            }
        }
    }
}
