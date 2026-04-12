using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class ScreenshotTools
    {
        [McpServerTool(Name = "capture_main_window")]
        [Description("Capture the main tracker window as a base64-encoded PNG screenshot")]
        public static async Task<string> CaptureMainWindow()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var window = GetMainWindow();
                    if (window == null)
                        return JsonSerializer.Serialize(new { error = "Main window not found" });

                    return CaptureWindow(window);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "capture_broadcast_view")]
        [Description("Capture the broadcast view window as a base64-encoded PNG screenshot")]
        public static async Task<string> CaptureBroadcastView()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (lifetime == null)
                        return JsonSerializer.Serialize(new { error = "Application lifetime not available" });

                    Window broadcastWindow = null;
                    foreach (var win in lifetime.Windows)
                    {
                        if (win is UI.BroadcastView)
                        {
                            broadcastWindow = win;
                            break;
                        }
                    }

                    if (broadcastWindow == null)
                        return JsonSerializer.Serialize(new { error = "Broadcast view is not open" });

                    return CaptureWindow(broadcastWindow);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

        private static Window GetMainWindow()
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            return lifetime?.MainWindow;
        }

        private static string CaptureWindow(Window window)
        {
            var pixelSize = new PixelSize(
                Math.Max(1, (int)window.Bounds.Width),
                Math.Max(1, (int)window.Bounds.Height));

            var dpi = new Vector(96, 96);
            if (window.Screens?.Primary != null)
            {
                var scaling = window.Screens.Primary.Scaling;
                dpi = new Vector(96 * scaling, 96 * scaling);
                pixelSize = new PixelSize(
                    Math.Max(1, (int)(window.Bounds.Width * scaling)),
                    Math.Max(1, (int)(window.Bounds.Height * scaling)));
            }

            var renderTarget = new RenderTargetBitmap(pixelSize, dpi);
            renderTarget.Render(window);

            using var ms = new MemoryStream();
            renderTarget.Save(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());

            return JsonSerializer.Serialize(new
            {
                image = base64,
                width = pixelSize.Width,
                height = pixelSize.Height,
                format = "png"
            });
        }
    }
}
