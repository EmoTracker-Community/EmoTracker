using EmoTracker.Data.Media;

#if WINDOWS
using System.Windows.Media;
#else
using Avalonia.Media;
#endif

namespace EmoTracker.UI.Media.Resolvers
{
    public abstract class ImageReferenceResolver
    {
        public abstract bool CanResolveReference(ImageReference imageRef);

#if WINDOWS
        public abstract ImageSource ResolveReference(ImageReference imageRef);
#else
        public abstract IImage ResolveReference(ImageReference imageRef);
#endif
    }
}
