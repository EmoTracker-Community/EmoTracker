using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Notes;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class NoteTools
    {
        [McpServerTool(Name = "add_note")]
        [Description("Add a text note to a location")]
        public static async Task<string> AddNote(
            [Description("The location name")] string locationName,
            [Description("The note text (supports markdown)")] string text)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var loc = LocationDatabase.Instance.FindLocation(locationName);
                    if (loc == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{locationName}' not found" });

                    if (loc.NoteTakingSite == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Note taking not available for this location" });

                    var note = new MarkdownTextWithItemsNote();
                    note.MarkdownSource = text;
                    loc.NoteTakingSite.AddNote(note);

                    return JsonSerializer.Serialize(new { success = true, location = loc.Name });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "get_notes")]
        [Description("Get all notes for a location")]
        public static async Task<string> GetNotes([Description("The location name")] string locationName)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var loc = LocationDatabase.Instance.FindLocation(locationName);
                if (loc == null)
                    return JsonSerializer.Serialize(new { found = false, error = $"Location '{locationName}' not found" });

                if (loc.NoteTakingSite == null)
                    return JsonSerializer.Serialize(new { found = true, notes = Array.Empty<object>() });

                var notes = new List<object>();
                foreach (var note in loc.NoteTakingSite.Notes)
                {
                    if (note is MarkdownTextNote mdNote)
                    {
                        var items = new List<string>();
                        if (note is MarkdownTextWithItemsNote itemNote)
                        {
                            foreach (var item in itemNote.Items)
                            {
                                if (item != null)
                                    items.Add(item.Name);
                            }
                        }

                        notes.Add(new
                        {
                            type = note.GetType().Name,
                            text = mdNote.MarkdownSource,
                            items,
                            readOnly = note.ReadOnly
                        });
                    }
                    else
                    {
                        notes.Add(new { type = note.GetType().Name, readOnly = note.ReadOnly });
                    }
                }

                return JsonSerializer.Serialize(new { found = true, location = loc.Name, notes });
            });
        }

        [McpServerTool(Name = "clear_notes")]
        [Description("Clear all notes from a location")]
        public static async Task<string> ClearNotes([Description("The location name")] string locationName)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var loc = LocationDatabase.Instance.FindLocation(locationName);
                    if (loc == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{locationName}' not found" });

                    if (loc.NoteTakingSite == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Note taking not available for this location" });

                    loc.NoteTakingSite.Clear();
                    return JsonSerializer.Serialize(new { success = true, location = loc.Name });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }
    }
}
