using EmoTracker.Core;
using EmoTracker.Data.Debugging;
using Newtonsoft.Json.Linq;
using Serilog;
using System;

namespace EmoTracker.Extensions.LuaDebugger
{
    /// <summary>
    /// Application-scoped extension that hosts the in-process
    /// Lua DAP server. Listens on port 27126 (override via
    /// <c>EMOTRACKER_DAP_PORT</c>). Dev-mode only — disabled
    /// completely outside <c>UserDirectory.IsDevMode</c> the same way
    /// the MCP server is, so production builds carry zero TCP
    /// surface from this code path.
    ///
    /// <para>
    /// The companion VS Code extension <c>emotracker-lua-debug</c>
    /// (under <c>vscode-extensions/</c>) connects on attach and
    /// drives breakpoints / stepping / variable inspection against
    /// each TrackerState's interpreter.
    /// </para>
    /// </summary>
    public class LuaDebuggerExtension : ObservableObject, IApplicationExtension
    {
        const int DefaultPort = 27126;

        public string Name => "Lua Debugger";
        public string UID => "emotracker_lua_debugger";
        public int Priority => -490;

        private bool mbActive;
        public bool Active
        {
            get => mbActive;
            private set => SetProperty(ref mbActive, value);
        }

        private string mStatusText = "Stopped";
        public string StatusText
        {
            get => mStatusText;
            private set => SetProperty(ref mStatusText, value);
        }

        // No status-bar surface yet — uses the same dev-mode-only
        // gate as MCP and stays silent until we want a green-when-
        // attached indicator. Returning null opts out cleanly.
        public object StatusBarControl => null;

        LuaDebugServer mServer;

        public void Start(IApplicationContext app)
        {
            if (!UserDirectory.IsDevMode)
                return;

            try
            {
                int port = DefaultPort;
                string portEnv = Environment.GetEnvironmentVariable("EMOTRACKER_DAP_PORT");
                if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out int envPort))
                    port = envPort;

                // Bridge debug-layer trace output into Serilog so
                // EMOTRACKER_DAP_TRACE=1 surfaces in the same log
                // sink as the rest of the app (visible to a debug
                // build via the developer terminal + Serilog file
                // sink). Without this bridge, Console.WriteLine
                // would be swallowed because EmoTracker is a WinExe
                // and never attaches a console.
                LuaDebuggee.Sink = msg => Log.Information(msg);

                mServer = new LuaDebugServer(port);
                mServer.Start();
                Active = true;
                StatusText = $"Listening on port {port}";
                Log.Information("[LuaDbg] DAP server listening on port {Port}", port);
                if (LuaDebuggee.sTrace)
                    Log.Information("[LuaDbg] EMOTRACKER_DAP_TRACE is on — verbose tracing enabled");
            }
            catch (Exception ex)
            {
                Active = false;
                StatusText = $"Error: {ex.Message}";
                Log.Error(ex, "[LuaDbg] Failed to start DAP server");
            }
        }

        public void Stop()
        {
            try { mServer?.Stop(); } catch { }
            mServer = null;
            Active = false;
            StatusText = "Stopped";
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
