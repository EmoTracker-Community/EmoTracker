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

            public override ModelTypeBase Fork()
            {
                var copy = new Tab();
                copy.InitializeAsForkOf(this);
                if (this.mContent != null)
                    copy.mContent = (LayoutItem)this.mContent.Fork();
                return copy;
            }
        }

        ObservableCollection<Tab> mTabs = new ObservableCollection<Tab>();

        public IEnumerable<Tab> Tabs
        {
            get { return mTabs; }
        }

        // CurrentTab is pure per-state runtime state (no definition default).
        // Held as a private Tab reference rather than via the KV store —
        // the Tab itself is a per-state owned instance, so storing it in
        // MutableData would invite IDeepCopyable-on-Tab questions for no
        // benefit. On Fork we rewire CurrentTab to the fork's same-index Tab.
        Tab mCurrentTab;
        public Tab CurrentTab
        {
            get { return mCurrentTab; }
            set { SetProperty(ref mCurrentTab, value); }
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
                    LayoutItem layout = CreateLayoutItem(entry.GetValue<JObject>("content"), package);
                    if (layout != null)
                    {
                        var tab = new Tab();
                        tab.SeedDefinition(
                            entry.GetValue<string>("title"),
                            ImageReference.FromPackRelativePath(package, entry.GetValue<string>("icon"), entry.GetValue<string>("icon_image_spec")),
                            layout);
                        mTabs.Add(tab);
                    }
                }

                CurrentTab = mTabs.FirstOrDefault();
            }

            return true;
        }

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = new TabPanel();
            copy.InitializeAsForkOf(this);

            int currentIdx = -1;
            for (int i = 0; i < this.mTabs.Count; i++)
            {
                if (ReferenceEquals(this.mTabs[i], this.mCurrentTab))
                    currentIdx = i;

                var forked = (Tab)this.mTabs[i].Fork();
                copy.mTabs.Add(forked);
            }

            // Rewire CurrentTab to the fork's same-index tab (pre-Phase-4
            // semantics: first tab is current after parse, the user may have
            // switched at runtime — preserve that runtime selection).
            if (currentIdx >= 0 && currentIdx < copy.mTabs.Count)
                copy.mCurrentTab = copy.mTabs[currentIdx];

            return copy;
        }
    }
}
