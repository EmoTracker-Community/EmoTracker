using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media;
using System.Collections.Generic;

namespace EmoTracker.UI
{
    /// <summary>
    /// Queues the images the user can see right now ahead of the idle prefetch.
    /// Inactive tabs, other stages, and layouts that are not showing stay at
    /// <see cref="ImagePriority.Idle"/>, which is where pack load enqueues them.
    /// </summary>
    public static class VisibleImageBoost
    {
        public static void Boost(Layout layout, ImagePriority priority)
        {
            if (layout?.Root == null)
                return;

            var layouts = new HashSet<Layout>();
            var items = new HashSet<LayoutItem>();
            BoostLayout(layout, priority, layouts, items);
        }

        /// <summary>
        /// Boosts the images in a subtree that just became visible, such as
        /// the tab the user switched to. Other tabs of a nested panel stay idle.
        /// </summary>
        public static void BoostContent(LayoutItem item, ImagePriority priority)
        {
            if (item == null)
                return;

            var layouts = new HashSet<Layout>();
            var items = new HashSet<LayoutItem>();
            BoostItem(item, priority, layouts, items);
        }

        static void BoostLayout(Layout layout, ImagePriority priority, HashSet<Layout> layouts, HashSet<LayoutItem> items)
        {
            if (layout?.Root == null)
                return;
            if (!layouts.Add(layout))
                return;
            BoostItem(layout.Root, priority, layouts, items);
        }

        static void BoostItem(LayoutItem item, ImagePriority priority, HashSet<Layout> layouts, HashSet<LayoutItem> items)
        {
            if (item == null || !items.Add(item))
                return;

            switch (item)
            {
                case Image image:
                    Queue(image.Content, priority);
                    break;
                case Item single:
                    Queue(single.Data?.Icon, priority);
                    break;
                case ItemGrid grid:
                    if (grid.Data?.Rows != null)
                    {
                        foreach (var row in grid.Data.Rows)
                        {
                            if (row == null)
                                continue;
                            foreach (var cell in row)
                                Queue(cell?.Icon, priority);
                        }
                    }
                    break;
                case TabPanel panel:
                    if (panel.Tabs != null)
                    {
                        foreach (var tab in panel.Tabs)
                            Queue(tab?.Icon, priority);
                    }
                    BoostItem(panel.CurrentTab?.Content, priority, layouts, items);
                    return;
                case ButtonPopup popup:
                    Queue(popup.Image, priority);
                    return;
                case MapPanel map:
                    foreach (var entry in map.Maps)
                    {
                        Queue(entry?.Image, priority);
                        if (entry?.Locations == null)
                            continue;
                        foreach (var pin in entry.Locations)
                        {
                            if (pin?.Location?.BadgeItems == null)
                                continue;
                            foreach (var badge in pin.Location.BadgeItems)
                                Queue(badge?.Image, priority);
                        }
                    }
                    break;
                case RecentPinnedLocations pins:
                    foreach (var location in pins.Locations)
                        QueueLocation(location, priority);
                    break;
                case LastClearedLocation lastCleared:
                    QueueLocation(lastCleared.Location, priority);
                    break;
                case LayoutReference reference:
                    BoostLayout(reference.Layout, priority, layouts, items);
                    return;
            }

            foreach (var child in item.EnumerateChildren())
                BoostItem(child, priority, layouts, items);
        }

        static void QueueLocation(Location location, ImagePriority priority)
        {
            if (location == null)
                return;
            Queue(location.Thumbnail, priority);
            if (location.Sections != null)
            {
                foreach (var section in location.Sections)
                {
                    if (section == null)
                        continue;
                    Queue(section.Thumbnail, priority);
                    Queue(section.GateItem?.Icon, priority);
                    Queue(section.OpenChestImage, priority);
                    Queue(section.ClosedChestImage, priority);
                    Queue(section.UnavailableOpenChestImage, priority);
                    Queue(section.UnavailableClosedChestImage, priority);
                }
            }
            if (location.BadgeItems == null)
                return;
            foreach (var badge in location.BadgeItems)
                Queue(badge?.Image, priority);
        }

        static void Queue(ImageReference image, ImagePriority priority)
        {
            if (image == null)
                return;
            ImageReferenceService.Instance.QueueResolution(image, priority);
        }
    }
}
