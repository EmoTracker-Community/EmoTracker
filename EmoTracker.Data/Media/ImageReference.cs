using EmoTracker.Core;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Media
{
    public abstract class ImageReference : ObservableObject
    {
        /// <summary>
        /// The resolved display-ready image for this reference.  Set by the image
        /// resolution service on the UI thread once background generation completes.
        /// XAML bindings should bind to <c>Icon.ResolvedImage</c> (etc.) so that the
        /// UI updates automatically when the image becomes available.
        /// </summary>
        object mResolvedImage;
        public object ResolvedImage
        {
            get { return mResolvedImage; }
            set { SetProperty(ref mResolvedImage, value); }
        }

        /// <summary>
        /// Optional callback invoked whenever a new ImageReference is created via
        /// a factory method.  Set by the image resolution service at startup so
        /// that newly-created references are automatically queued for background
        /// resolution.
        /// </summary>
        public static Action<ImageReference> OnImageReferenceCreated { get; set; }

        static void NotifyCreated(ImageReference imageRef)
        {
            OnImageReferenceCreated?.Invoke(imageRef);
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

            NotifyCreated(result);

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

            NotifyCreated(result);

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
                NotifyCreated(instance);

                return instance;
            }

            return null;
        }
    }
}
