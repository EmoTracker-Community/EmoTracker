using EmoTracker.Data;
using EmoTracker.UI.Controls;
using System.Windows;
using System.Windows.Media;

namespace EmoTracker.UI
{
    public enum PreserveDimension
    {
        None,
        Width,
        Height
    }

    /// <summary>
    /// Interaction logic for LocationControl.xaml
    /// </summary>
    public partial class LocationControl : ObservableUserControl
    {
        public LocationControl()
        {
            InitializeComponent();
        }

        public bool Compact
        {
            get { return (bool)GetValue(CompactProperty); }
            set { SetValue(CompactProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Compact.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CompactProperty =
            DependencyProperty.Register("Compact", typeof(bool), typeof(LocationControl), new PropertyMetadata(false));



        public PreserveDimension PreserveDimension
        {
            get { return (PreserveDimension)GetValue(PreserveDimensionProperty); }
            set { SetValue(PreserveDimensionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for PreserveDimension.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PreserveDimensionProperty =
            DependencyProperty.Register("PreserveDimension", typeof(PreserveDimension), typeof(LocationControl), new PropertyMetadata(PreserveDimension.None));

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement elem = sender as FrameworkElement;
            if (elem != null)
            {
                Data.Notes.Note note = elem.DataContext as Data.Notes.Note;
                if (note != null)
                {
                    INoteTaking noteContainer = DataContext as INoteTaking;
                    if (noteContainer != null)
                        noteContainer.RemoveNote(note);
                }
            }
        }

        private void AddNoteButton_Click(object sender, RoutedEventArgs e)
        {
            INoteTaking noteContainer = DataContext as INoteTaking;
            if (noteContainer != null)
                noteContainer.AddNote(new Data.Notes.MarkdownTextWithItemsNote());
        }

        private T FindParentDataContextOfType<T>(DependencyObject child) where T: class
        {
            if (child == null)
                return null;

            FrameworkElement elem = child as FrameworkElement;
            if (elem != null)
            {
                T contextAsT = elem.DataContext as T;
                if (contextAsT != null)
                    return contextAsT;
            }

            DependencyObject parent = VisualTreeHelper.GetParent(child);
            if (parent != null)
                return FindParentDataContextOfType<T>(parent);

            return null;
        }

        private void RemoveNoteItemButton_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement elem = sender as FrameworkElement;
            if (elem != null)
            {
                ITrackableItem item = elem.DataContext as ITrackableItem;
                if (item != null)
                {
                    IItemCollection items = FindParentDataContextOfType<IItemCollection>(elem);
                    if (items != null)
                        items.RemoveItem(item);
                }
            }
        }
    }
}
