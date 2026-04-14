using Avalonia.Threading;
using EmoTracker.Data.Scripting;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class NotificationTools
    {
        [McpServerTool(Name = "push_markdown_notification")]
        [Description("Display a markdown-formatted notification in the tracker UI. The notification appears in the in-app notification list and can be dismissed by the user.")]
        public static async Task<string> PushMarkdownNotification(
            [Description("Markdown source to render inside the notification.")] string markdown,
            [Description("Notification severity/style. One of: Message (default), Celebration, Warning, Error.")] string type = "Message",
            [Description("Auto-dismiss timeout in milliseconds. Use -1 (default) for a sticky notification that the user must dismiss manually.")] int timeout = -1)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return JsonSerializer.Serialize(new { success = false, error = "markdown is required" });

            if (!Enum.TryParse<NotificationType>(type, ignoreCase: true, out var parsedType))
                return JsonSerializer.Serialize(new { success = false, error = $"Unknown notification type '{type}'. Expected one of: Message, Celebration, Warning, Error." });

            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    ApplicationModel.Instance.PushMarkdownNotification(parsedType, markdown, timeout);
                    return JsonSerializer.Serialize(new { success = true, type = parsedType.ToString(), timeout });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }
    }
}
