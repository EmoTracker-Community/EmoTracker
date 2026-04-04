using EmoTracker.Core;
using EmoTracker.Data.Items;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace EmoTracker.Data
{
    public class ItemDatabase : Singleton<ItemDatabase>, ICodeProvider
    {
        ObservableCollection<ITrackableItem> mItems = new ObservableCollection<ITrackableItem>();

        public IEnumerable<ITrackableItem> Items
        {
            get { return mItems; }
        }

        public ItemDatabase()
        {
        }

        public void Reset()
        {
            foreach (var item in mItems)
            {
                if (item != null)
                    item.Dispose();
            }

            mItems.Clear();            
        }

        public void RegisterItem(ITrackableItem item)
        {
            if (!mItems.Contains(item))
                mItems.Add(item);
        }

        public bool LegacyLoad(IGamePackage package)
        {
            //  Do not load legacy data if we already have new-style data
            if (mItems.Count > 0)
                return true;

            //  This is to support legacy-packages only
            return IncrementalLoad("items.json", package, true);
        }

        public bool IncrementalLoad(string path, IGamePackage package, bool bLegacy = false)
        {
            bool bSuccess = false;

            if (bLegacy)
                ScriptManager.Instance.OutputWarning("Loading legacy items");
            else
                ScriptManager.Instance.Output("Loading Items: {0}", path);

            using (new LoggingBlock())
            {
                try
                {
                    LocationDatabase.Instance.SuspendRefresh = true;

                    using (StreamReader reader = new StreamReader(package.Open(path)))
                    {
                        JArray items = (JArray)JToken.ReadFrom(new JsonTextReader(reader));
                        foreach (JObject item in items)
                        {
                            ITrackableItem instance = ItemBase.CreateItem(item, package);
                            if (instance != null)
                                mItems.Add(instance);
                        }
                    }

                    bSuccess = true;
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
                }
                finally
                {
                    LocationDatabase.Instance.SuspendRefresh = false;
                }
            }

            return bSuccess;
        }

        internal bool CodeIsProvided(string code)
        {
            foreach (ITrackableItem item in Items)
            {
                if (item.ProvidesCode(code) > 0)
                    return true;
            }

            return false;
        }

        public object FindObjectForCode(string code)
        {
            return FindProvidingItemForCode(code);
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibilityLevel)
        {
            code = code.ToLower();

            //  Item codes never constrain accessibility
            maxAccessibilityLevel = AccessibilityLevel.Normal;

            uint nCount = 0;
            foreach (ITrackableItem item in Items)
            {
                nCount += item.ProvidesCode(code);
            }

            return nCount;
        }

        internal ITrackableItem FindProvidingItemForCode(string code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                foreach (ITrackableItem item in Items)
                {
                    if (item.CanProvideCode(code))
                        return item;
                }
            }

            return null;
        }

        public ITrackableItem[] FindProvidingItemsForCode(string code)
        {
            List<ITrackableItem> found = new List<ITrackableItem>();

            if (!string.IsNullOrWhiteSpace(code))
            {
                foreach (ITrackableItem item in Items)
                {
                    if (item.CanProvideCode(code))
                        found.Add(item);
                }
            }

            return found.ToArray();
        }

        internal void Save(JObject root)
        {
            JArray itemDataArray = new JArray();
            foreach (ITrackableItem item in mItems)
            {
                JObject itemData = new JObject();
                itemData["item_reference"] = GetPersistableItemReference(item);

                try
                {
                    if (item.Save(itemData))
                        itemDataArray.Add(itemData);
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
                }
            }

            if (itemDataArray.Count > 0)
                root["item_database"] = itemDataArray;
        }

        internal bool Load(JObject root)
        {
            JArray itemDataArray = root.GetValue<JArray>("item_database");
            if (itemDataArray != null)
            {
                foreach (JObject itemData in itemDataArray)
                {
                    ITrackableItem item = ResolvePersistableItemReference(itemData.GetValue<string>("item_reference"));
                    if (item == null)
                    {
                        item = ResolvePersistableItemReference(itemData.GetValue<string>("item_reference"), true);
                        if (item != null)
                        {
                            ScriptManager.Instance.OutputWarning("Item index consistency issue found (and allowed) for reference \"{0}\" while loading a save. Ensure that items are loaded/created in a consistent order.", itemData.GetValue<string>("item_reference"));
                        }

                        if (item == null)
                            return false;
                    }

                    if (!item.Load(itemData))
                        return false;
                }
            }

            return true;
        }

        public string GetPersistableItemReference(ITrackableItem item, bool allowAnyType = false)
        {
            int idx = mItems.IndexOf(item);

            if (idx < 0)
                throw new InvalidOperationException("Cannot generate persistable reference for item that is not in the ItemDatabase");

            string jsonTypeTag = JsonTypeTagsAttribute.GetDefaultTagForType(item.GetType());
            if (string.IsNullOrWhiteSpace(jsonTypeTag))
                throw new InvalidOperationException("Cannot generate persistable reference for item that does not provide at least one json type tag");

            if (!string.IsNullOrWhiteSpace(item.Name))
                return string.Format("{0}:{1}:{2}", idx, allowAnyType ? "*" : Uri.EscapeDataString(jsonTypeTag), Uri.EscapeDataString(item.Name));
            else
                return string.Format("{0}:{1}", idx, allowAnyType ? "*" : Uri.EscapeDataString(jsonTypeTag));
        }

        private ITrackableItem FindItemByName(string name)
        {
            foreach (ITrackableItem item in Items)
            {
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        public ITrackableItem ResolvePersistableItemReference(string reference, bool allowIndexMismatch = false)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            string[] tokens = reference.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return null;

            int idx = -1;
            if (!int.TryParse(tokens[0], out idx) || idx < 0 || idx > (mItems.Count - 1))
                return null;

            ITrackableItem item = mItems[idx];

            if (!string.Equals(tokens[1], "*") && !JsonTypeTagsAttribute.GetTypeSupportsTag(item.GetType(), Uri.UnescapeDataString(tokens[1])))
                return null;

            if (item != null && tokens.Length >= 3)
            {
                if (!string.Equals(Uri.UnescapeDataString(tokens[2]), item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (allowIndexMismatch)
                    {
                        return FindItemByName(Uri.UnescapeDataString(tokens[2]));
                    }
                    
                    return null;
                }
            }

            return item;
        }
    }
}
