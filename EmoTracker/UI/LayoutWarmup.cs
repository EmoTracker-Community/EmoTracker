using Avalonia.Controls;
using Avalonia.Threading;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.Generic;

namespace EmoTracker.UI
{
    /// <summary>
    /// Realizes hidden tabs of a layout that is already on screen, one
    /// tab at a time while the UI thread is idle. Map tabs are left
    /// until the user selects them, because each map builds a control
    /// for every pin. Layouts that are not showing are built by
    /// <see cref="Install"/> the first time a window or popup displays
    /// them, and dropped when that host lets go.
    /// Image decode for icons that are not on screen is a separate
    /// background queue on <see cref="Media.ImageReferenceService"/>.
    /// </summary>
    public static class LayoutWarmup
    {
        static readonly List<TabContentHost> mTabHosts = new List<TabContentHost>();
        static readonly Dictionary<Layout, List<Control>> mTrees = new Dictionary<Layout, List<Control>>();

        static bool mStarted;
        static bool mScheduled;

        public static event Action<Control> TreeRemoved;

        public static void Start()
        {
            if (mStarted)
                return;
            mStarted = true;
            PackageLoader.OnPackageLoadStarting += OnPackageLoadStarting;
            PackageLoader.OnPackageLoadComplete += OnPackageLoadComplete;
        }

        public static void RegisterTabHost(TabContentHost host)
        {
            if (host == null || mTabHosts.Contains(host))
                return;
            mTabHosts.Add(host);
        }

        public static void UnregisterTabHost(TabContentHost host)
        {
            if (host == null)
                return;
            mTabHosts.Remove(host);
        }

        /// <summary>
        /// Parents a tree for <paramref name="layout"/> into
        /// <paramref name="destination"/>. A second host showing the
        /// same layout gets its own tree, because a control cannot
        /// have two parents.
        /// </summary>
        public static Control Install(Layout layout, ContentControl destination)
        {
            if (layout?.Root == null || destination == null)
                return null;

            if (!mTrees.TryGetValue(layout, out var list))
            {
                list = new List<Control>();
                mTrees[layout] = list;
            }

            Control tree = null;
            foreach (var candidate in list)
            {
                if (candidate.Parent == null || ReferenceEquals(candidate.Parent, destination))
                {
                    tree = candidate;
                    break;
                }
            }

            if (tree == null)
            {
                tree = CreateTree(layout);
                list.Add(tree);
            }

            Detach(tree);
            destination.Content = tree;
            return tree;
        }

        /// <summary>
        /// Drops a tree a host is no longer showing. The tree's content
        /// is cleared so layout bindings do not keep the controls alive.
        /// </summary>
        public static void Release(Control tree)
        {
            if (tree == null)
                return;

            Layout emptyKey = null;
            foreach (var kvp in mTrees)
            {
                if (!kvp.Value.Remove(tree))
                    continue;
                if (kvp.Value.Count == 0)
                    emptyKey = kvp.Key;
                break;
            }
            if (emptyKey != null)
                mTrees.Remove(emptyKey);

            Unload(tree);
            TreeRemoved?.Invoke(tree);
        }

        public static void Schedule()
        {
            if (mScheduled)
                return;
            mScheduled = true;
            Dispatcher.UIThread.Post(Step, DispatcherPriority.Background);
        }

        static void OnPackageLoadStarting(object sender, PackageLoader.PackageLoadEventArgs e)
        {
            if (e?.Target != null)
                DropState(e.Target);
        }

        static void OnPackageLoadComplete(object sender, PackageLoader.PackageLoadEventArgs e)
        {
            // The definitional state is forked into the window's state.
            // Warming it would build a second set of trees nobody shows.
            if (e?.Target != null && ReferenceEquals(e.Target, e.Target.PackageInstance?.DefinitionalState))
                return;
            Schedule();
        }

        static void DropState(TrackerState state)
        {
            var removed = new List<Control>();
            var staleLayouts = new List<Layout>();
            foreach (var kvp in mTrees)
            {
                if (!ReferenceEquals(kvp.Key.OwnerState, state))
                    continue;
                staleLayouts.Add(kvp.Key);
                removed.AddRange(kvp.Value);
            }
            foreach (var layout in staleLayouts)
                mTrees.Remove(layout);
            foreach (var tree in removed)
            {
                Unload(tree);
                TreeRemoved?.Invoke(tree);
            }
        }

        static void Step()
        {
            mScheduled = false;
            bool did = false;
            try
            {
                did = RealizeOneTab();
            }
            catch
            {
                // One bad tab should not stop the rest of the warmup.
            }
            if (did)
                Schedule();
        }

        static bool RealizeOneTab()
        {
            var hosts = mTabHosts.ToArray();
            foreach (var host in hosts)
            {
                if (host.TryRealizeOne())
                    return true;
            }
            return false;
        }

        static Control CreateTree(Layout layout)
        {
            return new ContentControl
            {
                Content = layout.Root,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            };
        }

        static void Unload(Control control)
        {
            // Leave the window first, then drop the layout. Clearing the
            // content removes the bindings that would otherwise pin the
            // controls to the layout items after the host has let go.
            Detach(control);
            if (control is ContentControl content)
                content.Content = null;
        }

        static void Detach(Control control)
        {
            if (control == null)
                return;
            switch (control.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(control);
                    break;
                case ContentControl content when ReferenceEquals(content.Content, control):
                    content.Content = null;
                    break;
                case Decorator decorator when ReferenceEquals(decorator.Child, control):
                    decorator.Child = null;
                    break;
            }
        }
    }
}
