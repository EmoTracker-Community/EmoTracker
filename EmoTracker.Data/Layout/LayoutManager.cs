using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 7.1: <see cref="LayoutManager"/> is per-state. Each
    /// <c>TrackerState</c> owns one. Reach via the holder's
    /// <see cref="ModelTypeBase.OwnerState"/>, or via
    /// <c>ApplicationModel.Instance.PrimaryState.Layouts</c> /
    /// <c>Sessions.SessionContext.ActiveState.Layouts</c>.
    /// </summary>
    public class LayoutManager : ObservableObject
    {
        // Phase 6 step 11: back-reference to the owning TrackerState.
        internal Sessions.TrackerState State { get; set; }

        Dictionary<string, LayoutItem> mUidToLayoutItem = new Dictionary<string, LayoutItem>();
        Dictionary<string, Layout> mKeyToLayout = new Dictionary<string, Layout>();

        public Layout FindLayout(string key)
        {
            Layout result = null;
            if (mKeyToLayout.TryGetValue(key, out result))
                return result;

            return null;
        }

        public LayoutItem FindElement(string uid)
        {
            LayoutItem result = null;
            if (mUidToLayoutItem.TryGetValue(uid, out result))
                return result;

            return null;
        }

        public void Clear()
        {
            mUidToLayoutItem.Clear();

            foreach (var layout in mKeyToLayout.Values)
            {
                layout.Dispose();
            }

            mKeyToLayout.Clear();
        }

        /// <summary>
        /// Phase 6 step 8: registers a forked layout under
        /// <paramref name="key"/> in this manager's lookup. Used by
        /// <c>TrackerState.Fork()</c>'s coordinated walk.
        /// </summary>
        internal void AddLayoutFromFork(string key, Layout layout)
        {
            if (string.IsNullOrEmpty(key) || layout == null) return;
            mKeyToLayout[key] = layout;
        }

        /// <summary>
        /// Phase 6 step 8: enumerates the (key, layout) pairs registered
        /// in this manager so the fork walk can iterate them.
        /// </summary>
        internal IEnumerable<KeyValuePair<string, Layout>> GetLayoutsForFork()
        {
            return mKeyToLayout;
        }

        public bool IncrementalLoad(string path, IGamePackage package)
        {
            this.State?.Scripts.Output("Loading Layouts: {0}", path);
            using (new LoggingBlock(this.State?.Scripts))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(package.Open(path)))
                    {
                        JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                        if (root != null)
                        {
                            foreach (var key in root.Properties())
                            {
                                string name = key.Name;
                                if (!mKeyToLayout.ContainsKey(name))
                                {
                                    JObject content = root.GetValue<JObject>(name);
                                    if (content != null)
                                    {
                                        Layout layout = new Layout();
                                        layout.OwnerState = this.State;
                                        layout.Load(content, package);

                                        if (layout.Root != null)
                                            mKeyToLayout[name] = layout;
                                        // Register the freshly-parsed layout in the
                                        // owning state's resolver so cross-state
                                        // references find it (per the construction-time
                                        // OwnerState contract).
                                        this.State?.RegisterModel(layout);
                                    }
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    this.State?.Scripts.OutputException(e);
                    return false;
                }
            }
        }

        public bool LegacyLoad(IGamePackage package)
        {
            if(package == null) { return false; }

            this.State?.Scripts.OutputWarning("Loading legacy layout data");
            using (new LoggingBlock(this.State?.Scripts))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(package.Open("tracker_layout.json")))
                    {
                        JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                        JObject layouts = root.GetValue<JObject>("layouts");
                        if (layouts != null)
                        {
                            foreach (var key in layouts.Properties())
                            {
                                string name = key.Name;
                                if (!mKeyToLayout.ContainsKey(name))
                                {
                                    JObject content = layouts.GetValue<JObject>(name);
                                    if (content != null)
                                    {
                                        Layout layout = new Layout();
                                        layout.OwnerState = this.State;
                                        layout.Load(content, package);

                                        if (layout.Root != null)
                                            mKeyToLayout[name] = layout;
                                        // Register the freshly-parsed layout in the
                                        // owning state's resolver so cross-state
                                        // references find it (per the construction-time
                                        // OwnerState contract).
                                        this.State?.RegisterModel(layout);
                                    }
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    this.State?.Scripts.OutputException(e);
                    return false;
                }
            }
        }


        internal void RegisterLayoutItemForUID(string uid, LayoutItem layoutItem)
        {
            if (!string.IsNullOrWhiteSpace(uid))
            {
                if (layoutItem != null)
                    mUidToLayoutItem[uid] = layoutItem;
            }
        }
    }
}
