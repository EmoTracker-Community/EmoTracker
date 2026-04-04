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

namespace EmoTracker.UI.Controls
{
    /// <summary>
    /// Interaction logic for MarkdownViewer.xaml
    /// </summary>
    public partial class MarkdownViewer : UserControl
    {
        public MarkdownViewer()
        {
            InitializeComponent();
        }

        public string Markdown
        {
            get { return (string)GetValue(MarkdownProperty); }
            set { SetValue(MarkdownProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Markdown.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.Register("Markdown", typeof(string), typeof(MarkdownViewer), new PropertyMetadata(null));
    }
}
