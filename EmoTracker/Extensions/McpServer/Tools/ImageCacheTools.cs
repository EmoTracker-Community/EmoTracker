using Avalonia.Threading;
using EmoTracker.UI.Media;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class ImageCacheTools
    {
        [McpServerTool(Name = "get_image_queue_status")]
        [Description("Get the current status of the async image resolution queue, including queue depth, cache size, and whether sync mode is active")]
        public static async Task<string> GetImageQueueStatus()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var service = ImageReferenceService.Instance;
                return JsonSerializer.Serialize(new
                {
                    syncMode = service.SyncMode,
                    queueCount = service.QueueCount,
                    cacheCount = service.CacheCount
                });
            });
        }
    }
}
