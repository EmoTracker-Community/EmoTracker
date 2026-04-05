using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmoTracker.Data;
using EmoTracker.UI.Controls;

namespace EmoTracker.UI
{
    public enum PreserveDimension
    {
        None,
        Width,
        Height
    }

    /// <summary>
    /// Interaction logic for LocationControl.axaml
    /// </summary>
    public partial class LocationControl : ObservableUserControl
    {
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
