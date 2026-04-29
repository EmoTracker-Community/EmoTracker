using KeraLua;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace EmoTracker.Data.Debugging
{
    /// <summary>
    /// Reasons a debuggee transitions into the paused state. Drives
    /// the DAP "stopped" event reason field.
    /// </summary>
    public enum PauseReason
    {
        Breakpoint,
        Step,
        Exception,
        Pause,
    }

    /// <summary>
    /// A request from the DAP server thread to the paused executor
    /// thread. The server cannot touch the Lua state directly — only
    /// the thread that hit the hook can. Requests sit on a blocking
    /// queue; the paused thread drains and replies via
    /// <see cref="Result"/>.
    /// </summary>
    public sealed class DebugRequest
    {
        public enum Kind
        {
            Continue,
            StepOver,   // next line at current depth or shallower
            StepIn,     // next line at any depth
            StepOut,    // next return that lands at depth &lt; anchor
            StackTrace,
            Scopes,
            Variables,
            Evaluate,
        }

        public Kind RequestKind;

        // Per-request payload. Not all fields are used by every kind;
        // unused fields stay at default. Cleaner than a flat object[]
        // and quicker to read at call sites than a dynamic.
        public int FrameId;
        public int VariablesReference;
        public string Expression;
        public string EvaluateContext;

        // Filled by the executor thread before signaling the server.
        public object Result;
        public string Error;

        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
    }

    /// <summary>
    /// Per-<see cref="ScriptManager"/> debug adapter. Owns the hook
    /// callback installed on the underlying Lua state, the breakpoint
    /// table, the paused-state coordinator, and the request queue
    /// that lets the DAP server thread fetch info on the paused
    /// thread's behalf.
    ///
    /// <para>
    /// One instance per Lua interpreter (pre-fork: definitional;
    /// post-fork: each fork spawns its own). Forks copy the parent's
    /// <c>Name</c> + breakpoint set so a fresh fork starts with the
    /// same breakpoints as its source — matters when the user is
    /// debugging an autotracker callback that always runs on the
    /// primary state's fork chain.
    /// </para>
    /// </summary>
    public sealed class LuaDebuggee : IDisposable
    {
        readonly NLua.Lua mLua;
        readonly KeraLua.Lua mState;

        /// <summary>
        /// Stable id assigned by the server, used as the DAP
        /// "threadId" for this debuggee.
        /// </summary>
        public int Id { get; internal set; }

        /// <summary>Display name shown in VS Code's threads panel.</summary>
        public string Name { get; }

        /// <summary>
        /// Pack root absolute path, when the pack was loaded from a
        /// directory. Used to resolve VS Code's absolute breakpoint
        /// paths against the pack-relative chunknames Lua reports.
        /// Null when the pack came from an archive (no on-disk paths).
        /// </summary>
        public string PackRootPath { get; set; }

        // -- Hook installation --------------------------------------

        // Holding the delegate as a field pins it for GC — passing a
        // lambda directly to SetHook would let the runtime collect
        // the trampoline and the next hook invocation would AV.
        readonly LuaHookFunction mHookDelegate;

        // Current hook mask. The hook stays installed for our
        // lifetime; we toggle Line on/off based on whether stepping
        // or matching breakpoints are armed.
        LuaHookMask mActiveMask = LuaHookMask.Disabled;

        // -- Pause coordination -------------------------------------

        // Flipped to non-null while the executor thread is blocked
        // inside Pause(); the server reads this to find out which
        // thread is parked and what the current "stopped" location is.
        readonly object mPauseLock = new object();
        bool mPaused;
        public bool IsPaused { get { lock (mPauseLock) return mPaused; } }

        // Inbound requests for the paused executor thread. Bounded
        // capacity keeps the queue honest if something pathological
        // happens (e.g. server posts faster than we drain).
        readonly BlockingCollection<DebugRequest> mRequests = new BlockingCollection<DebugRequest>(64);

        // -- Breakpoints ---------------------------------------------

        // Map of normalized source-key (lower-cased forward-slash
        // basename-relative path; see NormalizeSource) → set of
        // 1-based line numbers. Read on every Line hook event so we
        // keep the lookups O(1) and the structures lock-free for
        // reads (we swap whole entries under a lock on writes).
        readonly object mBreakpointsLock = new object();
        Dictionary<string, HashSet<int>> mBreakpoints = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        // Cheap "any breakpoints set anywhere" flag, written under
        // mBreakpointsLock. Lets the hook short-circuit before doing
        // the full source-resolution dance when the user hasn't set
        // any breakpoints at all.
        volatile bool mAnyBreakpoints;

        // Diagnostic toggle. Set EMOTRACKER_DAP_TRACE=1 in the env to
        // log infrequent events (registration, breakpoint set,
        // pause/resume, first-time-seen Lua source) through the
        // logging sink. Hook-line events are NOT logged — they fire
        // ~1M times during a typical CodeTracker init.lua and even
        // formatted log lines would tank performance.
        public static readonly bool sTrace =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EMOTRACKER_DAP_TRACE"));

        // Sink wired by the app-layer extension (LuaDebuggerExtension)
        // to a real logger (Serilog, etc.). EmoTracker.Data has no
        // Serilog dependency, and the app is a WinExe so
        // Console.WriteLine has nowhere to go — using a delegate
        // sink keeps the data layer log-framework-agnostic while
        // still giving us visibility through the dev terminal /
        // Serilog file sinks.
        public static Action<string> Sink { get; set; }

        public static void Trace(string format, params object[] args)
        {
            if (!sTrace) return;
            try
            {
                string line = "[LuaDbg] " + string.Format(format, args);
                Sink?.Invoke(line);
            }
            catch { }
        }

        /// <summary>
        /// Always-on info log (not gated by EMOTRACKER_DAP_TRACE).
        /// Use for infrequent events the user needs visibility into
        /// even without opting into verbose tracing — debuggee
        /// lifecycle, DAP session connect/disconnect,
        /// setBreakpoints. Routes through the same sink as
        /// <see cref="Trace"/>.
        /// </summary>
        public static void Info(string format, params object[] args)
        {
            try
            {
                string line = "[LuaDbg] " + string.Format(format, args);
                Sink?.Invoke(line);
            }
            catch { }
        }

        // Source paths we've already logged once at hook time. Lets
        // the trace surface "Lua reports source X" exactly once per
        // unique source instead of on every single line, so the user
        // can spot path-mismatch issues without drowning in output.
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> mSeenSources = new();

        // -- Step mode ---------------------------------------------

        enum StepMode { None, StepOver, StepIn, StepOut }

        StepMode mStepMode = StepMode.None;
        // Lua call-stack depth at the moment the user clicked Step.
        // Used by StepOver to decide "current depth or shallower" and
        // by StepOut to decide "shallower than this".
        int mStepAnchorDepth;

        // True iff exception breakpoints are armed. Read by the C#
        // upcall installed as _safe_call's xpcall error handler.
        public bool BreakOnException;

        // -- Construction & lifecycle -------------------------------

        public LuaDebuggee(NLua.Lua lua, string name)
        {
            mLua = lua ?? throw new ArgumentNullException(nameof(lua));
            mState = lua.State;
            Name = name ?? "lua";

            mHookDelegate = HookCallback;
            // Install Call/Return + Line up front. Lua hooks are
            // cheap when no work is done in them; our gate paths
            // exit immediately when there's nothing armed. We need
            // Line at least sometimes (for breakpoints/steps); leaving
            // Call/Return on always lets us track depth without
            // re-arming the hook on every step transition.
            mActiveMask = LuaHookMask.Call | LuaHookMask.Return | LuaHookMask.Line;
            mState.SetHook(mHookDelegate, mActiveMask, 0);
            Info("debuggee created: name='{0}' mask={1}", Name, mActiveMask);
        }

        public void Dispose()
        {
            // Best-effort hook removal. If the Lua state has already
            // closed (Reset path), SetHook will throw — swallow.
            try { mState.SetHook(null, LuaHookMask.Disabled, 0); } catch { }
            mRequests.CompleteAdding();
        }

        // -- Public API used by LuaDebugSession ---------------------

        /// <summary>
        /// Replace this debuggee's breakpoint set for a given source
        /// (call once per setBreakpoints request). Server passes a
        /// normalized path — see <see cref="NormalizeSource"/>.
        /// </summary>
        public void SetBreakpoints(string normalizedSource, IEnumerable<int> lines)
        {
            lock (mBreakpointsLock)
            {
                if (lines == null || !TryAny(lines))
                {
                    mBreakpoints.Remove(normalizedSource);
                }
                else
                {
                    var hs = new HashSet<int>(lines);
                    mBreakpoints[normalizedSource] = hs;
                    Info("setBp on '{0}': '{1}' = [{2}]", Name, normalizedSource, string.Join(",", hs));
                }

                bool any = false;
                foreach (var kv in mBreakpoints)
                {
                    if (kv.Value.Count > 0) { any = true; break; }
                }
                mAnyBreakpoints = any;
            }
        }

        /// <summary>
        /// Match the line at the currently-executing Lua source
        /// against the breakpoint table. Tries exact-match first;
        /// falls back to bidirectional suffix matching so an
        /// absolute-path breakpoint key
        /// (<c>c:/users/.../packs/foo/scripts/init.lua</c>) hits the
        /// pack-relative chunk name Lua reports
        /// (<c>scripts/init.lua</c>) and vice versa. The suffix scan
        /// only runs when the exact-match miss happens, so the
        /// no-breakpoint hot path is unaffected.
        /// </summary>
        bool MatchBreakpoint(string normalizedLuaSource, int line)
        {
            HashSet<int> exact;
            lock (mBreakpointsLock)
            {
                if (mBreakpoints.TryGetValue(normalizedLuaSource, out exact) && exact != null && exact.Contains(line))
                    return true;

                // Suffix match. The Lua source is the chunk name (typically
                // pack-relative); the breakpoint key is the VS Code path
                // (typically absolute). We need '/foo/bar.lua' to match
                // 'c:/.../foo/bar.lua' but NOT 'something_foo/bar.lua' —
                // hence the leading-slash anchor.
                string suffixAnchor = "/" + normalizedLuaSource;
                foreach (var kv in mBreakpoints)
                {
                    if (kv.Value == null || !kv.Value.Contains(line)) continue;
                    string k = kv.Key;
                    if (k.EndsWith(normalizedLuaSource, StringComparison.OrdinalIgnoreCase) &&
                        (k.Length == normalizedLuaSource.Length ||
                         k[k.Length - normalizedLuaSource.Length - 1] == '/'))
                    {
                        return true;
                    }
                    // Inverse: VS Code path is shorter than the chunk
                    // name (rare — but happens if Lua source is e.g. a
                    // resolved absolute path while VS Code sent
                    // pack-relative).
                    if (normalizedLuaSource.EndsWith(k, StringComparison.OrdinalIgnoreCase) &&
                        (normalizedLuaSource.Length == k.Length ||
                         normalizedLuaSource[normalizedLuaSource.Length - k.Length - 1] == '/'))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Hand a request to the paused executor thread, block until
        /// it responds. Throws when the debuggee is not currently
        /// paused (DAP shouldn't ask for vars/etc on a running
        /// thread — only stackTrace pre-pause is forbidden too).
        /// </summary>
        public DebugRequest SubmitRequest(DebugRequest req)
        {
            if (!IsPaused) throw new InvalidOperationException("Debuggee is not paused");
            try { mRequests.Add(req); }
            catch (InvalidOperationException) { req.Error = "queue closed"; req.Done.Set(); return req; }
            req.Done.Wait();
            return req;
        }

        /// <summary>
        /// Manual pause request from DAP "pause". Sets a one-shot
        /// "pause at next hook event" flag the hook checks. Cleared
        /// when the pause fires.
        /// </summary>
        public void RequestPause()
        {
            mPauseRequested = true;
        }
        volatile bool mPauseRequested;

        /// <summary>
        /// Drop pause/step state. Called by the server when the DAP
        /// session detaches so that any executor thread blocked in
        /// the hook returns immediately — never leave Lua threads
        /// blocked because VS Code disconnected.
        /// </summary>
        internal void OnSessionDetached()
        {
            mPauseRequested = false;
            mStepMode = StepMode.None;
            BreakOnException = false;
            // Wake any blocked executor with a synthetic Continue.
            if (IsPaused)
            {
                try { mRequests.Add(new DebugRequest { RequestKind = DebugRequest.Kind.Continue }); } catch { }
            }
        }

        // -- Hook callback ------------------------------------------

        // Fires on the executing thread (whichever thread is calling
        // into Lua). Stays as light as possible in the common case
        // (no breakpoints, no step) — three branches, one volatile
        // read, return.
        void HookCallback(IntPtr stateP, IntPtr arP)
        {
            // Wrap the activation record. Lua's hook conventions:
            // for Line events, Event=Line and CurrentLine is set;
            // for Call/Return, Event=Call/Return.
            //
            // We use the IntPtr-flavored GetInfo so we don't pay for
            // a managed copy until we know we want one.
            var ev = LuaDebug.FromIntPtr(arP);
            var evType = ev.Event;

            // Track depth for stepping using Call/Return events. We
            // can't ask Lua for the current depth cheaply outside of
            // GetStack-loops, so we maintain a counter ourselves.
            if (evType == LuaHookEvent.Call || evType == LuaHookEvent.TailCall)
            {
                mCallDepth++;
                return;
            }
            if (evType == LuaHookEvent.Return)
            {
                mCallDepth--;
                if (mStepMode == StepMode.StepOut && mCallDepth < mStepAnchorDepth)
                {
                    // Returning past the anchor frame — pause on the
                    // next line we observe in the parent. Switch to
                    // StepIn semantics so the next Line event fires.
                    mStepMode = StepMode.StepIn;
                }
                return;
            }
            if (evType != LuaHookEvent.Line) return;

            // Fast path: nothing armed. When EMOTRACKER_DAP_TRACE
            // is set we keep going so the once-per-source diagnostic
            // log surfaces even without breakpoints — useful for
            // verifying path mappings without setting a real
            // breakpoint first.
            if (!sTrace && !mPauseRequested && mStepMode == StepMode.None && !mAnyBreakpoints && !mBreakOnExceptionLatched)
                return;

            // Manual pause — fire on the next line we observe.
            if (mPauseRequested)
            {
                mPauseRequested = false;
                EnterPause(stateP, arP, PauseReason.Pause, null);
                return;
            }

            // Step modes — check anchor depth.
            if (mStepMode != StepMode.None)
            {
                bool fire = false;
                switch (mStepMode)
                {
                    case StepMode.StepIn: fire = true; break;
                    case StepMode.StepOver: fire = mCallDepth <= mStepAnchorDepth; break;
                    case StepMode.StepOut: fire = false; break; // handled in Return branch
                }
                if (fire)
                {
                    mStepMode = StepMode.None;
                    EnterPause(stateP, arP, PauseReason.Step, null);
                    return;
                }
            }

            // Breakpoint check. Get source + line via GetInfo("Sl",
            // ar). 'S' fills Source/ShortSource/LineDefined, 'l'
            // fills CurrentLine.
            if (TryGetCurrentLocation(stateP, arP, out string src, out int line))
            {
                string norm = NormalizeSource(src);
                // First-time-seen log only when verbose tracing is
                // armed — useful for diagnosing path-mismatch issues
                // (Lua source vs. VS Code breakpoint path), but we
                // don't want one log line per Lua file in the steady
                // state.
                if (sTrace && mSeenSources.TryAdd(norm, 0))
                    Trace("first-line on '{0}': source='{1}'", Name, norm);
                if (mAnyBreakpoints && MatchBreakpoint(norm, line))
                {
                    EnterPause(stateP, arP, PauseReason.Breakpoint, null);
                    return;
                }
            }
        }

        // -- Pause loop ---------------------------------------------

        int mCallDepth;
        // Latched while a _safe_call error has just been raised and
        // the C# upcall is about to enter pause. Means "the hook
        // should fall through and let pause coordinate take over"
        // when the user resumes after an exception break.
        bool mBreakOnExceptionLatched;

        // Information about where we paused, populated on entry
        // and consumed by Stack/Scopes/Variables requests.
        internal PausedContext PausedCtx { get; private set; }

        void EnterPause(IntPtr stateP, IntPtr arP, PauseReason reason, string text)
        {
            PausedCtx = new PausedContext
            {
                StatePtr = stateP,
                State = mState,
                CurrentArPtr = arP,
                Reason = reason,
                CallDepthAtPause = mCallDepth,
            };

            lock (mPauseLock) mPaused = true;

            string dapReason = reason switch
            {
                PauseReason.Breakpoint => DapStopReason.Breakpoint,
                PauseReason.Step => DapStopReason.Step,
                PauseReason.Exception => DapStopReason.Exception,
                _ => DapStopReason.Pause,
            };
            // Pause/resume happens once per breakpoint hit and once
            // per step — fine to surface at trace level for
            // diagnostics without spamming the always-on log.
            if (sTrace)
            {
                bool hasSession = LuaDebugServer.Instance?.HasActiveSession ?? false;
                Trace("EnterPause: name='{0}' reason={1} sessionAttached={2}", Name, dapReason, hasSession);
            }
            LuaDebugServer.Instance?.NotifyStopped(this, dapReason, text);

            // Drain requests until the user resumes. Each request
            // runs synchronously on this thread (the only thread
            // allowed to touch the Lua state right now).
            try
            {
                while (true)
                {
                    DebugRequest req;
                    try { req = mRequests.Take(); }
                    catch (InvalidOperationException) { break; } // queue closed (Dispose)
                    catch (OperationCanceledException) { break; }

                    try { ServeRequest(req); }
                    catch (Exception ex) { req.Error = ex.Message; }
                    finally { req.Done.Set(); }

                    if (req.RequestKind == DebugRequest.Kind.Continue ||
                        req.RequestKind == DebugRequest.Kind.StepOver ||
                        req.RequestKind == DebugRequest.Kind.StepIn ||
                        req.RequestKind == DebugRequest.Kind.StepOut)
                        break;
                }
            }
            finally
            {
                lock (mPauseLock) mPaused = false;
                PausedCtx = null;
            }
        }

        void ServeRequest(DebugRequest req)
        {
            switch (req.RequestKind)
            {
                case DebugRequest.Kind.Continue:
                    mStepMode = StepMode.None;
                    break;
                case DebugRequest.Kind.StepOver:
                    mStepMode = StepMode.StepOver;
                    mStepAnchorDepth = mCallDepth;
                    break;
                case DebugRequest.Kind.StepIn:
                    mStepMode = StepMode.StepIn;
                    mStepAnchorDepth = mCallDepth;
                    break;
                case DebugRequest.Kind.StepOut:
                    mStepMode = StepMode.StepOut;
                    mStepAnchorDepth = mCallDepth;
                    break;
                case DebugRequest.Kind.StackTrace:
                    req.Result = LuaDebugInspector.BuildStackTrace(this);
                    break;
                case DebugRequest.Kind.Scopes:
                    req.Result = LuaDebugInspector.BuildScopes(this, req.FrameId);
                    break;
                case DebugRequest.Kind.Variables:
                    req.Result = LuaDebugInspector.BuildVariables(this, req.VariablesReference);
                    break;
                case DebugRequest.Kind.Evaluate:
                    req.Result = LuaDebugInspector.Evaluate(this, req.Expression, req.FrameId);
                    break;
            }
        }

        /// <summary>
        /// Called from the C#-side <c>__et_dap_on_error</c> upcall
        /// (registered into <c>_safe_call</c>'s xpcall error handler)
        /// when a Lua error fires while a debug session is attached
        /// and exception break is armed. Pauses the executor thread
        /// at the throw site so the user can inspect locals before
        /// the error unwinds. Returns the traceback string the
        /// xpcall handler should propagate as the error value.
        /// </summary>
        internal void EnterExceptionPause(string errMessage, string traceback)
        {
            if (!BreakOnException) return;

            mBreakOnExceptionLatched = true;
            try
            {
                // Pause inside this call. We don't have an `ar`
                // pointer (we're in a Lua-side error handler, not a
                // C-side hook), so the inspector falls back to
                // walking GetStack() levels for locals/upvalues.
                EnterPause(IntPtr.Zero, IntPtr.Zero, PauseReason.Exception, errMessage);
            }
            finally { mBreakOnExceptionLatched = false; }
        }

        // -- Helpers ------------------------------------------------

        static bool TryGetCurrentLocation(IntPtr stateP, IntPtr arP, out string source, out int line)
        {
            source = null;
            line = 0;
            try
            {
                var luaState = KeraLua.Lua.FromIntPtr(stateP);
                if (!luaState.GetInfo("Sl", arP)) return false;
                var dbg = LuaDebug.FromIntPtr(arP);
                source = dbg.Source ?? string.Empty;
                line = dbg.CurrentLine;
                return line > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Reduce a Lua source identifier (chunkname) and a VS Code
        /// absolute path to the same key so set-and-hit work
        /// regardless of which side opened the file. Strategy:
        /// strip a leading '@' (Lua's "from file" prefix), normalize
        /// to forward slashes, lowercase. We then do suffix-matching
        /// at hook time to find which breakpoint set applies.
        /// </summary>
        public static string NormalizeSource(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            if (src[0] == '@' || src[0] == '=') src = src.Substring(1);
            src = src.Replace('\\', '/');
            return src.ToLowerInvariant();
        }

        static bool TryAny<T>(IEnumerable<T> seq)
        {
            foreach (var _ in seq) return true;
            return false;
        }
    }

    /// <summary>
    /// Snapshot of the paused state captured when the debuggee enters
    /// a pause. Read by <see cref="LuaDebugInspector"/> to build
    /// stack traces and variable scopes against the executor
    /// thread's Lua state.
    /// </summary>
    public sealed class PausedContext
    {
        public IntPtr StatePtr;
        public KeraLua.Lua State;
        public IntPtr CurrentArPtr;
        public PauseReason Reason;
        public int CallDepthAtPause;
    }
}
