using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmoTracker.Data;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class UiAutomationTools
    {
        [McpServerTool(Name = "click_at")]
        [Description("Simulate a mouse click at pixel coordinates on the main window. Coordinates are relative to the window client area.")]
        public static async Task<string> ClickAt(
            [Description("X coordinate in pixels")] double x,
            [Description("Y coordinate in pixels")] double y,
            [Description("Mouse button: 'left', 'right', or 'middle'")] string button = "left")
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var window = GetMainWindow();
                    if (window == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Main window not found" });

                    var point = new Point(x, y);
                    var target = window.InputHitTest(point) as InputElement;

                    if (target == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"No input element at ({x}, {y})" });

                    var pointerButton = button?.ToLowerInvariant() switch
                    {
                        "right" => MouseButton.Right,
                        "middle" => MouseButton.Middle,
                        _ => MouseButton.Left
                    };

                    // Use direct item interaction for tracker items rather than
                    // synthetic pointer events (which require an IPointer instance)
                    if (target.DataContext is ITrackableItem trackable)
                    {
                        if (pointerButton == MouseButton.Left)
                            trackable.OnLeftClick();
                        else if (pointerButton == MouseButton.Right)
                            trackable.OnRightClick();
                    }
                    else
                    {
                        // For non-trackable controls, try raising tapped event
                        var tappedArgs = new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Input.InputElement.TappedEvent);
                        target.RaiseEvent(tappedArgs);
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        targetType = target.GetType().Name,
                        targetName = (target as Control)?.Name
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "send_key")]
        [Description("Simulate keyboard input on the main window. Key is an Avalonia Key enum name (e.g. 'F5', 'A', 'Enter'). Modifiers: 'ctrl', 'shift', 'alt', comma-separated.")]
        public static async Task<string> SendKey(
            [Description("Key name from Avalonia Key enum (e.g. 'F5', 'A', 'Enter')")] string key,
            [Description("Comma-separated modifiers: 'ctrl', 'shift', 'alt', 'meta'")] string modifiers = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var window = GetMainWindow();
                    if (window == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Main window not found" });

                    if (!Enum.TryParse<Key>(key, true, out var parsedKey))
                        return JsonSerializer.Serialize(new { success = false, error = $"Unknown key: {key}" });

                    var keyMods = KeyModifiers.None;
                    if (!string.IsNullOrEmpty(modifiers))
                    {
                        foreach (var mod in modifiers.Split(',', StringSplitOptions.TrimEntries))
                        {
                            switch (mod.ToLowerInvariant())
                            {
                                case "ctrl":
                                case "control":
                                    keyMods |= KeyModifiers.Control;
                                    break;
                                case "shift":
                                    keyMods |= KeyModifiers.Shift;
                                    break;
                                case "alt":
                                    keyMods |= KeyModifiers.Alt;
                                    break;
                                case "meta":
                                case "win":
                                    keyMods |= KeyModifiers.Meta;
                                    break;
                            }
                        }
                    }

                    var downArgs = new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = parsedKey,
                        KeyModifiers = keyMods
                    };
                    window.RaiseEvent(downArgs);

                    var upArgs = new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyUpEvent,
                        Key = parsedKey,
                        KeyModifiers = keyMods
                    };
                    window.RaiseEvent(upArgs);

                    return JsonSerializer.Serialize(new { success = true, key, modifiers });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "get_window_bounds")]
        [Description("Get the main window position and size")]
        public static async Task<string> GetWindowBounds()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = GetMainWindow();
                if (window == null)
                    return JsonSerializer.Serialize(new { error = "Main window not found" });

                return JsonSerializer.Serialize(new
                {
                    x = window.Position.X,
                    y = window.Position.Y,
                    width = window.Bounds.Width,
                    height = window.Bounds.Height,
                    clientWidth = window.ClientSize.Width,
                    clientHeight = window.ClientSize.Height
                });
            });
        }

        [McpServerTool(Name = "list_ui_elements")]
        [Description("List visible interactive UI elements in the main window with their bounds. Use type filter: 'all', 'buttons', 'named'.")]
        public static async Task<string> ListUiElements(
            [Description("Filter type: 'all', 'buttons', or 'named' (default)")] string type = "named")
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = GetMainWindow();
                if (window == null)
                    return JsonSerializer.Serialize(new { error = "Main window not found" });

                var elements = new List<object>();
                WalkVisualTree(window, window, elements, type ?? "named");

                return JsonSerializer.Serialize(elements);
            });
        }

        private static void WalkVisualTree(Visual node, Window root, List<object> elements, string filter)
        {
            if (node is Control control && control.IsVisible)
            {
                bool include = false;

                switch (filter.ToLowerInvariant())
                {
                    case "all":
                        include = control is InputElement;
                        break;
                    case "buttons":
                        include = control is Avalonia.Controls.Button ||
                                  control is Avalonia.Controls.Primitives.ToggleButton;
                        break;
                    case "named":
                    default:
                        include = !string.IsNullOrEmpty(control.Name);
                        break;
                }

                if (include)
                {
                    try
                    {
                        var transform = control.TransformToVisual(root);
                        if (transform.HasValue)
                        {
                            var topLeft = transform.Value.Transform(new Point(0, 0));
                            var bottomRight = transform.Value.Transform(
                                new Point(control.Bounds.Width, control.Bounds.Height));
                            elements.Add(new
                            {
                                name = control.Name,
                                type = control.GetType().Name,
                                x = topLeft.X,
                                y = topLeft.Y,
                                width = bottomRight.X - topLeft.X,
                                height = bottomRight.Y - topLeft.Y,
                                isEnabled = control.IsEnabled,
                                dataContext = control.DataContext?.GetType().Name
                            });
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (node is Visual visual)
            {
                foreach (var child in visual.GetVisualChildren())
                {
                    WalkVisualTree(child, root, elements, filter);
                }
            }
        }

        private static Window GetMainWindow()
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            return lifetime?.MainWindow;
        }
    }
}
