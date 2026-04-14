using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using EmoTracker.Data;
using System;
using System.Collections.Specialized;
using EmoTracker.Data.Session;

namespace EmoTracker.UI
{
    public partial class DeveloperConsole : Window
    {
        private DateTime _lastWheelTime;

        public DeveloperConsole()
        {
            InitializeComponent();

            Opened += DeveloperConsole_Opened;

            if (TrackerSession.Current.Scripts.LogOutput is INotifyCollectionChanged changeTracker)
                changeTracker.CollectionChanged += LogOutput_CollectionChanged;
        }

        private void DeveloperConsole_Opened(object sender, EventArgs e)
        {
            ScrollToEnd();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            _lastWheelTime = DateTime.Now;
            base.OnPointerWheelChanged(e);
        }

        private void LogOutput_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(ScrollToEnd);
        }

        private void ScrollToEnd()
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ConsoleScrollViewer");
            var listBox = this.FindControl<ListBox>("ConsoleOutput");

            if (scrollViewer != null)
            {
                // ScrollableHeight equivalent: Extent.Height - Viewport.Height
                double maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
                if (scrollViewer.Offset.Y >= maxScroll)
                    _lastWheelTime = DateTime.MinValue;
            }

            if ((DateTime.Now - _lastWheelTime).TotalMilliseconds < 60000)
                return;

            if (listBox != null && listBox.ItemCount > 0)
            {
                // Retrieve the last item from the bound source collection directly
                if (listBox.ItemsSource is System.Collections.IList list && list.Count > 0)
                    listBox.ScrollIntoView(list[list.Count - 1]);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (TrackerSession.Current.Scripts.LogOutput is INotifyCollectionChanged changeTracker)
                changeTracker.CollectionChanged -= LogOutput_CollectionChanged;

            base.OnClosed(e);
        }
    }
}
