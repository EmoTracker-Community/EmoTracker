using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Data.Debugging
{
    /// <summary>
    /// One DAP client connection. Owns the socket, the read loop, and
    /// the request → response routing. Concurrent writes are
    /// serialized by <see cref="mWriteLock"/>; each write is a single
    /// framed message so partial-write tearing isn't possible.
    /// </summary>
    public sealed class LuaDebugSession : IDisposable
    {
        readonly LuaDebugServer mServer;
        readonly TcpClient mClient;
        readonly NetworkStream mStream;
        readonly object mWriteLock = new object();
        int mNextSeq;

        bool mDisposed;

        // Breakpoint state owned by the session. Lets us replay the
        // current set onto a debuggee that registers AFTER the user
        // already set breakpoints (typical for forked TrackerStates,
        // which spawn at runtime as the user opens additional tabs
        // or the autotracker forks the primary state).
        readonly object mBreakpointStateLock = new object();
        readonly Dictionary<string, List<int>> mBreakpointState = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        bool mBreakOnExceptionLatched;

        public LuaDebugSession(LuaDebugServer server, TcpClient client)
        {
            mServer = server;
            mClient = client;
            mStream = client.GetStream();
        }

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            try { mStream?.Dispose(); } catch { }
            try { mClient?.Close(); } catch { }
            mServer.NotifySessionClosed(this);
        }

        internal async Task RunAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && !mDisposed)
                {
                    var msg = await LuaDebugServer.ReadMessageAsync(mStream, ct).ConfigureAwait(false);
                    if (msg == null) break;
                    if (msg.Type != "request") continue;

                    try { Handle(msg); }
                    catch (Exception ex)
                    {
                        SendErrorResponse(msg, ex.Message);
                    }
                }
            }
            catch { /* socket closed / cancelled */ }
            finally { Dispose(); }
        }

        // -- Outbound ------------------------------------------------

        void SendResponse(DapMessage req, JObject body = null, bool success = true, string message = null)
        {
            var resp = new DapMessage
            {
                Seq = NextSeq(),
                Type = "response",
                RequestSeq = req.Seq,
                Success = success,
                Command = req.Command,
                Body = body,
                Message = message,
            };
            Write(resp);
        }

        void SendErrorResponse(DapMessage req, string message)
        {
            var resp = new DapMessage
            {
                Seq = NextSeq(),
                Type = "response",
                RequestSeq = req.Seq,
                Success = false,
                Command = req.Command,
                Message = message,
                Body = JObject.FromObject(new { error = new { format = message, showUser = false } }),
            };
            Write(resp);
        }

        void SendEvent(string eventName, JObject body)
        {
            var ev = new DapMessage
            {
                Seq = NextSeq(),
                Type = "event",
                Event = eventName,
                Body = body,
            };
            Write(ev);
        }

        internal void SendStoppedEvent(int threadId, string reason, string description = null, string text = null)
        {
            SendEvent("stopped", JObject.FromObject(new
            {
                reason,
                threadId,
                description,
                text,
                allThreadsStopped = false,
                preserveFocusHint = false,
            }));
        }

        internal void SendContinuedEvent(int threadId)
        {
            SendEvent("continued", JObject.FromObject(new { threadId, allThreadsContinued = false }));
        }

        internal void SendThreadEvent(string reason, int threadId)
        {
            SendEvent("thread", JObject.FromObject(new { reason, threadId }));
        }

        /// <summary>
        /// Replay the session's current breakpoint + exception-break
        /// state onto a freshly-registered debuggee. Called by the
        /// server when a new ScriptManager bootstraps mid-session
        /// (typically a TrackerState fork): without this, breakpoints
        /// set before the fork existed wouldn't fire on the fork's
        /// interpreter.
        /// </summary>
        internal void ReplayStateOnto(LuaDebuggee debuggee)
        {
            if (debuggee == null) return;
            lock (mBreakpointStateLock)
            {
                foreach (var kv in mBreakpointState)
                    debuggee.SetBreakpoints(kv.Key, kv.Value);
            }
            debuggee.BreakOnException = mBreakOnExceptionLatched;
        }

        void Write(DapMessage m)
        {
            if (mDisposed) return;
            try
            {
                lock (mWriteLock)
                    LuaDebugServer.WriteMessage(mStream, m);
            }
            catch
            {
                // Socket likely closed. Tear down.
                Dispose();
            }
        }

        int NextSeq() => Interlocked.Increment(ref mNextSeq);

        // -- Inbound dispatch ----------------------------------------

        void Handle(DapMessage req)
        {
            switch (req.Command)
            {
                case "initialize": OnInitialize(req); break;
                case "launch": OnAttach(req); break;        // we don't launch the debuggee — treat as attach
                case "attach": OnAttach(req); break;
                case "configurationDone": SendResponse(req); break;
                case "disconnect":
                    SendResponse(req);
                    Dispose();
                    break;
                case "threads": OnThreads(req); break;
                case "setBreakpoints": OnSetBreakpoints(req); break;
                case "setExceptionBreakpoints": OnSetExceptionBreakpoints(req); break;
                case "stackTrace": OnStackTrace(req); break;
                case "scopes": OnScopes(req); break;
                case "variables": OnVariables(req); break;
                case "evaluate": OnEvaluate(req); break;
                case "continue": OnContinueLike(req, DebugRequest.Kind.Continue); break;
                case "next": OnContinueLike(req, DebugRequest.Kind.StepOver); break;
                case "stepIn": OnContinueLike(req, DebugRequest.Kind.StepIn); break;
                case "stepOut": OnContinueLike(req, DebugRequest.Kind.StepOut); break;
                case "pause": OnPause(req); break;
                case "source": OnSource(req); break;
                default:
                    // Reply unsupported with success=true so VS Code
                    // doesn't surface red dots for things like
                    // "loadedSources" or "modules" we don't implement.
                    SendResponse(req);
                    break;
            }
        }

        // -- Handlers -----------------------------------------------

        void OnInitialize(DapMessage req)
        {
            // Capabilities we actually support today. Set explicit
            // false on the rest so VS Code surfaces a sensible UI
            // (e.g. no "set value on hover" if we don't handle it).
            var caps = JObject.FromObject(new
            {
                supportsConfigurationDoneRequest = true,
                supportsConditionalBreakpoints = false,
                supportsHitConditionalBreakpoints = false,
                supportsEvaluateForHovers = true,
                supportsSetVariable = false,
                supportsExceptionInfoRequest = false,
                supportsTerminateRequest = false,
                supportsRestartRequest = false,
                exceptionBreakpointFilters = new[]
                {
                    new { filter = "luaError", label = "Lua errors", @default = false }
                },
            });
            SendResponse(req, caps);
            SendEvent("initialized", new JObject());
        }

        void OnAttach(DapMessage req)
        {
            // Echo back. The actual "attachment" is just having the
            // socket open — debuggees were registered by their
            // ScriptManagers when they bootstrapped.
            SendResponse(req);
        }

        void OnThreads(DapMessage req)
        {
            var arr = new JArray();
            foreach (var d in mServer.AllDebuggees())
            {
                arr.Add(JObject.FromObject(new DapThread { Id = d.Id, Name = d.Name }));
            }
            SendResponse(req, JObject.FromObject(new { threads = arr }));
        }

        void OnSetBreakpoints(DapMessage req)
        {
            var args = req.Arguments;
            var src = args?["source"] as JObject;
            var breakpoints = args?["breakpoints"] as JArray;

            string sourcePath = src?["path"]?.Value<string>();
            string sourceName = src?["name"]?.Value<string>();
            string normalized = LuaDebuggee.NormalizeSource(sourcePath ?? sourceName ?? string.Empty);

            var lines = new List<int>();
            var responseBps = new JArray();
            if (breakpoints != null)
            {
                foreach (var bp in breakpoints)
                {
                    int line = bp["line"]?.Value<int>() ?? 0;
                    lines.Add(line);
                    responseBps.Add(JObject.FromObject(new DapBreakpoint
                    {
                        Verified = true,
                        Line = line,
                        Source = new DapSource { Name = sourceName, Path = sourcePath },
                    }));
                }
            }

            // The DAP protocol does setBreakpoints per-source-file;
            // we apply the same set to every debuggee so a
            // breakpoint hits regardless of which state's hook
            // observes it first. This matches user intent: "I want
            // to break here, on whatever interpreter runs this
            // file."
            lock (mBreakpointStateLock)
            {
                if (lines.Count == 0) mBreakpointState.Remove(normalized);
                else mBreakpointState[normalized] = new List<int>(lines);
            }
            foreach (var d in mServer.AllDebuggees())
            {
                d.SetBreakpoints(normalized, lines);
            }

            SendResponse(req, JObject.FromObject(new { breakpoints = responseBps }));
        }

        void OnSetExceptionBreakpoints(DapMessage req)
        {
            var filters = req.Arguments?["filters"] as JArray;
            bool armed = false;
            if (filters != null)
            {
                foreach (var f in filters)
                {
                    if (f.Value<string>() == "luaError") { armed = true; break; }
                }
            }
            mBreakOnExceptionLatched = armed;
            foreach (var d in mServer.AllDebuggees())
                d.BreakOnException = armed;
            SendResponse(req);
        }

        void OnStackTrace(DapMessage req)
        {
            int threadId = req.Arguments?["threadId"]?.Value<int>() ?? 0;
            var d = mServer.GetDebuggee(threadId);
            if (d == null || !d.IsPaused)
            {
                SendResponse(req, JObject.FromObject(new { stackFrames = new JArray(), totalFrames = 0 }));
                return;
            }
            var dr = new DebugRequest { RequestKind = DebugRequest.Kind.StackTrace };
            d.SubmitRequest(dr);
            var frames = (List<DapStackFrame>)dr.Result ?? new List<DapStackFrame>();
            var arr = new JArray();
            foreach (var f in frames) arr.Add(JObject.FromObject(f));
            SendResponse(req, JObject.FromObject(new { stackFrames = arr, totalFrames = frames.Count }));
        }

        void OnScopes(DapMessage req)
        {
            int frameId = req.Arguments?["frameId"]?.Value<int>() ?? 0;
            var d = FindDebuggeeByPause();
            if (d == null) { SendResponse(req, JObject.FromObject(new { scopes = new JArray() })); return; }
            var dr = new DebugRequest { RequestKind = DebugRequest.Kind.Scopes, FrameId = frameId };
            d.SubmitRequest(dr);
            var scopes = (List<DapScope>)dr.Result ?? new List<DapScope>();
            var arr = new JArray();
            foreach (var s in scopes) arr.Add(JObject.FromObject(s));
            SendResponse(req, JObject.FromObject(new { scopes = arr }));
        }

        void OnVariables(DapMessage req)
        {
            int varRef = req.Arguments?["variablesReference"]?.Value<int>() ?? 0;
            var d = FindDebuggeeByPause();
            if (d == null) { SendResponse(req, JObject.FromObject(new { variables = new JArray() })); return; }
            var dr = new DebugRequest { RequestKind = DebugRequest.Kind.Variables, VariablesReference = varRef };
            d.SubmitRequest(dr);
            var vars = (List<DapVariable>)dr.Result ?? new List<DapVariable>();
            var arr = new JArray();
            foreach (var v in vars) arr.Add(JObject.FromObject(v));
            SendResponse(req, JObject.FromObject(new { variables = arr }));
        }

        void OnEvaluate(DapMessage req)
        {
            string expr = req.Arguments?["expression"]?.Value<string>() ?? string.Empty;
            int frameId = req.Arguments?["frameId"]?.Value<int>() ?? 0;
            var d = FindDebuggeeByPause();
            if (d == null)
            {
                // Evaluate while not paused isn't safe (we'd have to
                // race with whatever the interpreter is doing).
                // Surface a hint instead of silently failing.
                SendResponse(req, JObject.FromObject(new { result = "(not paused)", variablesReference = 0 }), success: false, message: "evaluate requires a paused thread");
                return;
            }
            var dr = new DebugRequest { RequestKind = DebugRequest.Kind.Evaluate, Expression = expr, FrameId = frameId };
            d.SubmitRequest(dr);
            var result = dr.Result as DapVariable;
            if (result == null)
            {
                SendResponse(req, JObject.FromObject(new { result = dr.Error ?? "?", variablesReference = 0 }), success: false, message: dr.Error);
                return;
            }
            SendResponse(req, JObject.FromObject(new
            {
                result = result.Value,
                type = result.Type,
                variablesReference = result.VariablesReference,
            }));
        }

        void OnContinueLike(DapMessage req, DebugRequest.Kind kind)
        {
            int threadId = req.Arguments?["threadId"]?.Value<int>() ?? 0;
            var d = mServer.GetDebuggee(threadId) ?? FindDebuggeeByPause();
            if (d == null || !d.IsPaused)
            {
                SendResponse(req);
                return;
            }
            // Drop variable handles before resuming so the next pause
            // starts clean.
            LuaDebugInspector.ResetHandlesForResume(d);
            var dr = new DebugRequest { RequestKind = kind };
            d.SubmitRequest(dr);
            SendResponse(req);
        }

        void OnPause(DapMessage req)
        {
            int threadId = req.Arguments?["threadId"]?.Value<int>() ?? 0;
            var d = mServer.GetDebuggee(threadId);
            d?.RequestPause();
            SendResponse(req);
        }

        void OnSource(DapMessage req)
        {
            // We don't synthesize sources today (all sources are
            // file-backed). Reply empty content; VS Code will use
            // the path field.
            SendResponse(req, JObject.FromObject(new { content = string.Empty }));
        }

        // Find whichever debuggee is currently paused. For requests
        // that don't carry an explicit threadId in some VS Code
        // builds (older protocols) we fall back to "the only paused
        // one" — there's typically just one.
        LuaDebuggee FindDebuggeeByPause()
        {
            foreach (var d in mServer.AllDebuggees())
                if (d.IsPaused) return d;
            return null;
        }
    }
}
