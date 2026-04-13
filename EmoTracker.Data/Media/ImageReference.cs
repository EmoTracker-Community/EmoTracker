using EmoTracker.Core;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Media
{
    public abstract class ImageReference : ObservableObject
    {
        static bool sTracking;
        static List<ImageReference> sTrackedReferences;

        public static List<ImageReference> LastCollectedReferences { get; private set; }

        public static void BeginTrackingCreatedReferences()
        {
            sTrackedReferences = new List<ImageReference>();
            sTracking = true;
        }

        public static List<ImageReference> EndTrackingCreatedReferences()
        {
            sTracking = false;
            LastCollectedReferences = sTrackedReferences;
            var result = sTrackedReferences;
            sTrackedReferences = null;
            return result;
        }

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

            var result = new ConcreteImageReference()
            {
                URI = new Uri(path),
                Filter = filter
            };

            if (sTracking)
                sTrackedReferences?.Add(result);

            return result;
        }

        public static ImageReference FromImageReference(ImageReference existingReference, string filter = null)
        {
            if (existingReference == null)
                return null;

            if (string.IsNullOrWhiteSpace(filter))
                return existingReference;

            var result = new FilterImageReference()
            {
                Reference = existingReference,
                Filter = filter
            };

            if (sTracking)
                sTrackedReferences?.Add(result);

            return result;
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
            {
                if (sTracking)
                    sTrackedReferences?.Add(instance);

                return instance;
            }

            return null;
        }
    }
}
