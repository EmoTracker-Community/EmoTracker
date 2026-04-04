using EmoTracker.Core;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("blank")]
    public class BlankItem : ItemBase
    {
        public BlankItem()
        {
            Capturable = false;
        }

        public override bool CanProvideCode(string code)
        {
            return false;
        }

        public override void OnLeftClick()
        {
        }

        public override void OnRightClick()
        {
        }

        public override uint ProvidesCode(string code)
        {
            return 0;
        }

        public override void AdvanceToCode(string code = null)
        {
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {            
        }
    }
}
