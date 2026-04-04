using EmoTracker.Data.Media;
using System.Windows.Media;

namespace EmoTracker.UI.Media.Resolvers
{
    public abstract class ImageReferenceResolver
    {
        public abstract bool CanResolveReference(ImageReference imageRef);

        public abstract ImageSource ResolveReference(ImageReference imageRef);
    }
}
