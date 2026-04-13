using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.IO;

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
        /// Source image width in pixels, read from the image header at creation time.
        /// Used to create correctly-sized placeholder images so Avalonia's layout
        /// system can measure controls before the real image is resolved.
        /// Zero if dimensions could not be determined.
        /// </summary>
        public int SourceWidth { get; set; }

        /// <summary>
        /// Source image height in pixels.  See <see cref="SourceWidth"/>.
        /// </summary>
        public int SourceHeight { get; set; }

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

        /// <summary>
        /// Reads the width and height from a PNG or BMP image stream header
        /// without fully decoding the image.  Returns (0, 0) if the format
        /// is not recognised or the stream is too short.
        /// </summary>
        internal static (int width, int height) ReadImageDimensions(Stream stream)
        {
            if (stream == null || !stream.CanRead)
                return (0, 0);

            try
            {
                byte[] header = new byte[26];
                int bytesRead = 0;
                while (bytesRead < header.Length)
                {
                    int n = stream.Read(header, bytesRead, header.Length - bytesRead);
                    if (n == 0) break;
                    bytesRead += n;
                }

                // PNG: signature(8) + IHDR length(4) + "IHDR"(4) + width(4) + height(4)
                if (bytesRead >= 24 &&
                    header[0] == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71)
                {
                    int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    return (w, h);
                }

                // BMP: "BM" + filesize(4) + reserved(4) + offset(4) + headersize(4) + width(4) + height(4)
                if (bytesRead >= 26 && header[0] == (byte)'B' && header[1] == (byte)'M')
                {
                    int w = header[18] | (header[19] << 8) | (header[20] << 16) | (header[21] << 24);
                    int h = header[22] | (header[23] << 8) | (header[24] << 16) | (header[25] << 24);
                    if (h < 0) h = -h; // top-down BMP uses negative height
                    return (w, h);
                }

                // JPEG: SOI marker (0xFF 0xD8), then scan for SOF0/SOF2 frame marker
                if (bytesRead >= 2 && header[0] == 0xFF && header[1] == 0xD8)
                {
                    return ReadJpegDimensions(stream, header, bytesRead);
                }
            }
            catch
            {
                // Swallow – dimensions are best-effort
            }

            return (0, 0);
        }

        /// <summary>
        /// Scans JPEG markers to find a Start-Of-Frame (SOF) segment and reads
        /// the image dimensions from it.  The stream position is after the initial
        /// header bytes that were already read into <paramref name="header"/>.
        /// </summary>
        static (int width, int height) ReadJpegDimensions(Stream stream, byte[] header, int bytesRead)
        {
            // We've consumed the first 'bytesRead' bytes into header[].
            // Continue reading the marker stream from the current position.
            // JPEG markers are 0xFF followed by a marker type byte.
            // SOF markers: 0xC0 (baseline), 0xC1 (extended), 0xC2 (progressive).
            // SOF payload: length(2) + precision(1) + height(2) + width(2)

            // First, process any remaining bytes in the header buffer starting
            // after the 2-byte SOI marker.
            int pos = 2;
            byte[] buf = new byte[2];

            while (true)
            {
                // Read marker: 0xFF + type
                int b1, b2;

                if (pos + 1 < bytesRead)
                {
                    b1 = header[pos];
                    b2 = header[pos + 1];
                    pos += 2;
                }
                else
                {
                    b1 = stream.ReadByte();
                    b2 = stream.ReadByte();
                }

                if (b1 < 0 || b2 < 0)
                    return (0, 0); // unexpected end of stream

                if (b1 != 0xFF)
                    return (0, 0); // not a valid marker

                // Skip padding 0xFF bytes
                while (b2 == 0xFF)
                {
                    b2 = stream.ReadByte();
                    if (b2 < 0) return (0, 0);
                }

                // SOF0 (0xC0), SOF1 (0xC1), SOF2 (0xC2) contain dimensions
                if (b2 >= 0xC0 && b2 <= 0xC2)
                {
                    // Read: length(2) + precision(1) + height(2) + width(2)
                    byte[] sof = new byte[7];
                    int sofRead = 0;
                    while (sofRead < 7)
                    {
                        int n = stream.Read(sof, sofRead, 7 - sofRead);
                        if (n == 0) return (0, 0);
                        sofRead += n;
                    }

                    int height = (sof[3] << 8) | sof[4];
                    int width = (sof[5] << 8) | sof[6];
                    return (width, height);
                }

                // Not a SOF marker – skip this segment
                // Read segment length (2 bytes, big-endian, includes the length bytes)
                int len1 = stream.ReadByte();
                int len2 = stream.ReadByte();
                if (len1 < 0 || len2 < 0) return (0, 0);

                int segLen = (len1 << 8) | len2;
                if (segLen < 2) return (0, 0);

                // Skip the rest of the segment
                int toSkip = segLen - 2;
                if (stream.CanSeek)
                {
                    stream.Position += toSkip;
                }
                else
                {
                    byte[] skipBuf = new byte[Math.Min(toSkip, 4096)];
                    while (toSkip > 0)
                    {
                        int n = stream.Read(skipBuf, 0, Math.Min(toSkip, skipBuf.Length));
                        if (n == 0) return (0, 0);
                        toSkip -= n;
                    }
                }

                // Safety: don't scan forever
                if (stream.CanSeek && stream.Position > 65536)
                    return (0, 0);
            }
        }

        /// <summary>
        /// Reads image dimensions from the package for the given path and stores
        /// them on the reference.  The stream is opened, header is read, and
        /// the stream is disposed immediately.
        /// </summary>
        static void PopulateDimensions(ImageReference result, IGamePackage package, string rawPath)
        {
            try
            {
                // rawPath is the gamepackage:// URI – extract the actual file path
                string filePath = rawPath;
                if (filePath.StartsWith("gamepackage://"))
                    filePath = filePath.Substring("gamepackage://".Length);

                using (Stream s = package.Open(filePath))
                {
                    if (s != null)
                    {
                        var (w, h) = ReadImageDimensions(s);
                        result.SourceWidth = w;
                        result.SourceHeight = h;
                    }
                }
            }
            catch
            {
                // Dimensions are best-effort; leave as 0×0
            }
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

            PopulateDimensions(result, package, path);
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

            // Filters don't change dimensions – inherit from the source
            result.SourceWidth = existingReference.SourceWidth;
            result.SourceHeight = existingReference.SourceHeight;

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
                // Layered images composite at the first layer's dimensions
                var firstLayer = instance.Layers[0];
                instance.SourceWidth = firstLayer.SourceWidth;
                instance.SourceHeight = firstLayer.SourceHeight;

                NotifyCreated(instance);

                return instance;
            }

            return null;
        }
    }
}
