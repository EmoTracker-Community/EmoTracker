using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using EmoTracker.Data.Layout;
using EmoTracker.UI.Media;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace EmoTracker.UI
{
    /// <summary>
    /// Keeps the non-map tabs of the showing layout alive and shows the
    /// current one. The stock TabControl presenter destroys the outgoing
    /// tree and rebuilds the incoming one on each switch. A map tab is
    /// built the first time it is selected, because each map creates a
    /// control for every pin. Hidden tabs are not measured, so they do
    /// not affect the window size.
    /// </summary>
    public class TabContentHost : Panel
    {
        public static readonly StyledProperty<IEnumerable<TabPanel.Tab>> TabsProperty =
            AvaloniaProperty.Register<TabContentHost, IEnumerable<TabPanel.Tab>>(nameof(Tabs));

        public static readonly StyledProperty<TabPanel.Tab> CurrentTabProperty =
            AvaloniaProperty.Register<TabContentHost, TabPanel.Tab>(nameof(CurrentTab));

        readonly Dictionary<TabPanel.Tab, ContentControl> mHosts = new Dictionary<TabPanel.Tab, ContentControl>();
        readonly HashSet<ContentControl> mRealized = new HashSet<ContentControl>();
        // Map tabs build a control per pin. They stay unrealized until selected.
        readonly HashSet<ContentControl> mMapTabs = new HashSet<ContentControl>();
        INotifyCollectionChanged mObservedTabs;
        Size mLastAvailableSize;

        public IEnumerable<TabPanel.Tab> Tabs
        {
            get => GetValue(TabsProperty);
            set => SetValue(TabsProperty, value);
        }

        public TabPanel.Tab CurrentTab
        {
            get => GetValue(CurrentTabProperty);
            set => SetValue(CurrentTabProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TabsProperty)
            {
                if (mObservedTabs != null)
                    mObservedTabs.CollectionChanged -= OnTabsChanged;
                mObservedTabs = Tabs as INotifyCollectionChanged;
                if (mObservedTabs != null)
                    mObservedTabs.CollectionChanged += OnTabsChanged;
                RebuildHosts();
            }
            else if (change.Property == CurrentTabProperty)
            {
                UpdateVisibility();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LayoutWarmup.RegisterTabHost(this);
            LayoutWarmup.Schedule();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LayoutWarmup.UnregisterTabHost(this);
            base.OnDetachedFromVisualTree(e);
        }

        void OnTabsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildHosts();
        }

        void RebuildHosts()
        {
            var keep = new HashSet<TabPanel.Tab>();
            if (Tabs != null)
            {
                foreach (var tab in Tabs)
                {
                    if (tab == null)
                        continue;
                    keep.Add(tab);
                    if (mHosts.ContainsKey(tab))
                        continue;

                    var host = new ContentControl
                    {
                        DataContext = tab,
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        IsVisible = false,
                    };
                    host.Bind(ContentControl.ContentProperty, new Binding(nameof(TabPanel.Tab.Content)));
                    mHosts[tab] = host;
                    Children.Add(host);
                }
            }

            var stale = new List<TabPanel.Tab>();
            foreach (var kvp in mHosts)
            {
                if (keep.Contains(kvp.Key))
                    continue;
                Children.Remove(kvp.Value);
                mRealized.Remove(kvp.Value);
                mMapTabs.Remove(kvp.Value);
                stale.Add(kvp.Key);
            }
            foreach (var tab in stale)
                mHosts.Remove(tab);

            UpdateVisibility();
        }

        void UpdateVisibility()
        {
            var current = CurrentTab;
            foreach (var kvp in mHosts)
                kvp.Value.IsVisible = ReferenceEquals(kvp.Key, current);

            if (current != null)
                VisibleImageBoost.BoostContent(current.Content, ImagePriority.Immediate);

            LayoutWarmup.Schedule();
        }

        /// <summary>
        /// Builds one hidden tab that has not been realized yet.
        /// Returns false when this host has nothing left to build.
        /// </summary>
        internal bool TryRealizeOne()
        {
            if (this.GetVisualRoot() == null)
                return false;
            if (mLastAvailableSize.Width <= 0 || mLastAvailableSize.Height <= 0)
                return false;

            ContentControl next = null;
            foreach (var child in Children)
            {
                if (child is not ContentControl host || host.IsVisible || mRealized.Contains(host))
                    continue;
                if (mMapTabs.Contains(host))
                    continue;
                if (host.DataContext is TabPanel.Tab tab && ShowsMapImmediately(tab.Content))
                {
                    mMapTabs.Add(host);
                    continue;
                }
                next = host;
                break;
            }
            if (next == null)
                return false;

            // Never retry a child whose template failed to apply.
            mRealized.Add(next);
            var size = FiniteSize(mLastAvailableSize);
            next.IsVisible = true;
            try
            {
                next.Measure(size);
            }
            finally
            {
                next.IsVisible = false;
            }
            return true;
        }

        static Size FiniteSize(Size size)
        {
            double width = size.Width;
            double height = size.Height;
            if (double.IsInfinity(width) || width <= 0)
                width = 1280;
            if (double.IsInfinity(height) || height <= 0)
                height = 960;
            return new Size(width, height);
        }

        /// <summary>
        /// True when measuring this item would build a map, including
        /// the current page of a nested tab panel. Other pages of that
        /// panel are not built until they are selected.
        /// </summary>
        static bool ShowsMapImmediately(LayoutItem item)
        {
            var layouts = new HashSet<Layout>();
            var items = new HashSet<LayoutItem>();
            return ShowsMapImmediately(item, layouts, items);
        }

        static bool ShowsMapImmediately(LayoutItem item, HashSet<Layout> layouts, HashSet<LayoutItem> items)
        {
            if (item == null || !items.Add(item))
                return false;

            switch (item)
            {
                case MapPanel:
                    return true;
                case TabPanel panel:
                    return ShowsMapImmediately(panel.CurrentTab?.Content, layouts, items);
                case LayoutReference reference:
                    var target = reference.Layout;
                    if (target == null || !layouts.Add(target))
                        return false;
                    return ShowsMapImmediately(target.Root, layouts, items);
                case ButtonPopup:
                    // The popup body is not created until the popup opens.
                    return false;
            }

            foreach (var child in item.EnumerateChildren())
            {
                if (ShowsMapImmediately(child, layouts, items))
                    return true;
            }
            return false;
        }

        ContentControl CurrentHost()
        {
            var current = CurrentTab;
            if (current != null && mHosts.TryGetValue(current, out var host))
                return host;
            return null;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (availableSize.Width > 0 && availableSize.Height > 0
                && !double.IsInfinity(availableSize.Width)
                && !double.IsInfinity(availableSize.Height))
            {
                mLastAvailableSize = availableSize;
            }

            double width = 0;
            double height = 0;
            foreach (var child in Children)
            {
                if (!child.IsVisible)
                    continue;
                child.Measure(availableSize);
                if (child is ContentControl host)
                    mRealized.Add(host);
                if (width < child.DesiredSize.Width)
                    width = child.DesiredSize.Width;
                if (height < child.DesiredSize.Height)
                    height = child.DesiredSize.Height;
            }

            LayoutWarmup.Schedule();
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var current = CurrentHost();
            var rect = new Rect(finalSize);
            foreach (var child in Children)
            {
                if (!child.IsVisible)
                    continue;
                if (!ReferenceEquals(child, current))
                    continue;
                child.Arrange(rect);
            }
            return finalSize;
        }
    }
}
