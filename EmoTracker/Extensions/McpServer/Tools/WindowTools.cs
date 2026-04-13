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
    public class WindowTools
    {
        [McpServerTool(Name = "get_layout_scale")]
        [Description("Get the current main layout zoom level (100-500%)")]
        public static async Task<string> GetLayoutScale()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var scale = ApplicationModel.Instance.MainLayoutScale;
                return JsonSerializer.Serialize(new { scale, scaleFactor = scale / 100.0 });
            });
        }

        [McpServerTool(Name = "set_layout_scale")]
        [Description("Set the main layout zoom level (100-500%)")]
        public static async Task<string> SetLayoutScale(
            [Description("Zoom percentage (100-500)")] int scale)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (scale < 100 || scale > 500)
                        return JsonSerializer.Serialize(new { success = false, error = "Scale must be between 100 and 500" });

                    ApplicationModel.Instance.MainLayoutScale = scale;
                    return JsonSerializer.Serialize(new { success = true, scale, scaleFactor = scale / 100.0 });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "open_broadcast_view")]
        [Description("Open the broadcast view window")]
        public static async Task<string> OpenBroadcastView()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    ApplicationModel.Instance.ShowBroadcastViewCommand?.Execute(null);
                    return JsonSerializer.Serialize(new { success = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "close_broadcast_view")]
        [Description("Close the broadcast view window if it is open")]
        public static async Task<string> CloseBroadcastView()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (lifetime == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Application lifetime not available" });

                    foreach (var win in lifetime.Windows)
                    {
                        if (win is UI.BroadcastView bv)
                        {
                            bv.Close();
                            return JsonSerializer.Serialize(new { success = true });
                        }
                    }

                    return JsonSerializer.Serialize(new { success = false, error = "Broadcast view is not open" });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "open_developer_console")]
        [Description("Open the developer console window")]
        public static async Task<string> OpenDeveloperConsole()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    ApplicationModel.Instance.ShowDeveloperConsoleCommand?.Execute(null);
                    return JsonSerializer.Serialize(new { success = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "capture_developer_console")]
        [Description("Capture the developer console window as a base64-encoded PNG screenshot")]
        public static async Task<string> CaptureDeveloperConsole()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (lifetime == null)
                        return JsonSerializer.Serialize(new { error = "Application lifetime not available" });

                    Window consoleWindow = null;
                    foreach (var win in lifetime.Windows)
                    {
                        if (win is UI.DeveloperConsole)
                        {
                            consoleWindow = win;
                            break;
                        }
                    }

                    if (consoleWindow == null)
                        return JsonSerializer.Serialize(new { error = "Developer console is not open" });

                    return CaptureWindow(consoleWindow);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
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
