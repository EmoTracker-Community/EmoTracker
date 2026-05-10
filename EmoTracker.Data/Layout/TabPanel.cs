using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: <see cref="TabPanel"/> owns a per-state collection of
    /// <see cref="Tab"/> instances (each itself a <see cref="ModelTypeBase"/>),
    /// plus a <see cref="CurrentTab"/> reference that selects which one is
    /// currently visible. On <see cref="Fork"/> the tab list is forked
    /// element-by-element (parallel order); <see cref="CurrentTab"/> is rewired
    /// to the fork's tab at the same index.
    /// </summary>
    [JsonTypeTags("tabbed")]
    public partial class TabPanel : LayoutItem
    {
        /// <summary>
        /// A single tab page within a <see cref="TabPanel"/>. Title and icon
        /// are <c>[KVOverridable]</c>; Content is an owned single-child layout
        /// forked explicitly on <see cref="Fork"/>.
        /// </summary>
        public partial class Tab : ModelTypeBase
        {
            [KVOverridable]
            public partial string Title { get; set; }

            [KVOverridable]
            public partial ImageReference Icon { get; set; }

            // Content is the owned LayoutItem subtree — held as a private field,
            // forked explicitly. Not in MutableData (LayoutItem is reference-typed
            // and represents an owning relationship, not a cross-reference).
            LayoutItem mContent;
            public LayoutItem Content
            {
                get { return mContent; }
                set { SetProperty(ref mContent, value); }
            }

            public Tab()
            {
            }

            public override void Dispose()
            {
                DisposeObjectAndDefault(ref mContent);
                base.Dispose();
            }

            /// <summary>
            /// Construction-time seeding: writes the parsed values into
            /// <see cref="ModelTypeBase.ImmutableData"/> under the <c>__def</c>
            /// keys, mirroring the LayoutItem parse pattern at this nested
            /// scope. Called from <see cref="TabPanel.TryParseInternal"/>.
            /// </summary>
            internal void SeedDefinition(string title, ImageReference icon, LayoutItem content)
            {
                var def = new Dictionary<string, object>
                {
                    { DefinitionIdKey, this.DefinitionId },
                    { nameof(Title) + "__def", title },
                    { nameof(Icon) + "__def", icon },
                };
                this.ImmutableData = new ImmutableKeyValueStore(def);
                this.mContent = content;
            }

            public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
            {
                if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
                var copy = (Tab)System.Activator.CreateInstance(this.GetType());
                copy.OwnerState = destOwnerState;
                copy.InitializeAsForkOf(this);
                if (this.mContent != null)
                    copy.mContent = (LayoutItem)this.mContent.Fork(destOwnerState);
                return copy;
            }
        }

        [KVOverridable]
        public partial HorizontalAlignment TabStripHorizontalAlignment { get; set; }

        protected override void PopulateDefinitionData(Newtonsoft.Json.Linq.JObject data, IGamePackage package, System.Collections.Generic.Dictionary<string, object> definition)
        {
            definition[nameof(TabStripHorizontalAlignment) + "__def"] = data.GetEnumValue<HorizontalAlignment>("tabstrip_h_alignment", HorizontalAlignment.Center);
        }

        ObservableCollection<Tab> mTabs = new ObservableCollection<Tab>();

        public IEnumerable<Tab> Tabs
        {
            get { return mTabs; }
        }

        // CurrentTab is pure per-state runtime state — the user's currently-
        // selected tab in this state, with no parse-time definition default.
        // Stored by DefinitionId in MutableData (matching the Phase 3
        // Section.HostedItemId pattern) and exposed through a hand-written
        // CurrentTab accessor that resolves the id through this state's
        // mTabs collection.
        //
        // Why Guid-by-DefinitionId instead of storing the Tab reference?
        //
        // 1. Tab is a ModelTypeBase reference type that doesn't (and
        //    shouldn't) implement IDeepCopyable, so the per-key COW boundary
        //    won't accept it directly via [KVMutable] partial Tab.
        // 2. The Guid roundtrip means CurrentTabId carries through Fork via
        //    per-key COW automatically — no explicit rewire in Fork(). The
        //    fork's CurrentTab getter resolves the inherited Guid through
        //    its own mTabs and returns the matching fork-side Tab.
        // 3. Tab order isn't an invariant the design needs to lean on; the
        //    DefinitionId lookup is order-independent (future per-state tab
        //    insertion / reordering wouldn't break the selection).
        // 4. Upgrade path: swapping [KVMutable] for [KVTransactable] makes
        //    tab selection undoable without restructuring the storage.
        [KVMutable]
        [DependentProperty(nameof(CurrentTab))]
        public partial System.Guid CurrentTabId { get; set; }

        public Tab CurrentTab
        {
            get
            {
                var id = CurrentTabId;
                if (id == System.Guid.Empty) return null;
                foreach (var tab in mTabs)
                    if (tab.DefinitionId == id) return tab;
                return null;
            }
            set { CurrentTabId = (value != null) ? value.DefinitionId : System.Guid.Empty; }
        }

        public override void Dispose()
        {
            DisposeCollection(mTabs);
            mTabs.Clear();
            base.Dispose();
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mTabs.Clear();

            JArray tabList = data.GetValue<JArray>("tabs");
            if (tabList != null)
            {
                foreach (JObject entry in tabList)
                {
                    LayoutItem layout = CreateLayoutItem(entry.GetValue<JObject>("content"), package, this.OwnerState);
                    if (layout != null)
                    {
                        var tab = new Tab();
                        tab.OwnerState = this.OwnerState;
                        tab.SeedDefinition(
                            entry.GetValue<string>("title"),
                            ImageReference.FromPackRelativePath((this.OwnerState as Sessions.TrackerState)?.PackageInstance, entry.GetValue<string>("icon"), entry.GetValue<string>("icon_image_spec")),
                            layout);
                        mTabs.Add(tab);
                    }
                }

                CurrentTab = mTabs.FirstOrDefault();
            }

            return true;
        }

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = (TabPanel)System.Activator.CreateInstance(this.GetType());
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);

            // Fork the owned mTabs subtree. CurrentTabId is inherited via
            // per-key COW on MutableData and resolves through the fork's
            // own mTabs (since each forked Tab carries the same DefinitionId
            // as its source counterpart) — no explicit selection rewire needed.
            foreach (var tab in this.mTabs)
                copy.mTabs.Add((Tab)tab.Fork(destOwnerState));

            return copy;
        }

        public override IEnumerable<LayoutItem> EnumerateChildren()
        {
            foreach (var t in mTabs)
            {
                if (t.Content != null) yield return t.Content;
            }
        }
    }
}
