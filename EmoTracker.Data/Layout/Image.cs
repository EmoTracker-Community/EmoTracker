using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("image")]
    public partial class Image : LayoutItem
    {
        // ImageReference is treated as logically immutable per Phase 2 §2.6 — its
        // IDeepCopyable.DeepCopy() returns this. So the per-key COW boundary
        // shares the reference safely across forks.
        [KVOverridable]
        public partial ImageReference Content { get; set; }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            definition[nameof(Content) + "__def"] = ImageReference.FromPackRelativePath(
                package, data.GetValue<string>("image"), data.GetValue<string>("image_filter"));
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            return true;
        }
    }
}
