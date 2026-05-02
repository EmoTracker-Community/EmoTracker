using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("static")]
    public partial class StaticItem : ItemBase
    {
        // Definition data: parsed once at pack-load.
        CodeProvider mCodeProvider = new CodeProvider();

        public StaticItem()
        {
            Capturable = false;
        }

        public StaticItem(EmoTracker.Core.DataModel.ITrackerStateContext state)
        {
            Capturable = false;
            OwnerState = state;
        }

        public override bool CanProvideCode(string code)
        {
            return mCodeProvider.ProvidesCode(code);
        }

        public override IEnumerable<string> GetAllProvidedCodes() => mCodeProvider.ProvidedCodes;

        public override void OnLeftClick()
        {
        }

        public override void OnRightClick()
        {
        }

        public override uint ProvidesCode(string code)
        {
            if (mCodeProvider.ProvidesCode(code))
                return 1;

            return 0;
        }

        public override void AdvanceToCode(string code = null)
        {
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            Icon = ImageReference.FromPackRelativePath(package, data.GetValue<string>("img"), data.GetValue<string>("img_mods"));
            mCodeProvider.AddCodes(data.GetValue<string>("codes"));
            Capturable = data.GetValue<bool>("capturable", false);
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (StaticItem)source;
            mCodeProvider = src.mCodeProvider;
        }
    }
}
