using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("group")]
    public partial class GroupBox : Container
    {
        [KVOverridable]
        public partial string Header { get; set; }

        [KVOverridable]
        public partial string HeaderBackground { get; set; }

        // HeaderContent is an owned single-child LayoutItem. Held as a private
        // field — owned subtree — and forked by the local Fork override.
        LayoutItem mHeaderContent;
        public LayoutItem HeaderContent
        {
            get { return mHeaderContent; }
            set { SetProperty(ref mHeaderContent, value); }
        }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            base.PopulateDefinitionData(data, package, definition);
            definition[nameof(Header) + "__def"] = data.GetValue<string>("header", null);
            definition[nameof(HeaderBackground) + "__def"] = data.GetValue<string>("header_background", "#212121");

            // Pack didn't specify a background? Apply the GroupBox-level default.
            // This is a definition-time decision — same observable effect as the
            // pre-Phase-4 "if (!OverrideBackground) Background = ..." sequencing,
            // but applied to ImmutableData so forks all inherit the same default.
            object background;
            if (!definition.TryGetValue(nameof(Background) + "__def", out background) || string.IsNullOrEmpty(background as string))
            {
                definition[nameof(Background) + "__def"] = "#66212121";
            }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            if (!base.TryParseInternal(data, package))
                return false;

            JObject headerContentAsObject = data.GetValue<JObject>("header_content");
            if (headerContentAsObject != null)
                HeaderContent = CreateLayoutItem(headerContentAsObject, package);

            return true;
        }

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            // Container.Fork uses Activator.CreateInstance(this.GetType()),
            // so calling base.Fork() returns a fully-set-up GroupBox with
            // ImmutableData + COW MutableData wired and the Items collection
            // already forked. We just need to fork the GroupBox-specific
            // owned subtree (HeaderContent) on top.
            var copy = (GroupBox)base.Fork();
            if (this.mHeaderContent != null)
            {
                copy.mHeaderContent = (LayoutItem)this.mHeaderContent.Fork();
            }
            return copy;
        }
    }
}
