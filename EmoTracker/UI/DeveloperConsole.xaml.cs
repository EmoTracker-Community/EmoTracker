using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using System.Windows.Shapes;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for DeveloperConsole.xaml
    /// </summary>
    public partial class DeveloperConsole : Window
    {
        DateTime mLastWheelTime;

        public DeveloperConsole()
        {
            InitializeComponent();

            INotifyCollectionChanged changeTracker = ConsoleOutput.Items as INotifyCollectionChanged;
            if (changeTracker != null)
                changeTracker.CollectionChanged += ChangeTracker_CollectionChanged;

            this.Loaded += DeveloperConsole_Loaded;
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            mLastWheelTime = DateTime.Now;
            base.OnPreviewMouseWheel(e);
        }

        private void DeveloperConsole_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollToEnd();
        }

        private void ChangeTracker_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            if (ConsoleScrollViewer.VerticalOffset >= ConsoleScrollViewer.ScrollableHeight)
                mLastWheelTime = DateTime.MinValue;

            if ((DateTime.Now - mLastWheelTime).TotalMilliseconds < 60000)
                return;

            if (ConsoleOutput.Items.Count > 0)
            {
                var itemcontainer = this.ConsoleOutput.ItemContainerGenerator.ContainerFromIndex(ConsoleOutput.Items.Count - 1) as FrameworkElement;
                if (itemcontainer != null)
                    itemcontainer.BringIntoView();
            }
        }
    }
}
