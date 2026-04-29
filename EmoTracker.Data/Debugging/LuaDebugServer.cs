using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Data.Debugging
{
    /// <summary>
    /// In-process Debug Adapter Protocol server. Listens on a TCP port
    /// (default 27126, override with <c>EMOTRACKER_DAP_PORT</c>) and
    /// hosts at most one connected DAP client at a time. The companion
    /// VS Code extension <c>emotracker-lua-debug</c> connects on
    /// attach.
    ///
    /// <para>
    /// Singleton owned by <c>LuaDebugServerExtension</c> in the app
    /// project; per-state debuggees register themselves via
    /// <see cref="RegisterDebuggee"/> when their interpreter
    /// bootstraps and unregister on Reset. The server fans out
    /// breakpoint state to every registered debuggee and routes
    /// per-thread DAP requests by <see cref="LuaDebuggee.Id"/>.
    /// </para>
    /// </summary>
    public sealed class LuaDebugServer : IDisposable
    {
        public const int DefaultPort = 27126;

        // Single global server. Constructed by the app extension on
        // startup when dev-mode is on; ScriptManager.BootstrapInterpreter
        // consults this slot to decide whether to register a debuggee.
        public static LuaDebugServer Instance { get; private set; }

        readonly int mPort;
        TcpListener mListener;
        CancellationTokenSource mShutdownCts;

        // Per-state debuggees, keyed by stable id. Ids are handed out
        // monotonically by Interlocked.Increment so they survive a
        // session's lifetime (and double as DAP "thread" ids).
        readonly ConcurrentDictionary<int, LuaDebuggee> mDebuggees = new();
        int mNextDebuggeeId;

        // The active DAP session, if any. We accept exactly one client
        // at a time — DAP convention. New connections close the old.
        LuaDebugSession mActiveSession;
        readonly object mSessionLock = new();

        public bool IsRunning => mListener != null;
        public int Port => mPort;

        /// <summary>True iff a DAP client is currently attached.</summary>
        public bool HasActiveSession
        {
            get { lock (mSessionLock) return mActiveSession != null; }
        }

        public LuaDebugServer(int port = DefaultPort)
        {
            mPort = port;
        }

        /// <summary>
        /// Start the TCP listener. Idempotent — repeated calls no-op
        /// once <see cref="IsRunning"/> is true. Sets
        /// <see cref="Instance"/> on success so per-state code can
        /// reach the singleton without a DI container.
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            mShutdownCts = new CancellationTokenSource();
            mListener = new TcpListener(IPAddress.Loopback, mPort);
            mListener.Start();
            Instance = this;

            // Accept loop on the thread pool. Every accepted client
            // gets its own LuaDebugSession and its own reader/writer
            // tasks; the session owns the lifetime of the socket.
            _ = Task.Run(() => AcceptLoopAsync(mShutdownCts.Token));
        }

        /// <summary>
        /// Stop the listener and tear down any active session.
        /// Debuggees stay registered — they're reusable across
        /// multiple DAP attach/detach cycles for the lifetime of the
        /// owning <c>ScriptManager</c>.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            try { mShutdownCts.Cancel(); } catch { }
            try { mListener.Stop(); } catch { }
            mListener = null;

            LuaDebugSession session;
            lock (mSessionLock)
            {
                session = mActiveSession;
                mActiveSession = null;
            }
            session?.Dispose();

            if (Instance == this) Instance = null;
        }

        public void Dispose() => Stop();

        async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await mListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch { continue; } // transient — keep listening

                // Boot the previous session if a new client connects;
                // DAP protocol is one-client-at-a-time.
                LuaDebugSession previous;
                LuaDebugSession session = new LuaDebugSession(this, client);
                lock (mSessionLock)
                {
                    previous = mActiveSession;
                    mActiveSession = session;
                }
                previous?.Dispose();

                _ = Task.Run(() => session.RunAsync(ct));
            }
        }

        /// <summary>
        /// Called by <see cref="LuaDebugSession"/> when its socket
        /// closes (client detached, error, etc.). Lets us drop the
        /// reference so <see cref="HasActiveSession"/> reflects truth
        /// without waiting for the next attach.
        /// </summary>
        internal void NotifySessionClosed(LuaDebugSession session)
        {
            lock (mSessionLock)
            {
                if (mActiveSession == session)
                    mActiveSession = null;
            }
            // Clear pause state on every debuggee so a stale session
            // doesn't leave Lua threads blocked.
            foreach (var d in mDebuggees.Values)
                d.OnSessionDetached();
        }

        // -- Debuggee registry ----------------------------------------

        /// <summary>
        /// Register a freshly-bootstrapped per-state debuggee. Called
        /// from <c>ScriptManager.BootstrapInterpreter</c> (after the
        /// Lua state is alive) and from the fork path. Returns the
        /// assigned id; callers store it on the debuggee.
        /// </summary>
        public int RegisterDebuggee(LuaDebuggee debuggee)
        {
            int id = Interlocked.Increment(ref mNextDebuggeeId);
            debuggee.Id = id;
            mDebuggees[id] = debuggee;

            // Tell the active session there's a new "thread" so VS
            // Code can refresh its threads view, and replay current
            // breakpoint/exception state onto the new debuggee so
            // post-fork interpreters honor breakpoints set before
            // they existed.
            LuaDebugSession session;
            lock (mSessionLock) session = mActiveSession;
            if (session != null)
            {
                session.ReplayStateOnto(debuggee);
                session.SendThreadEvent("started", id);
            }

            return id;
        }

        /// <summary>
        /// Drop a debuggee on <c>ScriptManager.Reset</c> /
        /// <c>Dispose</c>. Sends a DAP "thread exited" event so VS
        /// Code prunes the entry.
        /// </summary>
        public void UnregisterDebuggee(LuaDebuggee debuggee)
        {
            if (debuggee == null) return;
            if (mDebuggees.TryRemove(debuggee.Id, out _))
            {
                LuaDebugSession session;
                lock (mSessionLock) session = mActiveSession;
                session?.SendThreadEvent("exited", debuggee.Id);
            }
        }

        public LuaDebuggee GetDebuggee(int id)
        {
            mDebuggees.TryGetValue(id, out var d);
            return d;
        }

        public IEnumerable<LuaDebuggee> AllDebuggees() => mDebuggees.Values;

        /// <summary>
        /// Called by a debuggee when it transitions into the paused
        /// state. We forward a DAP "stopped" event to the active
        /// session (if any).
        /// </summary>
        internal void NotifyStopped(LuaDebuggee debuggee, string reason, string description = null, string text = null)
        {
            LuaDebugSession session;
            lock (mSessionLock) session = mActiveSession;
            session?.SendStoppedEvent(debuggee.Id, reason, description, text);
        }

        /// <summary>
        /// Called by a debuggee when it resumes. Used to send
        /// "continued" events when continuation is initiated by
        /// something other than the attached DAP client (e.g.
        /// inferior signal — not yet a path we exercise, but the
        /// hook is here for the future).
        /// </summary>
        internal void NotifyContinued(LuaDebuggee debuggee)
        {
            LuaDebugSession session;
            lock (mSessionLock) session = mActiveSession;
            session?.SendContinuedEvent(debuggee.Id);
        }

        // -- Wire format ---------------------------------------------

        // Newtonsoft instance shared across the server. Uses default
        // settings — we encode field names verbatim per JsonProperty
        // attributes on the DapTypes records.
        internal static readonly JsonSerializer Serializer = JsonSerializer.CreateDefault();

        /// <summary>
        /// Read one DAP message off a stream framed with
        /// "Content-Length: N\r\n\r\n&lt;json&gt;". Returns null on
        /// graceful EOF.
        /// </summary>
        internal static async Task<DapMessage> ReadMessageAsync(Stream stream, CancellationToken ct)
        {
            // Read the header line-by-line until \r\n\r\n.
            int contentLength = -1;
            var headerBuf = new StringBuilder();
            while (true)
            {
                int b = await ReadOneByteAsync(stream, ct).ConfigureAwait(false);
                if (b < 0) return null;
                headerBuf.Append((char)b);

                if (headerBuf.Length >= 4 &&
                    headerBuf[headerBuf.Length - 4] == '\r' &&
                    headerBuf[headerBuf.Length - 3] == '\n' &&
                    headerBuf[headerBuf.Length - 2] == '\r' &&
                    headerBuf[headerBuf.Length - 1] == '\n')
                {
                    break;
                }
            }

            // Parse Content-Length out of the header block.
            string headers = headerBuf.ToString();
            foreach (var line in headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                string val = line.Substring(colon + 1).Trim();
                if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(val, out int parsed))
                        contentLength = parsed;
                }
            }
            if (contentLength < 0) return null;

            // Read exactly contentLength bytes of UTF-8 JSON.
            var body = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int got = await stream.ReadAsync(body, read, contentLength - read, ct).ConfigureAwait(false);
                if (got <= 0) return null;
                read += got;
            }

            string json = Encoding.UTF8.GetString(body);
            using var sr = new StringReader(json);
            using var jr = new JsonTextReader(sr);
            return Serializer.Deserialize<DapMessage>(jr);
        }

        static async Task<int> ReadOneByteAsync(Stream stream, CancellationToken ct)
        {
            var buf = new byte[1];
            int n = await stream.ReadAsync(buf, 0, 1, ct).ConfigureAwait(false);
            return n <= 0 ? -1 : buf[0];
        }

        /// <summary>
        /// Frame and send a DAP message. Caller is responsible for
        /// serializing concurrent writes — sessions hold a per-socket
        /// write lock.
        /// </summary>
        internal static void WriteMessage(Stream stream, DapMessage msg)
        {
            using var sw = new StringWriter();
            Serializer.Serialize(sw, msg);
            string json = sw.ToString();
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            string header = $"Content-Length: {bodyBytes.Length}\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }
    }
}
