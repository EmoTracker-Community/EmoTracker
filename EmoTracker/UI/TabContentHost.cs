#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using EmoTracker.Data.Layout;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace EmoTracker.UI
{
    /// <summary>
    /// Hosts the content of every <see cref="TabPanel.Tab"/> at once and shows the
    /// current one by toggling <c>IsVisible</c>, instead of the stock TabControl
    /// behaviour of swapping one ContentPresenter (which tears down the outgoing
    /// tab's whole control tree and rebuilds the incoming one on every switch).
    ///
    /// Hidden children are not measured, so each tab's tree is still built lazily
    /// on its first visit; after that a switch costs one layout pass over the
    /// visible tab only.
    /// </summary>
    public class TabContentHost : Panel
    {
        public static readonly StyledProperty<IEnumerable<TabPanel.Tab>?> TabsProperty =
            AvaloniaProperty.Register<TabContentHost, IEnumerable<TabPanel.Tab>?>(nameof(Tabs));

        public static readonly StyledProperty<TabPanel.Tab?> CurrentTabProperty =
            AvaloniaProperty.Register<TabContentHost, TabPanel.Tab?>(nameof(CurrentTab));

        /// <summary>
        /// When true (default), hidden tabs are realized one per idle dispatcher
        /// tick after the current tab has laid out, so the first visit to each tab
        /// is already paid for by the time the user clicks it.
        /// </summary>
        public static readonly StyledProperty<bool> PrefetchProperty =
            AvaloniaProperty.Register<TabContentHost, bool>(nameof(Prefetch), defaultValue: true);

        public bool Prefetch
        {
            get => GetValue(PrefetchProperty);
            set => SetValue(PrefetchProperty, value);
        }

        public IEnumerable<TabPanel.Tab>? Tabs
        {
            get => GetValue(TabsProperty);
            set => SetValue(TabsProperty, value);
        }

        public TabPanel.Tab? CurrentTab
        {
            get => GetValue(CurrentTabProperty);
            set => SetValue(CurrentTabProperty, value);
        }

        private INotifyCollectionChanged? mObservedTabs;
        private readonly Dictionary<TabPanel.Tab, ContentControl> mHosts = new Dictionary<TabPanel.Tab, ContentControl>();

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

        private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildHosts();
        }

        private void RebuildHosts()
        {
            var tabs = Tabs;
            var keep = new HashSet<TabPanel.Tab>();

            if (tabs != null)
            {
                foreach (var tab in tabs)
                {
                    keep.Add(tab);
                    if (!mHosts.ContainsKey(tab))
                    {
                        var host = new ContentControl
                        {
                            // Same as the old TabControl.ContentTemplate: the LayoutItem
                            // DataTemplates on LayoutControl resolve through DataContext.
                            DataContext = tab,
                            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                            IsVisible = false,
                        };
                        host.Bind(ContentControl.ContentProperty, new Avalonia.Data.Binding(nameof(TabPanel.Tab.Content)));
                        mHosts[tab] = host;
                        Children.Add(host);
                    }
                }
            }

            var stale = new List<TabPanel.Tab>();
            foreach (var kvp in mHosts)
            {
                if (!keep.Contains(kvp.Key))
                {
                    Children.Remove(kvp.Value);
                    mPrefetched.Remove(kvp.Value);
                    stale.Add(kvp.Key);
                }
            }
            foreach (var tab in stale)
                mHosts.Remove(tab);

            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            var current = CurrentTab;
            foreach (var kvp in mHosts)
                kvp.Value.IsVisible = ReferenceEquals(kvp.Key, current);
            SchedulePrefetch();
        }

        // ---- prefetch --------------------------------------------------------

        private bool mPrefetchScheduled;
        private Size mLastAvailableSize = Size.Infinity;

        private readonly HashSet<ContentControl> mPrefetched = new HashSet<ContentControl>();

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            SchedulePrefetch();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            mPrefetchScheduled = false;
            base.OnDetachedFromVisualTree(e);
        }

        private void SchedulePrefetch()
        {
            if (mPrefetchScheduled || !Prefetch || !IsAttachedToVisualTree())
                return;
            mPrefetchScheduled = true;
            // Background priority runs after pending layout and render work, so the
            // visible tab is on screen before any hidden tab is built.
            Avalonia.Threading.Dispatcher.UIThread.Post(PrefetchOne, Avalonia.Threading.DispatcherPriority.Background);
        }

        private bool IsAttachedToVisualTree() => this.GetVisualRoot() != null;

        private void PrefetchOne()
        {
            mPrefetchScheduled = false;
            if (!Prefetch || !IsAttachedToVisualTree())
                return;

            ContentControl? next = null;
            foreach (var child in Children)
            {
                // A ContentControl whose template has not been applied has no
                // presenter yet; that is the "not realized" state.
                if (child is ContentControl cc && !cc.IsVisible && cc.Presenter == null && !mPrefetched.Contains(cc))
                {
                    next = cc;
                    break;
                }
            }
            if (next == null)
                return;

            // Realize by running one measure while briefly visible, then hide
            // again. Measure on an attached control applies its template and
            // builds the content tree synchronously; hiding it afterwards keeps
            // it out of normal layout passes.
            mPrefetched.Add(next); // never retry a child whose template failed to apply
            next.IsVisible = true;
            try
            {
                next.Measure(mLastAvailableSize);
            }
            finally
            {
                next.IsVisible = false;
            }

            // One tab per tick so input stays responsive between builds.
            SchedulePrefetch();
        }

        // Single-cell layout: every visible child gets the full slot. Invisible
        // children are skipped by Avalonia's Measure/Arrange.

        protected override Size MeasureOverride(Size availableSize)
        {
            mLastAvailableSize = availableSize;
            double w = 0, h = 0;
            foreach (var child in Children)
            {
                if (!child.IsVisible) continue;
                child.Measure(availableSize);
                w = Math.Max(w, child.DesiredSize.Width);
                h = Math.Max(h, child.DesiredSize.Height);
            }
            // Arm prefetch once the first real measure has happened (initial
            // CurrentTab assignment occurs before the host is attached).
            SchedulePrefetch();
            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var rect = new Rect(finalSize);
            foreach (var child in Children)
            {
                if (!child.IsVisible) continue;
                child.Arrange(rect);
            }
            return finalSize;
        }
    }
}
