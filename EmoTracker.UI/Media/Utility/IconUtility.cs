#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data;
using System;
using System.IO;
using System.Linq;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.Collections.Generic;

namespace EmoTracker.UI.Media.Utility
{
    public class IconUtility : ObservableSingleton<IconUtility>
    {
        private bool mbEnableDpiConversion = true;

        public bool EnableDpiConversion
        {
            get { return mbEnableDpiConversion; }
            set { SetProperty(ref mbEnableDpiConversion, value); }
        }

        public enum LuminanceMode
        {
            Avg,
            Average,
            Max,
            Blue,
            Green,
            Red,
            BT709,
            BT601
        }

        public static string[] GetArgs(string filterCommand)
        {
            string[] args = filterCommand.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < args.Length; ++i)
                args[i] = args[i].Trim();
            return args;
        }

        // ── Avalonia / SkiaSharp image pipeline ──────────────────────────────────

        // Alpha masks keyed by IImage: bool[] of length (width * height), true = opaque
        private static readonly Dictionary<IImage, (bool[] mask, int w, int h)> sAlphaMasks = new();

        // Cached Skia-encoded PNG bytes for each IImage produced by SkToAvalonia.
        // Used by ToSkBitmap to bypass Avalonia's Bitmap.Save(Stream), which may strip the
        // alpha channel on some platforms — causing overlay compositing to treat every pixel
        // as fully opaque and completely hide the base layer.
        private static readonly Dictionary<IImage, byte[]> sPngCache = new();

        // HTTP/HTTPS image download cache. null value means "download in progress".
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IImage?> sHttpCache = new();
        private static readonly System.Net.Http.HttpClient sHttpClient = new();

        /// <summary>
        /// Raised on the UI thread after an HTTP image finishes downloading.
        /// Subscribers (e.g. ApplicationModel) can use this to refresh bindings.
        /// </summary>
        public static event EventHandler? HttpImageLoaded;

        /// <summary>Returns the precomputed alpha mask for an image, or null if not available.</summary>
        public static (bool[] mask, int w, int h)? GetAlphaMask(IImage image)
        {
            if (image != null && sAlphaMasks.TryGetValue(image, out var entry))
                return entry;
            return null;
        }

        /// <summary>Convert an Avalonia IImage back to an SKBitmap for pixel processing.</summary>
        /// <remarks>
        /// Uses the Skia-encoded PNG bytes cached by <see cref="SkToAvalonia"/> when available,
        /// avoiding <c>Bitmap.Save(Stream)</c> which may drop the alpha channel on some platforms
        /// (causing all pixels to appear fully opaque and breaking overlay compositing).
        /// Always returns a <c>Bgra8888</c> bitmap so downstream pixel loops can read and write
        /// alpha correctly regardless of the source image's original colour type.
        /// </remarks>
        private static SKBitmap ToSkBitmap(IImage image)
        {
            if (image is not Avalonia.Media.Imaging.Bitmap avBitmap)
                return null;
            try
            {
                Stream pngStream;
                if (sPngCache.TryGetValue(avBitmap, out byte[] cachedBytes))
                {
                    // Use the exact bytes that Skia encoded — guaranteed to have correct alpha.
                    pngStream = new MemoryStream(cachedBytes, writable: false);
                }
                else
                {
                    // Fallback for images not created by SkToAvalonia (e.g. from GetImageRaw).
                    var ms = new MemoryStream();
                    avBitmap.Save(ms);
                    ms.Position = 0;
                    pngStream = ms;
                }

                using (pngStream)
                {
                    SKBitmap decoded = SKBitmap.Decode(pngStream);
                    if (decoded == null) return null;

                    // Promote to Bgra8888/Premul so pixel operations can read and write alpha correctly.
                    // Rgb888x forces alpha to 255; AlphaType.Opaque causes SKImage.FromBitmap to encode
                    // as RGB PNG (no alpha channel), so transparent pixels from the color-key pass are
                    // lost when the image is round-tripped through sPngCache.
                    if (decoded.ColorType != SKColorType.Bgra8888 || decoded.AlphaType == SKAlphaType.Opaque)
                    {
                        var targetInfo = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        SKBitmap promoted = new SKBitmap(targetInfo);
                        using (var cvs = new SKCanvas(promoted))
                        {
                            cvs.Clear(SKColors.Transparent);
                            cvs.DrawBitmap(decoded, 0, 0);
                        }
                        decoded.Dispose();
                        return promoted;
                    }
                    return decoded;
                }
            }
            catch { return null; }
        }

        /// <summary>Convert an SKBitmap to an Avalonia IImage, optionally caching its alpha mask.</summary>
        private static IImage SkToAvalonia(SKBitmap bmp, bool storeMask = false)
        {
            using var skImg = SKImage.FromBitmap(bmp);
            using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
            byte[] pngBytes = encoded.ToArray();
            var avBitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(pngBytes));

            // Always cache the raw PNG bytes so ToSkBitmap can round-trip back to SKBitmap
            // without going through Bitmap.Save(Stream), which may strip the alpha channel.
            sPngCache[avBitmap] = pngBytes;

            if (storeMask)
            {
                var mask = new bool[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        mask[y * bmp.Width + x] = bmp.GetPixel(x, y).Alpha >= 10;
                sAlphaMasks[avBitmap] = (mask, bmp.Width, bmp.Height);
            }

            return avBitmap;
        }

        /// <summary>
        /// Translates a WPF pack://application:,,,/AssemblyName;component/path URI
        /// to the Avalonia equivalent avares://AssemblyName/path.
        /// Returns the original URI unchanged for all other schemes.
        /// </summary>
        private static Uri TranslatePackUri(Uri uri)
        {
            const string packPrefix = "pack://application:,,,/";
            string orig = uri.OriginalString;
            if (!orig.StartsWith(packPrefix, StringComparison.OrdinalIgnoreCase))
                return uri;
            string rest = orig.Substring(packPrefix.Length);
            int compIdx = rest.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
            if (compIdx < 0) return uri;
            string assembly = rest.Substring(0, compIdx);
            string path = rest.Substring(compIdx + ";component".Length); // includes leading /
            return new Uri($"avares://{assembly}{path}");
        }

        public static IImage GetImageRaw(Uri uri)
        {
            try
            {
                Uri resolved = TranslatePackUri(uri);
                if (resolved.Scheme == "avares")
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(resolved);
                    return new Avalonia.Media.Imaging.Bitmap(stream);
                }
                if (resolved.IsFile)
                    return new Avalonia.Media.Imaging.Bitmap(resolved.LocalPath);
                if (resolved.Scheme == "http" || resolved.Scheme == "https")
                    return GetImageFromHttp(resolved);
                return null;
            }
            catch { return null; }
        }

        private static IImage? GetImageFromHttp(Uri uri)
        {
            string key = uri.AbsoluteUri;
            if (sHttpCache.TryGetValue(key, out IImage? cached))
                return cached; // null if still loading, non-null if loaded

            // Mark as loading to avoid duplicate downloads
            sHttpCache[key] = null;

            // Download in background; raise HttpImageLoaded when done so callers can refresh
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await sHttpClient.GetByteArrayAsync(uri).ConfigureAwait(false);
                    using var ms = new System.IO.MemoryStream(bytes);
                    sHttpCache[key] = new Avalonia.Media.Imaging.Bitmap(ms);
                }
                catch
                {
                    sHttpCache.TryRemove(key, out _); // allow retry on next call
                }
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    HttpImageLoaded?.Invoke(null, EventArgs.Empty));
            });

            return null;
        }

        public static IImage GetImage(Uri uri)
        {
            try
            {
                Uri resolved = TranslatePackUri(uri);
                if (resolved.Scheme == "avares")
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(resolved);
                    return GetImage(stream);
                }
                if (resolved.IsFile)
                {
                    using var stream = File.OpenRead(resolved.LocalPath);
                    return GetImage(stream);
                }
                return null;
            }
            catch { return null; }
        }

        public static IImage GetImage(Stream stream)
        {
            if (stream == null)
                return null;
            try
            {
                SKBitmap decoded = SKBitmap.Decode(stream);
                if (decoded == null) return null;

                // Promote to Bgra8888/Premul before the color-key pass.
                // SKBitmap.Decode may return Rgb888x (no alpha channel, e.g. RGB PNG or 24-bit BMP).
                // Even if the color type is already Bgra8888, AlphaType.Opaque causes SKImage.FromBitmap
                // to encode as RGB PNG — dropping all alpha — so transparent pixels set by the color-key
                // pass are lost when the image is round-tripped through sPngCache for filter operations.
                SKBitmap bmp;
                if (decoded.ColorType != SKColorType.Bgra8888 || decoded.AlphaType == SKAlphaType.Opaque)
                {
                    var targetInfo = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    bmp = new SKBitmap(targetInfo);
                    using (var cvs = new SKCanvas(bmp))
                    {
                        cvs.Clear(SKColors.Transparent);
                        cvs.DrawBitmap(decoded, 0, 0);
                    }
                    decoded.Dispose();
                }
                else
                {
                    bmp = decoded;
                }

                // Apply color key: magenta (R=255, G=0, B=255) → transparent
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        SKColor c = bmp.GetPixel(x, y);
                        if (c.Red == 255 && c.Green == 0 && c.Blue == 255)
                            bmp.SetPixel(x, y, SKColors.Transparent);
                    }
                }

                var result = SkToAvalonia(bmp, storeMask: true);
                bmp.Dispose();
                return result;
            }
            catch { return null; }
        }

        public static IImage ApplyOverlayImage(IGamePackage package, IImage image, params string[] args)
        {
            if (package == null)
                return image;

            if (args.Length >= 1)
            {
                IImage overlay = GetImage(package.Open(args[0]));
                if (overlay != null)
                    return ApplyOverlayImage(image, overlay);
            }

            return image;
        }

        public static IImage ApplyOverlayImage(IImage image, IImage overlay)
        {
            if (overlay == null) return image;
            if (image == null) return overlay;

            try
            {
                SKBitmap baseBmp = ToSkBitmap(image);
                SKBitmap overlayBmp = ToSkBitmap(overlay);

                if (baseBmp == null) return image;
                if (overlayBmp == null) { baseBmp.Dispose(); return image; }

                if (baseBmp.Width != overlayBmp.Width || baseBmp.Height != overlayBmp.Height)
                {
                    baseBmp.Dispose();
                    overlayBmp.Dispose();
                    ScriptManager.Instance.OutputError("Not applying overlay to base image because dimensions don't match.");
                    return image;
                }

                for (int y = 0; y < baseBmp.Height; y++)
                {
                    for (int x = 0; x < baseBmp.Width; x++)
                    {
                        SKColor b = baseBmp.GetPixel(x, y);
                        SKColor o = overlayBmp.GetPixel(x, y);

                        float alpha = o.Alpha / 255.0f;
                        float invAlpha = 1.0f - alpha;

                        byte r = (byte)Math.Clamp((int)(o.Red   * alpha + b.Red   * invAlpha), 0, 255);
                        byte g = (byte)Math.Clamp((int)(o.Green * alpha + b.Green * invAlpha), 0, 255);
                        byte bl = (byte)Math.Clamp((int)(o.Blue  * alpha + b.Blue  * invAlpha), 0, 255);
                        byte a = Math.Max(b.Alpha, o.Alpha);

                        baseBmp.SetPixel(x, y, new SKColor(r, g, bl, a));
                    }
                }

                overlayBmp.Dispose();
                var result = SkToAvalonia(baseBmp, storeMask: true);
                baseBmp.Dispose();
                return result;
            }
            catch { return image; }
        }

        private static byte Lerp(byte a, byte b, float factor)
        {
            if (factor <= 0.0f) return a;
            if (factor >= 1.0f) return b;
            return (byte)(((float)a * (1.0f - factor)) + ((float)b * factor) + 0.5f);
        }

        public static IImage MakeImageGrayscale(IImage image, LuminanceMode mode = LuminanceMode.Average, float saturation = 0.0f)
        {
            if (image == null) return null;

            SKBitmap bmp = ToSkBitmap(image);
            if (bmp == null) return image;

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    SKColor c = bmp.GetPixel(x, y);
                    byte r = c.Red, g = c.Green, b = c.Blue;
                    byte ro = r, go = g, bo = b;

                    switch (mode)
                    {
                        case LuminanceMode.Avg:
                        case LuminanceMode.Average: r = g = b = (byte)((r + g + b) / 3.0); break;
                        case LuminanceMode.Max:     r = g = b = Math.Max(Math.Max(r, g), b); break;
                        case LuminanceMode.Blue:    r = g = b; break;
                        case LuminanceMode.Green:   b = r = g; break;
                        case LuminanceMode.Red:     b = g = r; break;
                        case LuminanceMode.BT709:   r = g = b = (byte)(((uint)r + (uint)r + (uint)b + (uint)g + (uint)g + (uint)g) / 6); break;
                        case LuminanceMode.BT601:   r = g = b = (byte)(((uint)r + (uint)r + (uint)b + (uint)g + (uint)g + (uint)g) >> 3); break;
                    }

                    b = Lerp(b, bo, saturation);
                    g = Lerp(g, go, saturation);
                    r = Lerp(r, ro, saturation);

                    bmp.SetPixel(x, y, new SKColor(r, g, b, c.Alpha));
                }
            }

            var result = SkToAvalonia(bmp, storeMask: true);
            bmp.Dispose();
            return result;
        }

        public static IImage AdjustSaturation(IGamePackage package, IImage image, params string[] args)
        {
            if (image == null) return null;

            if (args.Length >= 1)
            {
                float saturation;
                if (float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out saturation))
                {
                    LuminanceMode mode = LuminanceMode.Average;
                    if (args.Length >= 2)
                        Enum.TryParse<LuminanceMode>(args[1], true, out mode);
                    saturation = Math.Min(Math.Max(saturation, 0.0f), 1.0f);
                    return MakeImageGrayscale(image, mode, saturation);
                }
            }

            return image;
        }

        public static IImage AdjustBrightness(IGamePackage package, IImage image, params string[] args)
        {
            if (image == null) return null;

            if (args.Length >= 1)
            {
                float brightness;
                if (float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out brightness))
                {
                    brightness = Math.Max(brightness, 0.0f);

                    SKBitmap bmp = ToSkBitmap(image);
                    if (bmp == null) return image;

                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            SKColor c = bmp.GetPixel(x, y);
                            byte r = (byte)Math.Clamp(c.Red   * brightness, 0, 255);
                            byte g = (byte)Math.Clamp(c.Green * brightness, 0, 255);
                            byte b = (byte)Math.Clamp(c.Blue  * brightness, 0, 255);
                            bmp.SetPixel(x, y, new SKColor(r, g, b, c.Alpha));
                        }
                    }

                    var result = SkToAvalonia(bmp, storeMask: true);
                    bmp.Dispose();
                    return result;
                }
            }

            return image;
        }

        public static IImage MakeImageDim(IImage image, int divisor = 2)
        {
            if (image == null) return null;

            SKBitmap bmp = ToSkBitmap(image);
            if (bmp == null) return image;

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    SKColor c = bmp.GetPixel(x, y);
                    byte r = (byte)(c.Red   - c.Red   / divisor);
                    byte g = (byte)(c.Green - c.Green / divisor);
                    byte b = (byte)(c.Blue  - c.Blue  / divisor);
                    bmp.SetPixel(x, y, new SKColor(r, g, b, c.Alpha));
                }
            }

            var result = SkToAvalonia(bmp, storeMask: true);
            bmp.Dispose();
            return result;
        }

        public static IImage ApplyFilterSpecToImage(IGamePackage package, IImage image, string filterSpec)
        {
            if (image == null)
                return null;

            if (!string.IsNullOrWhiteSpace(filterSpec))
            {
                string[] mods = filterSpec.Split(',');
                foreach (string modRaw in mods)
                {
                    string[] tokens = GetArgs(modRaw);
                    if (tokens.Length >= 1)
                    {
                        string mod = tokens[0];
                        string[] args = tokens.Skip(1).ToArray<string>();

                        if (mod.StartsWith("grayscale", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.MakeImageGrayscale(image);
                        else if (mod.StartsWith("dim", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.MakeImageDim(image);
                        else if (mod.StartsWith("halfdim", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.MakeImageDim(image, 4);
                        else if (mod.StartsWith("quarterdim", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.MakeImageDim(image, 8);
                        else if (mod.StartsWith("brightness", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.AdjustBrightness(package, image, args);
                        else if (mod.StartsWith("overlay", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.ApplyOverlayImage(package, image, args);
                        else if (mod.StartsWith("saturation", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.AdjustSaturation(package, image, args);
                        else if (mod.StartsWith("@disabled", StringComparison.OrdinalIgnoreCase))
                            image = IconUtility.ApplyFilterSpecToImage(package, image, Tracker.Instance.DisabledImageFilterSpec);
                    }
                }
            }

            return image;
        }
    }
}
