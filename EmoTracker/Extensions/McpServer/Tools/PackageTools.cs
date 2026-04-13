using Avalonia.Threading;
using EmoTracker.Data;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class PackageTools
    {
        [McpServerTool(Name = "get_pack_files")]
        [Description("List all files in the currently loaded game pack")]
        public static async Task<string> GetPackFiles()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var pack = Tracker.Instance.ActiveGamePackage;
                if (pack == null)
                    return JsonSerializer.Serialize(new { error = "No pack loaded" });

                var source = pack.Source;
                if (source == null)
                    return JsonSerializer.Serialize(new { error = "Pack source not available" });

                var files = source.Files?.ToList() ?? new List<string>();
                return JsonSerializer.Serialize(new
                {
                    packName = pack.DisplayName,
                    fileCount = files.Count,
                    files
                });
            });
        }

        [McpServerTool(Name = "get_pack_file_content")]
        [Description("Read a text file from the currently loaded game pack (e.g. Lua scripts, JSON layouts)")]
        public static async Task<string> GetPackFileContent(
            [Description("Path of the file within the pack (e.g. 'scripts/init.lua', 'manifest.json')")] string path)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var pack = Tracker.Instance.ActiveGamePackage;
                    if (pack == null)
                        return JsonSerializer.Serialize(new { error = "No pack loaded" });

                    using var stream = pack.Open(path);
                    if (stream == null)
                        return JsonSerializer.Serialize(new { error = $"File not found: {path}" });

                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();

                    return JsonSerializer.Serialize(new
                    {
                        path,
                        size = content.Length,
                        content
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "reload_pack")]
        [Description("Reload the currently loaded pack (equivalent to pressing F5)")]
        public static async Task<string> ReloadPack()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var pack = Tracker.Instance.ActiveGamePackage;
                    if (pack == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No pack loaded" });

                    Tracker.Instance.Reload();
                    return JsonSerializer.Serialize(new { success = true, packName = pack.DisplayName });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }
    }
}
