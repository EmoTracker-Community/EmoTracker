using Avalonia.Threading;
using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Session;
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
    public class SaveLoadTools
    {
        [McpServerTool(Name = "save_progress")]
        [Description("Save the current tracker state to a file. If no path is given, saves to the default saves directory.")]
        public static async Task<string> SaveProgress(
            [Description("File path to save to. If just a filename, saves to the default saves directory.")] string path)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    string savePath = path;
                    if (!Path.IsPathRooted(savePath))
                    {
                        string savesDir = Path.Combine(UserDirectory.Path, "saves");
                        Directory.CreateDirectory(savesDir);
                        savePath = Path.Combine(savesDir, savePath);
                    }

                    if (!savePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        savePath += ".json";

                    bool result = TrackerSession.Current.Tracker.SaveProgress(savePath);
                    return JsonSerializer.Serialize(new { success = result, path = savePath });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "load_progress")]
        [Description("Load tracker state from a save file. If just a filename, loads from the default saves directory.")]
        public static async Task<string> LoadProgress(
            [Description("File path to load from")] string path)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    string loadPath = path;
                    if (!Path.IsPathRooted(loadPath))
                    {
                        loadPath = Path.Combine(UserDirectory.Path, "saves", loadPath);
                    }

                    if (!File.Exists(loadPath))
                        return JsonSerializer.Serialize(new { success = false, error = $"File not found: {loadPath}" });

                    bool result = TrackerSession.Current.Tracker.LoadProgress(loadPath);
                    return JsonSerializer.Serialize(new { success = result, path = loadPath });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "list_save_files")]
        [Description("List all save files in the default saves directory")]
        public static async Task<string> ListSaveFiles()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    string savesDir = Path.Combine(UserDirectory.Path, "saves");
                    if (!Directory.Exists(savesDir))
                        return JsonSerializer.Serialize(Array.Empty<object>());

                    var files = Directory.GetFiles(savesDir, "*.json")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Select(f => new
                        {
                            name = f.Name,
                            path = f.FullName,
                            size = f.Length,
                            lastModified = f.LastWriteTime.ToString("o")
                        })
                        .ToArray();

                    return JsonSerializer.Serialize(files);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }
    }
}
