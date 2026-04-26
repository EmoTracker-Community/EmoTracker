using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Items
{
    public class ItemList : ObservableCollection<ITrackableItem>
    {
    };

    /// <summary>
    /// Phase 7 polish: <see cref="ItemGrid"/> stores its cell data as
    /// <see cref="ModelReference{T}"/>s into the holder layout's owning
    /// state — so per-fork resolution flows through the fork's
    /// <c>IModelResolver</c> just like every other cross-reference in the
    /// data model. Each cell's <c>ModelReference&lt;ITrackableItem&gt;</c>
    /// is bound to the wrapping <see cref="Layout.ItemGrid"/>; on fork
    /// the holder rebuilds its inner grid with refs bound to itself, so
    /// the fork's items panel renders the fork's own catalog.
    /// </summary>
    public class ItemGrid : ObservableObject
    {
        readonly ObservableCollection<ItemList> mRows = new ObservableCollection<ItemList>();
        readonly List<List<ModelReference<ITrackableItem>>> mRowRefs = new List<List<ModelReference<ITrackableItem>>>();

        public IEnumerable<ItemList> Rows => mRows;

        // Phase 7 polish: snapshot of the parsed item codes per row.
        // Used as the source of truth for fork-time rebuilds when the new
        // holder needs to construct its own ModelReferences.
        readonly List<List<string>> mRowCodes = new List<List<string>>();
        internal IReadOnlyList<IReadOnlyList<string>> RowCodes => mRowCodes;

        string mLegacyMargin;
        public string LegacyMargin
        {
            get { return mLegacyMargin; }
            set { SetProperty(ref mLegacyMargin, value); }
        }

        double mItemWidth = 32;
        public double ItemWidth
        {
            get { return mItemWidth; }
            set { SetProperty(ref mItemWidth, value); }
        }

        double mItemHeight = 32;
        public double ItemHeight
        {
            get { return mItemHeight; }
            set { SetProperty(ref mItemHeight, value); }
        }

        string mItemMargin;
        public string ItemMargin
        {
            get { return mItemMargin; }
            set { SetProperty(ref mItemMargin, value); }
        }

        double mBadgeFontSize = 12;
        public double BadgeFontSize
        {
            get { return mBadgeFontSize; }
            set { SetProperty(ref mBadgeFontSize, value); }
        }

        public void Clear()
        {
            mRows.Clear();
            mRowRefs.Clear();
            mRowCodes.Clear();
        }

        /// <summary>
        /// Parse + populate the grid against <paramref name="holder"/>.
        /// Codes are resolved through <paramref name="holder"/>'s owning
        /// state (or the ambient SessionContext if the holder isn't yet
        /// stamped at parse time). Each cell's resolved item is wrapped in
        /// a <see cref="ModelReference{T}"/> bound to <paramref name="holder"/>
        /// so subsequent reads route through the holder's resolver.
        /// </summary>
        public void Load(JObject data, ModelTypeBase holder)
        {
            ItemMargin = data.GetValue<string>("item_margin", "5");
            ItemWidth = mItemHeight = data.GetValue<double>("item_size", 32.0f);
            ItemWidth = data.GetValue<double>("item_width", ItemWidth);
            ItemHeight = data.GetValue<double>("item_height", ItemHeight);
            BadgeFontSize = data.GetValue<double>("badge_font_size", 12.0);
            LoadRows(data.GetValue<JArray>("rows"), holder);
        }

        void LoadRows(JArray data, ModelTypeBase holder)
        {
            mRowCodes.Clear();
            mRowRefs.Clear();
            mRows.Clear();
            if (data == null) return;

            var itemDb = (holder?.OwnerState as Sessions.TrackerState)?.Items
                         ?? Sessions.SessionContext.ActiveState?.Items;

            foreach (JArray rowData in data)
            {
                var rowCodes = new List<string>(rowData.Count);
                var rowRefs = new List<ModelReference<ITrackableItem>>(rowData.Count);
                var rowResolved = new ItemList();

                foreach (string code in rowData)
                {
                    rowCodes.Add(code);
                    var resolved = itemDb?.FindProvidingItemForCode(code);
                    var modelRef = resolved != null
                        ? new ModelReference<ITrackableItem>(holder, resolved)
                        : new ModelReference<ITrackableItem>(holder);
                    rowRefs.Add(modelRef);
                    rowResolved.Add(modelRef.Target);
                }

                mRowCodes.Add(rowCodes);
                mRowRefs.Add(rowRefs);
                mRows.Add(rowResolved);
            }
        }

        /// <summary>
        /// Phase 7 polish: rebuild this grid's rows under a new
        /// <paramref name="holder"/> (typically a forked <see cref="Layout.ItemGrid"/>).
        /// Copies the codes + metadata from <paramref name="source"/>, then
        /// constructs fresh <see cref="ModelReference{T}"/>s bound to
        /// <paramref name="holder"/>. The references' Targets resolve
        /// lazily through <paramref name="holder"/>'s OwnerState's resolver,
        /// which the caller stamps next.
        /// </summary>
        public void RebuildForFork(ItemGrid source, ModelTypeBase holder)
        {
            if (source == null) return;
            // Copy metadata.
            LegacyMargin = source.LegacyMargin;
            ItemMargin = source.ItemMargin;
            ItemWidth = source.ItemWidth;
            ItemHeight = source.ItemHeight;
            BadgeFontSize = source.BadgeFontSize;

            // Copy codes.
            mRowCodes.Clear();
            foreach (var row in source.mRowCodes)
                mRowCodes.Add(new List<string>(row));

            // Build empty refs bound to the new holder. The visible Rows
            // is left EMPTY here — the fork's OwnerState hasn't been
            // stamped yet, so resolving Targets now would return the
            // source state's items via the ambient
            // <see cref="ModelResolver.Current"/> fallback. The fork
            // pipeline calls <see cref="ResolveTargets"/> after stamping
            // OwnerState; at that point the refs resolve correctly through
            // the fork's own resolver.
            mRowRefs.Clear();
            mRows.Clear();
            foreach (var srcRow in source.mRowRefs)
            {
                var newRow = new List<ModelReference<ITrackableItem>>(srcRow.Count);
                foreach (var srcRef in srcRow)
                {
                    // Carry the DefinitionId via ForFork — same identity,
                    // bound to the new holder, fresh cache.
                    newRow.Add(srcRef.ForFork(holder));
                }
                mRowRefs.Add(newRow);
            }
        }

        /// <summary>
        /// Phase 7 polish: re-evaluate every cell's ModelReference.Target
        /// and refresh the visible <see cref="Rows"/>. Called by the fork
        /// pipeline once the holder's OwnerState is stamped.
        /// </summary>
        public void ResolveTargets()
        {
            mRows.Clear();
            foreach (var refRow in mRowRefs)
            {
                var resolved = new ItemList();
                foreach (var modelRef in refRow)
                    resolved.Add(modelRef.Target);
                mRows.Add(resolved);
            }
            NotifyPropertyChanged(nameof(Rows));
        }
    }
}
