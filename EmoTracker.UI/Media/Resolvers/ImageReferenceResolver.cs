using EmoTracker.Data.Media;

using Avalonia.Media;

namespace EmoTracker.UI.Media.Resolvers
{
    public abstract class ImageReferenceResolver
    {
        public abstract bool CanResolveReference(ImageReference imageRef);

        public abstract IImage ResolveReference(ImageReference imageRef);
    }
}
