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
    public class LayoutManager : ObservableSingleton<LayoutManager>
    {
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
