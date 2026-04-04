using EmoTracker.Core;
using System;

namespace EmoTracker.Data.Media
{
    public abstract class ImageReference : ObservableObject
    {
        public static ImageReference FromPackRelativePath(string path, string filter = null)
        {
            return FromPackRelativePath(Tracker.Instance.ActiveGamePackage, path, filter);
        }

        public static ImageReference FromPackRelativePath(IGamePackage package, string path, string filter = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim();
            path = path.TrimStart(',', '/', '\\');

            if (package == null || !package.Exists(path))
                return null;

            if (!path.StartsWith("gamepackage://"))
            {
                path = "gamepackage://" + path;
            }

            return new ConcreteImageReference()
            {
                URI = new Uri(path),
                Filter = filter
            };
        }

        public static ImageReference FromImageReference(ImageReference existingReference, string filter = null)
        {
            if (existingReference == null)
                return null;

            if (string.IsNullOrWhiteSpace(filter))
                return existingReference;

            return new FilterImageReference()
            {
                Reference = existingReference,
                Filter = filter
            };
        }

        public static ImageReference FromExternalURI(Uri uri, string filter = null)
        {
            return new ConcreteImageReference()
            {
                URI = uri,
                Filter = filter
            };
        }

        public static ImageReference FromLayeredImageReferences(params ImageReference[] layers)
        {
            LayeredImageReference instance = new LayeredImageReference();

            foreach (ImageReference layer in layers)
            {
                if (layer != null)
                    instance.Layers.Add(layer);
            }

            if (instance.Layers.Count > 0)
                return instance;

            return null;
        }
    }
}
