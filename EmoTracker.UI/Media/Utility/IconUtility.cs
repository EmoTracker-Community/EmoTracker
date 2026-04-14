#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Session;
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

        // Alpha masks keyed by IImage: bool[] of length (width * height), true = opaque.
        // ConcurrentDictionary because the background image worker writes masks while
        // the UI thread reads them (InputMaskingImage.HitTest via GetAlphaMask).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<IImage, (bool[] mask, int w, int h)> sAlphaMasks = new();

        // Cached Skia-encoded PNG bytes for each IImage produced by SkToAvalonia.
        // Used by ToSkBitmap to bypass Avalonia's Bitmap.Save(Stream), which may strip the
        // alpha channel on some platforms — causing overlay compositing to treat every pixel
        // as fully opaque and completely hide the base layer.
        // ConcurrentDictionary because the background image worker writes entries while
        // filter resolution may read them concurrently.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<IImage, byte[]> sPngCache = new();

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

        // ── SKBitmap decode / promote ───────────────────────────────────────────

        /// <summary>
        /// Decode a stream to an SKBitmap, promote to Bgra8888/Premul, and apply
        /// the magenta colour-key.  This is the internal entry point for the
        /// SKBitmap filter pipeline — callers get an SKBitmap they can pass through
        /// <see cref="ApplyFilterSpecToSKBitmap"/> without any PNG round-trips.
        /// </summary>
        internal static SKBitmap DecodeSKBitmap(Stream stream)
        {
            if (stream == null)
                return null;
            try
            {
                SKBitmap decoded = SKBitmap.Decode(stream);
                if (decoded == null) return null;

                // Promote to Bgra8888/Premul.
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

                // Apply color key: magenta (R=255, G=0, B=255) → transparent.
                // Uses direct pixel buffer access for ~10-50× speedup over GetPixel/SetPixel.
                ApplyColorKey(bmp);

                return bmp;
            }
            catch { return null; }
        }

        /// <summary>
        /// Replace magenta (255, 0, 255) pixels with transparent using direct pixel
        /// buffer access.  The bitmap must be Bgra8888.
        /// </summary>
        private static unsafe void ApplyColorKey(SKBitmap bmp)
        {
            int pixelCount = bmp.Width * bmp.Height;
            uint* pixels = (uint*)bmp.GetPixels().ToPointer();

            for (int i = 0; i < pixelCount; i++)
            {
                uint c = pixels[i];
                // BGRA8888 little-endian: B=byte0, G=byte1, R=byte2, A=byte3
                // As uint32: 0xAARRGGBB
                byte blue  = (byte)(c);
                byte green = (byte)(c >> 8);
                byte red   = (byte)(c >> 16);

                if (red == 255 && green == 0 && blue == 255)
                    pixels[i] = 0; // fully transparent
            }
        }

        /// <summary>
        /// Compute the alpha mask from an SKBitmap using direct pixel buffer access.
        /// </summary>
        private static unsafe bool[] ComputeAlphaMask(SKBitmap bmp)
        {
            int pixelCount = bmp.Width * bmp.Height;
            var mask = new bool[pixelCount];
            uint* pixels = (uint*)bmp.GetPixels().ToPointer();

            for (int i = 0; i < pixelCount; i++)
            {
                byte alpha = (byte)(pixels[i] >> 24);
                mask[i] = alpha >= 10;
            }

            return mask;
        }

        // ── IImage ↔ SKBitmap conversion ────────────────────────────────────────

        /// <summary>
        /// Convert an Avalonia IImage to an SKBitmap for use in the filter pipeline.
        /// Returns null if conversion fails.  The caller owns the returned bitmap.
        /// </summary>
        internal static SKBitmap ToSkBitmapForFilter(IImage image) => ToSkBitmap(image);

        /// <summary>Convert an Avalonia IImage back to an SKBitmap for pixel processing.</summary>
        /// <remarks>
        /// Uses the Skia-encoded PNG bytes cached by <see cref="SkToAvalonia"/> when available,
        /// avoiding <c>Bitmap.Save(Stream)</c> which may drop the alpha channel on some platforms.
        /// Always returns a <c>Bgra8888</c> bitmap.
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
                    pngStream = new MemoryStream(cachedBytes, writable: false);
                }
                else
                {
                    var ms = new MemoryStream();
                    avBitmap.Save(ms);
                    ms.Position = 0;
                    pngStream = ms;
                }

                using (pngStream)
                {
                    SKBitmap decoded = SKBitmap.Decode(pngStream);
                    if (decoded == null) return null;

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

        /// <summary>
        /// Convert an SKBitmap to an Avalonia IImage, computing and caching its
        /// alpha mask for InputMaskingImage hit-testing.  Disposes the input bitmap.
        /// This is the single conversion point at the END of the SKBitmap pipeline.
        /// </summary>
        internal static IImage FinalizeToAvalonia(SKBitmap bmp)
        {
            if (bmp == null) return null;
            try
            {
                var mask = ComputeAlphaMask(bmp);
                var avBitmap = SkToAvaloniaCore(bmp);
                sAlphaMasks[avBitmap] = (mask, bmp.Width, bmp.Height);
                bmp.Dispose();
                return avBitmap;
            }
            catch
            {
                bmp?.Dispose();
                return null;
            }
        }

        /// <summary>Convert an SKBitmap to an Avalonia IImage, optionally caching its alpha mask.</summary>
        private static IImage SkToAvalonia(SKBitmap bmp, bool storeMask = false)
        {
            var avBitmap = SkToAvaloniaCore(bmp);

            if (storeMask)
            {
                var mask = ComputeAlphaMask(bmp);
                sAlphaMasks[avBitmap] = (mask, bmp.Width, bmp.Height);
            }

            return avBitmap;
        }

        /// <summary>
        /// Core SKBitmap → Avalonia Bitmap conversion.  Encodes as PNG and caches
        /// the PNG bytes so ToSkBitmap can round-trip without Bitmap.Save.
        /// </summary>
        private static Avalonia.Media.Imaging.Bitmap SkToAvaloniaCore(SKBitmap bmp)
        {
            using var skImg = SKImage.FromBitmap(bmp);
            using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
            byte[] pngBytes = encoded.ToArray();
            var avBitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(pngBytes));

            sPngCache[avBitmap] = pngBytes;

            return avBitmap;
        }

        // ── SKBitmap-based filter operations ────────────────────────────────────
        //
        // These operate entirely in SKBitmap space.  Skia's colour-matrix filter
        // runs natively (potentially SIMD-optimised) and is orders of magnitude
        // faster than per-pixel GetPixel/SetPixel loops.
        //
        // The caller is responsible for disposing the INPUT bitmap if it differs
        // from the returned bitmap (the ApplyFilterSpecToSKBitmap method does this
        // automatically for chained filters).

        /// <summary>Apply a 4×5 colour matrix to an SKBitmap via Skia's native path.</summary>
        private static SKBitmap ApplyColorMatrix(SKBitmap src, float[] matrix)
        {
            var result = new SKBitmap(src.Info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            using var filter = SKColorFilter.CreateColorMatrix(matrix);
            using var paint = new SKPaint
            {
                ColorFilter = filter,
                BlendMode = SKBlendMode.Src   // overwrite destination, don't composite
            };
            canvas.DrawBitmap(src, 0, 0, paint);
            return result;
        }

        /// <summary>
        /// Build the 4×5 colour matrix for a grayscale+saturation operation.
        /// When <paramref name="saturation"/> is 0 the result is fully desaturated;
        /// when 1 the image is unchanged.
        /// </summary>
        private static float[] ComputeGrayscaleMatrix(LuminanceMode mode, float saturation)
        {
            // Luminance weights per mode — determines how RGB channels
            // contribute to the grayscale value.
            float wr, wg, wb;
            switch (mode)
            {
                case LuminanceMode.Max:
                    // Max-luminance can't be expressed as a linear matrix.
                    // Fall back to equal weights as a reasonable approximation.
                    wr = wg = wb = 1f / 3f;
                    break;
                case LuminanceMode.Blue:
                    wr = 0; wg = 0; wb = 1;
                    break;
                case LuminanceMode.Green:
                    wr = 0; wg = 1; wb = 0;
                    break;
                case LuminanceMode.Red:
                    wr = 1; wg = 0; wb = 0;
                    break;
                case LuminanceMode.BT709:
                    // The original code uses (2R+3G+B)/6 which is closer to BT.601
                    wr = 2f / 6f; wg = 3f / 6f; wb = 1f / 6f;
                    break;
                case LuminanceMode.BT601:
                    // Original uses (2R+3G+B)>>3 — divides by 8, not 6.
                    // Weights intentionally sum to 0.75, producing a dimmer result.
                    wr = 2f / 8f; wg = 3f / 8f; wb = 1f / 8f;
                    break;
                default: // Average, Avg
                    wr = wg = wb = 1f / 3f;
                    break;
            }

            // Saturation matrix: lerp between grayscale and identity.
            //   out_r = r * (wr*(1-s) + s) + g * wg*(1-s) + b * wb*(1-s)
            //   (and analogously for g, b)
            float s = saturation;
            float inv = 1f - s;

            return new float[]
            {
                wr * inv + s,  wg * inv,      wb * inv,      0, 0,
                wr * inv,      wg * inv + s,  wb * inv,      0, 0,
                wr * inv,      wg * inv,      wb * inv + s,  0, 0,
                0,             0,             0,             1, 0
            };
        }

        internal static SKBitmap MakeGrayscaleSK(SKBitmap src, LuminanceMode mode = LuminanceMode.Average, float saturation = 0.0f)
        {
            return ApplyColorMatrix(src, ComputeGrayscaleMatrix(mode, saturation));
        }

        internal static SKBitmap MakeDimSK(SKBitmap src, int divisor = 2)
        {
            // Original: r = r - r/divisor  →  factor = 1 - 1/divisor
            float factor = 1.0f - 1.0f / divisor;
            float[] matrix =
            {
                factor, 0, 0, 0, 0,
                0, factor, 0, 0, 0,
                0, 0, factor, 0, 0,
                0, 0, 0, 1, 0
            };
            return ApplyColorMatrix(src, matrix);
        }

        internal static SKBitmap AdjustBrightnessSK(SKBitmap src, float brightness)
        {
            float[] matrix =
            {
                brightness, 0, 0, 0, 0,
                0, brightness, 0, 0, 0,
                0, 0, brightness, 0, 0,
                0, 0, 0, 1, 0
            };
            return ApplyColorMatrix(src, matrix);
        }

        /// <summary>
        /// Composite an overlay bitmap on top of a base bitmap using Skia's
        /// built-in SrcOver blend mode.  Disposes the overlay; returns a new bitmap.
        /// The base bitmap is NOT disposed (caller manages it).
        /// </summary>
        internal static SKBitmap ApplyOverlaySK(SKBitmap baseBmp, SKBitmap overlay)
        {
            if (overlay == null) return baseBmp;
            if (baseBmp == null) return overlay;

            if (baseBmp.Width != overlay.Width || baseBmp.Height != overlay.Height)
            {
                TrackerSession.Current.Scripts.OutputError("Not applying overlay to base image because dimensions don't match.");
                overlay.Dispose();
                return baseBmp;
            }

            // Clone base and draw overlay on top with SrcOver blend
            var result = baseBmp.Copy();
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint { BlendMode = SKBlendMode.SrcOver };
            canvas.DrawBitmap(overlay, 0, 0, paint);
            overlay.Dispose();
            return result;
        }

        /// <summary>
        /// Apply the full filter specification, staying in SKBitmap space for the
        /// entire chain.  Each filter step produces a new SKBitmap; intermediates
        /// are disposed automatically.  The INPUT bitmap is consumed (may be
        /// disposed or returned).
        /// </summary>
        internal static SKBitmap ApplyFilterSpecToSKBitmap(IGamePackage package, SKBitmap bmp, string filterSpec)
        {
            if (bmp == null)
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

                        SKBitmap prev = bmp;

                        if (mod.StartsWith("grayscale", StringComparison.OrdinalIgnoreCase))
                            bmp = MakeGrayscaleSK(bmp);
                        else if (mod.StartsWith("dim", StringComparison.OrdinalIgnoreCase))
                            bmp = MakeDimSK(bmp);
                        else if (mod.StartsWith("halfdim", StringComparison.OrdinalIgnoreCase))
                            bmp = MakeDimSK(bmp, 4);
                        else if (mod.StartsWith("quarterdim", StringComparison.OrdinalIgnoreCase))
                            bmp = MakeDimSK(bmp, 8);
                        else if (mod.StartsWith("brightness", StringComparison.OrdinalIgnoreCase))
                        {
                            if (args.Length >= 1 &&
                                float.TryParse(args[0], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float brightness))
                            {
                                brightness = Math.Max(brightness, 0.0f);
                                bmp = AdjustBrightnessSK(bmp, brightness);
                            }
                        }
                        else if (mod.StartsWith("overlay", StringComparison.OrdinalIgnoreCase))
                        {
                            if (package != null && args.Length >= 1)
                            {
                                SKBitmap overlay = DecodeSKBitmap(package.Open(args[0]));
                                if (overlay != null)
                                    bmp = ApplyOverlaySK(bmp, overlay);
                            }
                        }
                        else if (mod.StartsWith("saturation", StringComparison.OrdinalIgnoreCase))
                        {
                            if (args.Length >= 1 &&
                                float.TryParse(args[0], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float saturation))
                            {
                                LuminanceMode mode = LuminanceMode.Average;
                                if (args.Length >= 2)
                                    Enum.TryParse<LuminanceMode>(args[1], true, out mode);
                                saturation = Math.Min(Math.Max(saturation, 0.0f), 1.0f);
                                bmp = MakeGrayscaleSK(bmp, mode, saturation);
                            }
                        }
                        else if (mod.StartsWith("@disabled", StringComparison.OrdinalIgnoreCase))
                            bmp = ApplyFilterSpecToSKBitmap(package, bmp, TrackerSession.Current.Tracker.DisabledImageFilterSpec);

                        // Dispose the intermediate bitmap if a new one was produced
                        if (bmp != prev)
                            prev.Dispose();
                    }
                }
            }

            return bmp;
        }

        // ── Legacy IImage-based API (backward compat) ───────────────────────────

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

            sHttpCache[key] = null;

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
                    sHttpCache.TryRemove(key, out _);
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

        /// <summary>
        /// Decode a stream to an Avalonia IImage with colour-key and alpha mask.
        /// Kept for backward compatibility; new resolver code should prefer
        /// <see cref="DecodeSKBitmap"/> + <see cref="FinalizeToAvalonia"/>.
        /// </summary>
        public static IImage GetImage(Stream stream)
        {
            SKBitmap bmp = DecodeSKBitmap(stream);
            if (bmp == null) return null;

            var result = SkToAvalonia(bmp, storeMask: true);
            bmp.Dispose();
            return result;
        }

        // ── Legacy IImage filter wrappers ───────────────────────────────────────
        // These are kept for any call sites that still pass IImage.  They convert
        // to SKBitmap, apply the fast SKBitmap filter, convert back.

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

                var result = ApplyOverlaySK(baseBmp, overlayBmp);

                // overlayBmp disposed by ApplyOverlaySK; baseBmp is not
                if (result != baseBmp)
                    baseBmp.Dispose();

                var avResult = SkToAvalonia(result, storeMask: true);
                result.Dispose();
                return avResult;
            }
            catch { return image; }
        }

        public static IImage MakeImageGrayscale(IImage image, LuminanceMode mode = LuminanceMode.Average, float saturation = 0.0f)
        {
            if (image == null) return null;
            SKBitmap bmp = ToSkBitmap(image);
            if (bmp == null) return image;
            SKBitmap result = MakeGrayscaleSK(bmp, mode, saturation);
            bmp.Dispose();
            var avResult = SkToAvalonia(result, storeMask: true);
            result.Dispose();
            return avResult;
        }

        public static IImage AdjustSaturation(IGamePackage package, IImage image, params string[] args)
        {
            if (image == null) return null;
            if (args.Length >= 1)
            {
                if (float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float saturation))
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
                if (float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float brightness))
                {
                    brightness = Math.Max(brightness, 0.0f);
                    SKBitmap bmp = ToSkBitmap(image);
                    if (bmp == null) return image;
                    SKBitmap result = AdjustBrightnessSK(bmp, brightness);
                    bmp.Dispose();
                    var avResult = SkToAvalonia(result, storeMask: true);
                    result.Dispose();
                    return avResult;
                }
            }
            return image;
        }

        public static IImage MakeImageDim(IImage image, int divisor = 2)
        {
            if (image == null) return null;
            SKBitmap bmp = ToSkBitmap(image);
            if (bmp == null) return image;
            SKBitmap result = MakeDimSK(bmp, divisor);
            bmp.Dispose();
            var avResult = SkToAvalonia(result, storeMask: true);
            result.Dispose();
            return avResult;
        }

        /// <summary>
        /// Legacy IImage-based filter chain.  Kept for backward compatibility.
        /// Resolvers should prefer the SKBitmap pipeline.
        /// </summary>
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
                            image = IconUtility.ApplyFilterSpecToImage(package, image, TrackerSession.Current.Tracker.DisabledImageFilterSpec);
                    }
                }
            }

            return image;
        }
    }
}
