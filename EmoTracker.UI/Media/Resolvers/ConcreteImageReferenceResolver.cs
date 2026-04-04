using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using EmoTracker.Data;
using EmoTracker.Data.Media;

namespace EmoTracker.UI.Media.Resolvers
{
    public class ConcreteImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as ConcreteImageReference != null;
        }

        public override ImageSource ResolveReference(ImageReference imageRef)
        {
            ConcreteImageReference concreteRef = imageRef as ConcreteImageReference;
            if (concreteRef == null)
                return null;

            if (concreteRef.URI == null)
                return null;

            if (concreteRef.URI.Scheme.Equals("gamepackage", StringComparison.OrdinalIgnoreCase))
            {
                if (Tracker.Instance.ActiveGamePackage == null)
                    return null;

                using (Stream s = Tracker.Instance.ActiveGamePackage.Open(string.Format("{0}{1}", Uri.UnescapeDataString(concreteRef.URI.Host), Uri.UnescapeDataString(concreteRef.URI.AbsolutePath))))
                {
                    if (s == null)
                        return null;

                    return Utility.IconUtility.ApplyFilterSpecToImage(Tracker.Instance.ActiveGamePackage, Utility.IconUtility.GetImage(s), concreteRef.Filter);
                }
            }

            return Utility.IconUtility.GetImageRaw(concreteRef.URI);
        }
    }
}
