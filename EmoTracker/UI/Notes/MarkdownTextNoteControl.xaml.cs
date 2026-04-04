using EmoTracker.UI.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace EmoTracker.UI.Notes
{
    /// <summary>
    /// Interaction logic for MarkdownTextNoteControl.xaml
    /// </summary>
    public partial class MarkdownTextNoteControl : ObservableUserControl
    {
        public MarkdownTextNoteControl()
        {
            InitializeComponent();

            MarkdownSourceEditor.PreviewKeyDown += MarkdownSourceEditor_PreviewKeyDown;
            MarkdownSourceEditor.LostFocus += MarkdownSourceEditor_LostFocus;
        }

        bool mbIsEditModeEnabled = false;
        public bool IsEditModeEnabled
        {
            get { return mbIsEditModeEnabled; }
            set
            {
                if (SetProperty(ref mbIsEditModeEnabled, value))
                {
                    if (!mbIsEditModeEnabled)
                    {
                        BindingExpression be = MarkdownSourceEditor.GetBindingExpression(TextBox.TextProperty);
                        if (be != null)
                            be.UpdateSource();
                    }
                }
            }
        }

        private void MarkdownSourceEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            IsEditModeEnabled = false;
        }

        private void MarkdownSourceEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                IsEditModeEnabled = false;
                e.Handled = true;
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            IsEditModeEnabled = true;
            MarkdownSourceEditor.Focus();
        }
    }
}
