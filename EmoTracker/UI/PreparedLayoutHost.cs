using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmoTracker.Data.Layout;
using EmoTracker.UI.Media;

namespace EmoTracker.UI
{
    /// <summary>
    /// Shows a layout by building its control tree while this host is on
    /// screen. Button popups use this. The popup is not in the visual tree
    /// until it opens, so the tree is built on open and dropped on close.
    /// </summary>
    public class PreparedLayoutHost : ContentControl
    {
        public static readonly StyledProperty<Layout> LayoutProperty =
            AvaloniaProperty.Register<PreparedLayoutHost, Layout>(nameof(Layout));

        Control mInstalled;
        Layout mInstalledLayout;
        bool mAttached;

        public Layout Layout
        {
            get => GetValue(LayoutProperty);
            set => SetValue(LayoutProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == LayoutProperty)
                Swap();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            mAttached = true;
            LayoutWarmup.TreeRemoved -= OnTreeRemoved;
            LayoutWarmup.TreeRemoved += OnTreeRemoved;
            Swap();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            mAttached = false;
            LayoutWarmup.TreeRemoved -= OnTreeRemoved;
            Release();
            base.OnDetachedFromVisualTree(e);
        }

        void OnTreeRemoved(Control tree)
        {
            if (!ReferenceEquals(mInstalled, tree))
                return;
            mInstalled = null;
            mInstalledLayout = null;
            if (ReferenceEquals(Content, tree))
                Content = null;
        }

        void Swap()
        {
            // The popup's child is not in a visual tree until it opens.
            // Installing earlier would build the popup during the parent
            // layout's measure, which is the hitch this host avoids.
            if (!mAttached)
                return;

            if (Layout?.Root == null)
            {
                Release();
                return;
            }

            if (ReferenceEquals(mInstalledLayout, Layout)
                && mInstalled != null
                && ReferenceEquals(Content, mInstalled))
                return;

            Release();
            mInstalled = LayoutWarmup.Install(Layout, this);
            mInstalledLayout = Layout;
            if (mInstalled != null)
                VisibleImageBoost.Boost(Layout, ImagePriority.Immediate);
        }

        void Release()
        {
            var tree = mInstalled;
            mInstalled = null;
            mInstalledLayout = null;
            if (tree == null)
                return;
            LayoutWarmup.Release(tree);
        }
    }
}
