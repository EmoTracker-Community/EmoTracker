using Avalonia.Threading;
using EmoTracker.Data;
using ModelContextProtocol.Server;
using NLua;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class LuaTools
    {
        [McpServerTool(Name = "get_console_log")]
        [Description("Read the developer console log output. Returns the most recent log lines.")]
        public static async Task<string> GetConsoleLog(
            [Description("Number of most recent lines to return")] int lastN = 50)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var logOutput = ScriptManager.Instance.LogOutput;
                if (logOutput == null)
                    return JsonSerializer.Serialize(Array.Empty<object>());

                var lines = logOutput.Cast<ScriptManager.LogLine>().ToList();
                if (lastN > 0 && lines.Count > lastN)
                    lines = lines.Skip(lines.Count - lastN).ToList();

                var result = lines.Select(l => new
                {
                    text = l.Text,
                    color = l.Color
                }).ToArray();

                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "execute_lua")]
        [Description("Execute Lua code in the pack's Lua scripting environment. Returns the results or error.")]
        public static async Task<string> ExecuteLua(
            [Description("The Lua code to execute")] string code)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (!ScriptManager.Instance.IsLuaLoaded)
                        return JsonSerializer.Serialize(new { error = "No Lua environment loaded (no pack active)" });

                    var results = ScriptManager.Instance.ExecuteLuaString(code);
                    if (results == null || results.Length == 0)
                        return JsonSerializer.Serialize(new { results = Array.Empty<string>() });

                    var stringResults = results.Select(r => SerializeLuaValue(r)).ToArray();
                    return JsonSerializer.Serialize(new { results = stringResults });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "get_lua_global")]
        [Description("Read a Lua global variable's value by name")]
        public static async Task<string> GetLuaGlobal(
            [Description("The Lua global variable name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (!ScriptManager.Instance.IsLuaLoaded)
                        return JsonSerializer.Serialize(new { error = "No Lua environment loaded (no pack active)" });

                    var value = ScriptManager.Instance.GetLuaGlobal(name);
                    return JsonSerializer.Serialize(new
                    {
                        name,
                        value = SerializeLuaValue(value),
                        type = GetLuaTypeName(value)
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

        private static string SerializeLuaValue(object value)
        {
            if (value == null)
                return "nil";
            if (value is LuaTable table)
            {
                var entries = new List<string>();
                var keys = table.Keys;
                foreach (var key in keys)
                {
                    var val = table[key];
                    entries.Add($"{key} = {SerializeLuaValue(val)}");
                }
                return "{" + string.Join(", ", entries) + "}";
            }
            if (value is LuaFunction)
                return "<function>";
            if (value is bool b)
                return b ? "true" : "false";
            return value.ToString();
        }

        private static string GetLuaTypeName(object value)
        {
            if (value == null) return "nil";
            if (value is bool) return "boolean";
            if (value is string) return "string";
            if (value is double || value is long || value is int || value is float) return "number";
            if (value is LuaTable) return "table";
            if (value is LuaFunction) return "function";
            return value.GetType().Name;
        }
    }
}
