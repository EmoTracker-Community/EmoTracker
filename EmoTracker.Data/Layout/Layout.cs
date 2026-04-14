using EmoTracker.Core;
using EmoTracker.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Layout
{
    public class Layout : ObservableObject
    {
        LayoutItem mRoot;

        public LayoutItem Root
        {
            get { return mRoot; }
            private set { SetProperty(ref mRoot, value); }
        }

        public override void Dispose()
        {
            DisposeObjectAndDefault(ref mRoot);
            base.Dispose();
        }

        public bool Load(Stream stream, IGamePackage package)
        {
            try
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    return Load(root, package) && Root != null;
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }

            return false;
        }

        public bool Load(JObject root, IGamePackage package)
        {
            try
            {
                Root = LayoutItem.CreateLayoutItem(root, package);
            }
            catch
            {
            }

            return Root != null;
        }

        public void Clear()
        {
            Root = null;
        }
    }
}
