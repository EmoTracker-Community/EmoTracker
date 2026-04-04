using EmoTracker.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for NoteTakingSiteView.xaml
    /// </summary>
    public partial class NoteTakingSiteView : UserControl
    {
        public NoteTakingSiteView()
        {
            InitializeComponent();
        }

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
            {
                var note = new Data.Notes.MarkdownTextWithItemsNote();
                noteContainer.AddNote(note);

                var container = NotesItemsControl.ItemContainerGenerator.ContainerFromItem(note) as FrameworkElement;
                if (container != null)
                    container.BringIntoView();
            }
        }

        private T FindParentDatacontextOfType<T>(DependencyObject child) where T : class
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
                return FindParentDatacontextOfType<T>(parent);

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
                    IItemCollection items = FindParentDatacontextOfType<IItemCollection>(elem);
                    if (items != null)
                        items.RemoveItem(item);
                }
            }
        }
    }
}
