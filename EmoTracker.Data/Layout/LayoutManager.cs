using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

// Phase 6 step 11: LayoutManager's ScriptManager.Instance accesses are
// all pure logging.
#pragma warning disable CS0618

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 6 step 5: <see cref="LayoutManager"/> is now a regular
    /// instantiable <see cref="ObservableObject"/> (was
    /// <c>ObservableSingleton&lt;T&gt;</c>) so each <c>TrackerState</c>
    /// holds one. <see cref="Instance"/> aliases <see cref="Current"/>
    /// for the existing 20 callsites.
    /// </summary>
    public class LayoutManager : ObservableObject
    {
        // ---- Static current-instance plumbing (replaces ObservableSingleton<T>) ----

        static LayoutManager mCurrent;
        [System.Obsolete("Phase 6 step 11: prefer (this.OwnerState as TrackerState)?.Layouts for ModelTypeBase holders, or Sessions.SessionContext.ActiveState?.Layouts / ApplicationModel.Instance.PrimaryState?.Layouts otherwise.")]
        public static LayoutManager Current
        {
            get
            {
                if (mCurrent == null)
                    mCurrent = new LayoutManager();
                return mCurrent;
            }
        }
        [System.Obsolete("Phase 6 step 11: state-aware code installs the active state via TrackerState's catalog adoption rather than reassigning Current.")]
        public static void SetCurrent(LayoutManager manager) => mCurrent = manager;
        [System.Obsolete("Phase 6 step 11: prefer (this.OwnerState as TrackerState)?.Layouts for ModelTypeBase holders, or Sessions.SessionContext.ActiveState?.Layouts / ApplicationModel.Instance.PrimaryState?.Layouts otherwise.")]
        public static LayoutManager Instance
        {
            get
            {
                // file-level CS0618 disable covers the access here.
                return Current;
            }
        }

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
            ScriptManager.Instance.Output("Loading Layouts: {0}", path);
            using (new LoggingBlock())
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
                                        layout.Load(content, package);

                                        if (layout.Root != null)
                                            mKeyToLayout[name] = layout;
                                    }
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
                    return false;
                }
            }
        }

        public bool LegacyLoad(IGamePackage package)
        {
            if(package == null) { return false; }

            ScriptManager.Instance.OutputWarning("Loading legacy layout data");
            using (new LoggingBlock())
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
                                        layout.Load(content, package);

                                        if (layout.Root != null)
                                            mKeyToLayout[name] = layout;
                                    }
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
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
