using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("static")]
    public class StaticItem : ItemBase
    {
        CodeProvider mCodeProvider = new CodeProvider();

        public StaticItem()
        {
            Capturable = false;
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
    }
}
