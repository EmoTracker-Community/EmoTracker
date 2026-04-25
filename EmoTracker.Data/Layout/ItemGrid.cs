using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: thin layout wrapper around <see cref="Data.Items.ItemGrid"/>.
    /// The wrapped <c>Items.ItemGrid</c> configuration object is held as a
    /// private field (definition data populated at parse time, shared across
    /// forks by reference for now). A subsequent commit will convert
    /// <see cref="Data.Items.ItemGrid"/> onto <see cref="ModelTypeBase"/>
    /// itself; until then this layout wrapper just carries the reference.
    /// </summary>
    [JsonTypeTags("itemgrid")]
    public partial class ItemGrid : LayoutItem
    {
        private static Version mMarginFixVersion = new Version("1.0");

        // Wrapped grid config — held as a private field. It is parse-time
        // populated; runtime mutations would mutate the shared instance until
        // Items.ItemGrid is itself converted onto v2 in a follow-up commit.
        Data.Items.ItemGrid mItemGrid = new Data.Items.ItemGrid();

        public Data.Items.ItemGrid Data
        {
            get { return mItemGrid; }
            set { SetProperty(ref mItemGrid, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mItemGrid.Clear();
            mItemGrid.Load(data);

            //  Process legacy margin settings for old packages
            if (package.LayoutEngineVersion == null || package.LayoutEngineVersion < mMarginFixVersion)
                mItemGrid.LegacyMargin = data.GetValue<string>("margin", "5");

            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ItemGrid)source;
            // Share the wrapped Data.Items.ItemGrid by reference — the inner
            // type is not yet ModelTypeBase-derived, so per-state forking of
            // its contents is deferred. Behavior parity with pre-Phase-4
            // (singleton-shared item references) is preserved.
            mItemGrid = src.mItemGrid;
        }
    }
}
