using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmoTracker.Data.Layout;
using EmoTracker.UI.Media;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for LayoutControl.axaml
    /// </summary>
    public partial class LayoutControl : UserControl
    {
        Control mInstalled;
        bool mAttached;

        public LayoutControl()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            SwapInstalledTree();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            mAttached = true;
            LayoutWarmup.TreeRemoved -= OnTreeRemoved;
            LayoutWarmup.TreeRemoved += OnTreeRemoved;
            // DataContext may have been applied before this control joined a window.
            SwapInstalledTree();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            mAttached = false;
            LayoutWarmup.TreeRemoved -= OnTreeRemoved;
            ReleaseInstalled();
            base.OnDetachedFromVisualTree(e);
        }

        void OnTreeRemoved(Control tree)
        {
            if (!ReferenceEquals(mInstalled, tree))
                return;
            mInstalled = null;
            if (RootHost != null && ReferenceEquals(RootHost.Content, tree))
                RootHost.Content = null;
        }

        void ReleaseInstalled()
        {
            var previous = mInstalled;
            mInstalled = null;
            LayoutWarmup.Release(previous);
        }

        void SwapInstalledTree()
        {
            if (RootHost == null)
                return;

            // A closed popup still owns this control. Its tree waits until
            // the popup is actually on screen.
            if (!mAttached)
            {
                ReleaseInstalled();
                return;
            }

            if (mInstalled != null
                && ReferenceEquals(RootHost.Content, mInstalled)
                && mInstalled is ContentControl existing
                && DataContext is Layout current
                && ReferenceEquals(existing.Content, current.Root))
                return;

            ReleaseInstalled();

            if (DataContext is Layout layout && layout.Root != null)
            {
                mInstalled = LayoutWarmup.Install(layout, RootHost);
                // Queue the showing images before idle prefetch starts,
                // so the first paint is not stuck behind the rest of the pack.
                VisibleImageBoost.Boost(layout, ImagePriority.Normal);
                var images = ImageReferenceService.Instance;
                images.BoostDisplayedIcons = true;
                images.DeferIdlePrefetch = false;
                LayoutWarmup.Schedule();
            }
        }
    }
}
