using EmoTracker.Core;
using EmoTracker.Data.Items;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using EmoTracker.Data.Session;

namespace EmoTracker.Data
{
    public class ItemDatabase : ObservableObject, ICodeProvider
    {
        ObservableCollection<ITrackableItem> mItems = new ObservableCollection<ITrackableItem>();
        Dictionary<ITrackableItem, int> mItemIndex = new Dictionary<ITrackableItem, int>();

        // Code→provider index: maps lowercase code strings to lists of items that can provide them.
        // LuaItems have dynamic code providers (Lua callbacks) and cannot be statically indexed,
        // so they are kept in a separate list and brute-force checked as a fallback.
        Dictionary<string, List<ITrackableItem>> mCodeToProviders = new Dictionary<string, List<ITrackableItem>>(StringComparer.OrdinalIgnoreCase);
        List<ITrackableItem> mDynamicCodeItems = new List<ITrackableItem>();
        bool mCodeIndexBuilt = false;

        // Phase 3 of the TrackerSession refactor: ItemDatabase now hosts both the
        // immutable Catalog (item identities + lookup indices) and the mutable
        // session-owned StateStore (per-item runtime property dictionaries that
        // TransactableObject reads/writes through). The State store is what makes
        // future Fork() cheap — clone it and you get an independent set of item
        // values without touching the catalog or recreating the item objects.
        readonly ItemCatalog mCatalog;
        readonly ItemStateStore mStates = new ItemStateStore();

        public IEnumerable<ITrackableItem> Items
        {
            get { return mItems; }
        }

        /// <summary>
        /// Read-only view of the registered items (the immutable half of the
        /// item Catalog/State split).
        /// </summary>
        public ItemCatalog Catalog => mCatalog;

        /// <summary>
        /// Per-session mutable property store backing every item's transactable
        /// properties. Owned by the database in Phase 3; will move to direct
        /// session ownership in a later phase along with the rest of the per-
        /// session state.
        /// </summary>
        public ItemStateStore States => mStates;

        public ItemDatabase()
        {
            mCatalog = new ItemCatalog(mItems);
        }

        public void Reset()
        {
            foreach (var item in mItems)
            {
                if (item != null)
                    item.Dispose();
            }

            mItems.Clear();
            mItemIndex.Clear();
            mCodeToProviders.Clear();
            mDynamicCodeItems.Clear();
            mCodeIndexBuilt = false;
            mStates.Reset();
        }

        /// <summary>
        /// Builds the code→provider lookup index. Should be called once after all items are loaded.
        /// Items that return null from GetAllProvidedCodes (e.g. LuaItem) are placed in the
        /// dynamic fallback list and checked via brute-force on every query.
        /// </summary>
        public void BuildCodeIndex()
        {
            mCodeToProviders.Clear();
            mDynamicCodeItems.Clear();

            foreach (var item in mItems)
            {
                if (item is ItemBase itemBase)
                {
                    var codes = itemBase.GetAllProvidedCodes();
                    if (codes == null)
                    {
                        // Dynamic code provider (e.g. LuaItem) — must be brute-force checked
                        mDynamicCodeItems.Add(item);
                    }
                    else
                    {
                        foreach (string code in codes)
                        {
                            string key = code;
                            if (!mCodeToProviders.TryGetValue(key, out var list))
                            {
                                list = new List<ITrackableItem>();
                                mCodeToProviders[key] = list;
                            }
                            list.Add(item);
                        }
                    }
                }
                else
                {
                    // Non-ItemBase implementors — treat as dynamic
                    mDynamicCodeItems.Add(item);
                }
            }

            mCodeIndexBuilt = true;
        }

        public void RegisterItem(ITrackableItem item)
        {
            if (!mItemIndex.ContainsKey(item))
            {
                mItemIndex[item] = mItems.Count;
                mItems.Add(item);
            }
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
                TrackerSession.Current.Scripts.OutputWarning("Loading legacy items");
            else
                TrackerSession.Current.Scripts.Output("Loading Items: {0}", path);

            using (new LoggingBlock())
            {
                try
                {
                    using (new LocationDatabase.SuspendRefreshScope())
                    {
                        using (StreamReader reader = new StreamReader(package.Open(path)))
                        {
                            JArray items = (JArray)JToken.ReadFrom(new JsonTextReader(reader));
                            foreach (JObject item in items)
                            {
                                ITrackableItem instance = ItemBase.CreateItem(item, package);
                                if (instance != null)
                                {
                                    mItemIndex[instance] = mItems.Count;
                                    mItems.Add(instance);
                                }
                            }
                        }

                        bSuccess = true;
                    }
                }
                catch (Exception e)
                {
                    TrackerSession.Current.Scripts.OutputException(e);
                }
            }

            return bSuccess;
        }

        internal bool CodeIsProvided(string code)
        {
            if (mCodeIndexBuilt)
            {
                // Check indexed items
                if (mCodeToProviders.TryGetValue(code, out var indexed))
                {
                    foreach (var item in indexed)
                    {
                        if (item.ProvidesCode(code) > 0)
                            return true;
                    }
                }

                // Check dynamic items (LuaItems)
                foreach (var item in mDynamicCodeItems)
                {
                    if (item.ProvidesCode(code) > 0)
                        return true;
                }

                return false;
            }

            // Fallback: no index built yet
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
            //  Item codes never constrain accessibility
            maxAccessibilityLevel = AccessibilityLevel.Normal;

            if (mCodeIndexBuilt)
            {
                uint nCount = 0;

                // Check indexed items first
                if (mCodeToProviders.TryGetValue(code, out var indexed))
                {
                    foreach (var item in indexed)
                        nCount += item.ProvidesCode(code);
                }

                // Check dynamic items (LuaItems)
                foreach (var item in mDynamicCodeItems)
                    nCount += item.ProvidesCode(code);

                return nCount;
            }

            // Fallback: no index built yet
            {
                uint nCount = 0;
                foreach (ITrackableItem item in Items)
                    nCount += item.ProvidesCode(code);
                return nCount;
            }
        }

        internal ITrackableItem FindProvidingItemForCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            if (mCodeIndexBuilt)
            {
                if (mCodeToProviders.TryGetValue(code, out var indexed))
                {
                    foreach (var item in indexed)
                    {
                        if (item.CanProvideCode(code))
                            return item;
                    }
                }

                foreach (var item in mDynamicCodeItems)
                {
                    if (item.CanProvideCode(code))
                        return item;
                }

                return null;
            }

            foreach (ITrackableItem item in Items)
            {
                if (item.CanProvideCode(code))
                    return item;
            }

            return null;
        }

        public ITrackableItem[] FindProvidingItemsForCode(string code)
        {
            List<ITrackableItem> found = new List<ITrackableItem>();

            if (string.IsNullOrWhiteSpace(code))
                return found.ToArray();

            if (mCodeIndexBuilt)
            {
                if (mCodeToProviders.TryGetValue(code, out var indexed))
                {
                    foreach (var item in indexed)
                    {
                        if (item.CanProvideCode(code))
                            found.Add(item);
                    }
                }

                foreach (var item in mDynamicCodeItems)
                {
                    if (item.CanProvideCode(code))
                        found.Add(item);
                }

                return found.ToArray();
            }

            foreach (ITrackableItem item in Items)
            {
                if (item.CanProvideCode(code))
                    found.Add(item);
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
                    TrackerSession.Current.Scripts.OutputException(e);
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
                            TrackerSession.Current.Scripts.OutputWarning("Item index consistency issue found (and allowed) for reference \"{0}\" while loading a save. Ensure that items are loaded/created in a consistent order.", itemData.GetValue<string>("item_reference"));
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
            if (!mItemIndex.TryGetValue(item, out int idx))
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
