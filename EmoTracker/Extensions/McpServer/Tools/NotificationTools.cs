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
        [McpServerTool(Name = "push_notification")]
        [Description("Push a markdown notification to the tracker UI. Type must be one of: Message, Celebration, Warning, Error. Timeout is in milliseconds (-1 = 10s default, 0 = never expires).")]
        public static async Task<string> PushNotification(
            [Description("Notification type: Message, Celebration, Warning, or Error")] string type,
            [Description("Markdown content for the notification")] string markdown,
            [Description("Timeout in milliseconds (-1 for default 10 seconds, 0 for no expiry)")] int timeout = -1)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (!Enum.TryParse<NotificationType>(type, ignoreCase: true, out var notifType))
                        return JsonSerializer.Serialize(new { success = false, error = $"Invalid type '{type}'. Must be: Message, Celebration, Warning, or Error." });

                    ApplicationModel.Instance.PushMarkdownNotification(notifType, markdown, timeout);
                    return JsonSerializer.Serialize(new { success = true, type = notifType.ToString(), markdown });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }
    }
}
