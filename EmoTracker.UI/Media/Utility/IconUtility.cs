using EmoTracker.Core;
using EmoTracker.Data;
using System;
using System.IO;
using System.Linq;
using System.Net.Cache;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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


        private static RequestCachePolicy RawImageRequestPolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);

        public static ImageSource GetImageRaw(Uri uri)
        {
            try
            {
                return new BitmapImage(uri)
                {
                    UriCachePolicy = RawImageRequestPolicy
                };
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource GetImage(Uri uri)
        {
            try
            {
                FormatConvertedBitmap srcImg = new FormatConvertedBitmap();
                srcImg.BeginInit();
                srcImg.DestinationFormat = PixelFormats.Bgra32;
                srcImg.Source = new BitmapImage(uri);
                srcImg.EndInit();

                WriteableBitmap bmp = new WriteableBitmap(srcImg);

                byte[] buffer = new byte[bmp.PixelHeight * bmp.PixelWidth * 4];
                bmp.CopyPixels(buffer, bmp.PixelWidth * 4, 0);

                for (int y = 0; y < bmp.PixelHeight; ++y)
                {
                    for (int x = 0; x < bmp.PixelWidth; ++x)
                    {
                        byte b = buffer[(y * bmp.BackBufferStride + x * 4) + 0];
                        byte g = buffer[(y * bmp.BackBufferStride + x * 4) + 1];
                        byte r = buffer[(y * bmp.BackBufferStride + x * 4) + 2];

                        if (r == 255 && g == 0 && b == 255)
                        {
                            buffer[(y * bmp.BackBufferStride + x * 4) + 3] = 0;
                        }
                    }
                }

                if (IconUtility.Instance.EnableDpiConversion)
                {
                    //  Neutralize all images to 96dpi, which is the internal WPF standard
                    BitmapSource result = BitmapSource.Create(bmp.PixelWidth, bmp.PixelHeight, 96, 96, bmp.Format, bmp.Palette, buffer, bmp.BackBufferStride);
                    if (result != null)
                        result.Freeze();

                    return result;
                }
                else
                {
                    bmp.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), buffer, bmp.PixelWidth * 4, 0);
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch
            {
                return null;
            }

        }

        public static ImageSource GetImage(Stream stream)
        {
            if (stream == null)
                return null;

            try
            {
                BitmapImage baseImage = new BitmapImage();
                baseImage.BeginInit();
                baseImage.StreamSource = stream;
                baseImage.CacheOption = BitmapCacheOption.OnLoad;
                baseImage.EndInit();
                baseImage.Freeze();

                FormatConvertedBitmap srcImg = new FormatConvertedBitmap();
                srcImg.BeginInit();
                srcImg.DestinationFormat = PixelFormats.Bgra32;
                srcImg.Source = baseImage;
                srcImg.EndInit();

                WriteableBitmap bmp = new WriteableBitmap(srcImg);

                byte[] buffer = new byte[bmp.PixelHeight * bmp.PixelWidth * 4];
                bmp.CopyPixels(buffer, bmp.PixelWidth * 4, 0);

                for (int y = 0; y < bmp.PixelHeight; ++y)
                {
                    for (int x = 0; x < bmp.PixelWidth; ++x)
                    {
                        byte b = buffer[(y * bmp.BackBufferStride + x * 4) + 0];
                        byte g = buffer[(y * bmp.BackBufferStride + x * 4) + 1];
                        byte r = buffer[(y * bmp.BackBufferStride + x * 4) + 2];

                        if (r == 255 && g == 0 && b == 255)
                        {
                            buffer[(y * bmp.BackBufferStride + x * 4) + 3] = 0;
                        }
                    }
                }

                if (IconUtility.Instance.EnableDpiConversion)
                {
                    //  Neutralize all images to 96dpi, which is the internal WPF standard
                    BitmapSource result = BitmapSource.Create(bmp.PixelWidth, bmp.PixelHeight, 96, 96, bmp.Format, bmp.Palette, buffer, bmp.BackBufferStride);
                    if (result != null)
                        result.Freeze();

                    return result;
                }
                else
                {
                    bmp.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), buffer, bmp.PixelWidth * 4, 0);
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource ApplyOverlayImage(IGamePackage package, ImageSource image, params string[] args)
        {
            if (package == null)
                return image;

            if (args.Length >= 1)
            {
                ImageSource overlay = GetImage(package.Open(args[0]));
                if (overlay != null)
                    return ApplyOverlayImage(image, overlay);
            }

            return image;
        }

        public static ImageSource ApplyOverlayImage(ImageSource image, ImageSource overlay)
        {
            if (overlay == null)
                return image;

            if (image == null)
                return overlay;

            if (image == null && overlay == null)
                return null;

            WriteableBitmap bmp = new WriteableBitmap((BitmapSource)image);
            WriteableBitmap overlayBMP = new WriteableBitmap((BitmapSource)overlay);

            if (overlayBMP.PixelWidth != bmp.PixelWidth || overlayBMP.PixelHeight != bmp.PixelHeight)
            {
                ScriptManager.Instance.OutputError("Not applying overlay to base image because dimensions don't match.");
                return image;
            }

            byte[] buffer = new byte[bmp.PixelHeight * bmp.PixelWidth * 4];
            bmp.CopyPixels(buffer, bmp.PixelWidth * 4, 0);

            byte[] overlayBuffer = new byte[overlayBMP.PixelHeight * overlayBMP.PixelWidth * 4];
            overlayBMP.CopyPixels(overlayBuffer, bmp.PixelWidth * 4, 0);

            for (int y = 0; y < bmp.PixelHeight; ++y)
            {
                for (int x = 0; x < bmp.PixelWidth; ++x)
                {
                    byte b = buffer[(y * bmp.BackBufferStride + x * 4) + 0];
                    byte g = buffer[(y * bmp.BackBufferStride + x * 4) + 1];
                    byte r = buffer[(y * bmp.BackBufferStride + x * 4) + 2];
                    byte srcAlpha = buffer[(y * bmp.BackBufferStride + x * 4) + 3];

                    byte ob = overlayBuffer[(y * overlayBMP.BackBufferStride + x * 4) + 0];
                    byte og = overlayBuffer[(y * overlayBMP.BackBufferStride + x * 4) + 1];
                    byte or = overlayBuffer[(y * overlayBMP.BackBufferStride + x * 4) + 2];
                    byte oa = overlayBuffer[(y * overlayBMP.BackBufferStride + x * 4) + 3];

                    float alpha = oa / 255.0f;
                    float invAlpha = 1.0f - alpha;

                    b = (byte)Math.Min(Math.Max((uint)((uint)ob * alpha + (uint)b * invAlpha), 0), 255);
                    g = (byte)Math.Min(Math.Max((uint)((uint)og * alpha + (uint)g * invAlpha), 0), 255);
                    r = (byte)Math.Min(Math.Max((uint)((uint)or * alpha + (uint)r * invAlpha), 0), 255);

                    buffer[(y * bmp.BackBufferStride + x * 4) + 0] = b;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 1] = g;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 2] = r;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 3] = Math.Max(srcAlpha, oa);
                }
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), buffer, bmp.PixelWidth * 4, 0);
            bmp.Freeze();

            return bmp;
        }

        private static byte Lerp(byte a, byte b, float factor)
        {
            if (factor <= 0.0f)
                return a;

            if (factor >= 1.0f)
                return b;

            float raw = ((float)a * (1.0f - factor)) + ((float)b * factor);
            byte value = (byte)(raw + 0.5f);

            return value;
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

        public static ImageSource MakeImageGrayscale(ImageSource image, LuminanceMode mode = LuminanceMode.Average, float saturation = 0.0f)
        {
            if (image == null)
                return null;

            WriteableBitmap bmp = new WriteableBitmap((BitmapSource)image);

            byte[] buffer = new byte[bmp.PixelHeight * bmp.PixelWidth * 4];
            bmp.CopyPixels(buffer, bmp.PixelWidth * 4, 0);

            for (int y = 0; y < bmp.PixelHeight; ++y)
            {
                for (int x = 0; x < bmp.PixelWidth; ++x)
                {
                    byte b = buffer[(y * bmp.BackBufferStride + x * 4) + 0];
                    byte g = buffer[(y * bmp.BackBufferStride + x * 4) + 1];
                    byte r = buffer[(y * bmp.BackBufferStride + x * 4) + 2];

                    byte bo = b;
                    byte go = g;
                    byte ro = r;

                    switch (mode)
                    {
                        case LuminanceMode.Avg:
                        case LuminanceMode.Average:
                            b = g = r = (byte)((b + g + r) / 3.0);
                            break;

                        case LuminanceMode.Max:
                            b = g = r = Math.Max(Math.Max(b, g), r);
                            break;

                        case LuminanceMode.Blue:
                            g = r = b;
                            break;

                        case LuminanceMode.Green:
                            b = r = g;
                            break;

                        case LuminanceMode.Red:
                            b = g = r;
                            break;

                        case LuminanceMode.BT709:
                            b = g = r = (byte)(((uint)r + (uint)r + (uint)b + (uint)g + (uint)g + (uint)g) / 6);
                            break;

                        case LuminanceMode.BT601:
                            b = g = r = (byte)(((uint)r + (uint)r + (uint)b + (uint)g + (uint)g + (uint)g) >> 3);
                            break;
                    }

                    

                    // b = g = r = Math.Max(Math.Max(b, g), r);
                    // 

                    b = Lerp(b, bo, saturation);
                    g = Lerp(g, go, saturation);
                    r = Lerp(r, ro, saturation);

                    buffer[(y * bmp.BackBufferStride + x * 4) + 0] = b;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 1] = g;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 2] = r;
                }
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), buffer, bmp.PixelWidth * 4, 0);
            bmp.Freeze();

            return bmp;
        }

        public static ImageSource AdjustSaturation(IGamePackage package, ImageSource image, params string[] args)
        {
            if (image == null)
                return null;

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


        public static ImageSource AdjustBrightness(IGamePackage package, ImageSource image, params string[] args)
        {
            if (image == null)
                return null;

            if (args.Length >= 1)
            {
                float brightness;
                if (float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out brightness))
                {
                    brightness = Math.Max(brightness, 0.0f);

                    WriteableBitmap bmp = new WriteableBitmap((BitmapSource)image);

                    byte[] buffer = new byte[bmp.PixelHeight * bmp.PixelWidth * 4];
                    bmp.CopyPixels(buffer, bmp.PixelWidth * 4, 0);

                    for (int y = 0; y < bmp.PixelHeight; ++y)
                    {
                        for (int x = 0; x < bmp.PixelWidth; ++x)
                        {
                            byte b = (byte)(Math.Min(255.0f, Math.Max(0.0f, (buffer[(y * bmp.BackBufferStride + x * 4) + 0] * brightness))));
                            byte g = (byte)(Math.Min(255.0f, Math.Max(0.0f, (buffer[(y * bmp.BackBufferStride + x * 4) + 1] * brightness))));
                            byte r = (byte)(Math.Min(255.0f, Math.Max(0.0f, (buffer[(y * bmp.BackBufferStride + x * 4) + 2] * brightness))));

                            buffer[(y * bmp.BackBufferStride + x * 4) + 0] = b;
                            buffer[(y * bmp.BackBufferStride + x * 4) + 1] = g;
                            buffer[(y * bmp.BackBufferStride + x * 4) + 2] = r;
                        }
                    }

                    bmp.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), buffer, bmp.PixelWidth * 4, 0);
                    bmp.Freeze();

                    return bmp;
                }
            }

            return image;
        }

        public static ImageSource MakeImageDim(ImageSource image, int divisor = 2)
        {
            if (image == null)
                return null;

            WriteableBitmap bmp = new WriteableBitmap((BitmapSource)image);

            byte[] buffer = new byte[bmp.PixelHeight * bmp.PixelWidth * 4];
            bmp.CopyPixels(buffer, bmp.PixelWidth * 4, 0);

            for (int y = 0; y < bmp.PixelHeight; ++y)
            {
                for (int x = 0; x < bmp.PixelWidth; ++x)
                {
                    byte b = (byte)(buffer[(y * bmp.BackBufferStride + x * 4) + 0] - buffer[(y * bmp.BackBufferStride + x * 4) + 0] / divisor);
                    byte g = (byte)(buffer[(y * bmp.BackBufferStride + x * 4) + 1] - buffer[(y * bmp.BackBufferStride + x * 4) + 1] / divisor);
                    byte r = (byte)(buffer[(y * bmp.BackBufferStride + x * 4) + 2] - buffer[(y * bmp.BackBufferStride + x * 4) + 2] / divisor);

                    buffer[(y * bmp.BackBufferStride + x * 4) + 0] = b;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 1] = g;
                    buffer[(y * bmp.BackBufferStride + x * 4) + 2] = r;
                }
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), buffer, bmp.PixelWidth * 4, 0);
            bmp.Freeze();

            return bmp;
        }

        public static ImageSource ApplyFilterSpecToImage(IGamePackage package, ImageSource image, string filterSpec)
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
                        {
                            image = IconUtility.MakeImageGrayscale(image);
                        }
                        else if (mod.StartsWith("dim", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.MakeImageDim(image);
                        }
                        else if (mod.StartsWith("halfdim", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.MakeImageDim(image, 4);
                        }
                        else if (mod.StartsWith("quarterdim", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.MakeImageDim(image, 8);
                        }
                        else if (mod.StartsWith("brightness", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.AdjustBrightness(package, image, args);
                        }
                        else if (mod.StartsWith("overlay", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.ApplyOverlayImage(package, image, args);
                        }
                        else if (mod.StartsWith("saturation", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.AdjustSaturation(package, image, args);
                        }
                        else if (mod.StartsWith("@disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            image = IconUtility.ApplyFilterSpecToImage(package, image, Tracker.Instance.DisabledImageFilterSpec);
                        }
                    }
                }
            }

            if (image != null)
                image.Freeze();

            return image;
        }

        public static string[] GetArgs(string filterCommand)
        {
            string[] args = filterCommand.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < args.Length; ++i)
            {
                args[i] = args[i].Trim();
            }

            return args;
        }
    }
}
