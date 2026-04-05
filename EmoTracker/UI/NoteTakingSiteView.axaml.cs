using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmoTracker.Data;
using System;
using System.Globalization;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for NoteTakingSiteView.axaml
    /// </summary>
    public partial class NoteTakingSiteView : UserControl
    {
        /// <summary>
        /// Returns <c>false</c> when the value is null, <c>true</c> otherwise.
        /// Used to hide the Items column when <c>Items</c> is null.
        /// </summary>
        public static readonly IValueConverter NullToFalseConverter =
            new FuncValueConverter<object?, bool>(v => v != null);

        public NoteTakingSiteView()
        {
            InitializeComponent();
        }

        private void DeleteNoteButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control elem)
            {
                Data.Notes.Note? note = elem.DataContext as Data.Notes.Note;
                if (note != null)
                {
                    INoteTaking? noteContainer = DataContext as INoteTaking;
                    noteContainer?.RemoveNote(note);
                }
            }
        }

        private void AddNoteButton_Click(object? sender, RoutedEventArgs e)
        {
            INoteTaking? noteContainer = DataContext as INoteTaking;
            if (noteContainer != null)
            {
                var note = new Data.Notes.MarkdownTextWithItemsNote();
                noteContainer.AddNote(note);

                // Scroll the newly added note into view.
                // ItemsControl doesn't expose ContainerFromItem directly in Avalonia;
                // use the ListBox equivalent if the template is ever changed to a ListBox.
                // For ItemsControl, we find the last visual child and bring it into view.
                if (NotesItemsControl.ItemCount > 0)
                {
                    // Trigger a layout pass so the new container exists, then scroll.
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var panel = NotesItemsControl.ItemsPanelRoot;
                        if (panel != null && panel.Children.Count > 0)
                        {
                            var last = panel.Children[panel.Children.Count - 1];
                            last.BringIntoView();
                        }
                    }, Avalonia.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        private T? FindParentDataContextOfType<T>(Visual? child) where T : class
        {
            if (child == null)
                return null;

            if (child is StyledElement se && se.DataContext is T result)
                return result;

            Visual? parent = child.GetVisualParent();
            return FindParentDataContextOfType<T>(parent);
        }

        private void RemoveNoteItemButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control elem)
            {
                ITrackableItem? item = elem.DataContext as ITrackableItem;
                if (item != null)
                {
                    IItemCollection? items = FindParentDataContextOfType<IItemCollection>(elem);
                    items?.RemoveItem(item);
                }
            }
        }
    }
}
