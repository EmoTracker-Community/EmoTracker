using EmoTracker.Core;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.Data.Scripting;
using Newtonsoft.Json;
using NLua;
using NLua.Exceptions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using EmoTracker.Data.Session;

namespace EmoTracker.Data
{
    class ImageReferenceProvider
    {
        public ImageReference FromPackRelativePath(string path, string filter = null)
        {
            return ImageReference.FromPackRelativePath(path, filter);
        }

        public ImageReference FromImageReference(ImageReference existingReference, string filter = null)
        {
            return ImageReference.FromImageReference(existingReference, filter);
        }

        public ImageReference FromLayeredImageReferences(params ImageReference[] layers)
        {
            return ImageReference.FromLayeredImageReferences(layers);
        }
    }

    /// <summary>
    /// Surface exposed to Lua scripts as the global <c>Tracker</c> object.
    /// Phase 3 turned this into a session-injected facade (was a Singleton).
    /// Phase 7 re-routed every method/property through
    /// <see cref="Session.TrackerSession.Current"/> rather than a captured
    /// session reference, so Lua calls executed inside a fork scope see the
    /// fork's state even though the interpreter (and this interface instance)
    /// were constructed against the parent.
    /// </summary>
    class TrackerScriptInterface
    {
        public TrackerScriptInterface()
        {
        }

        static Session.TrackerSession S => Session.TrackerSession.Current;

        public void AddItems(string path) { S.Tracker.AddItems(path); }
        public void AddMaps(string path) { S.Tracker.AddMaps(path); }
        public void AddLocations(string path) { S.Tracker.AddLocations(path); }
        public void AddLayouts(string path) { S.Tracker.AddLayouts(path); }

        public object FindObjectForCode(string code)
        {
            return S.Tracker.FindObjectForCode(code);
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            return S.Tracker.ProviderCountForCode(code, out maxAccessibility);
        }

        public string ActiveVariantUID => S.Tracker.ActiveVariantUID;
        public Location RootLocation => S.Locations.Root;

        #region -- Backwards Compatibility (Temp) --

        public bool DisplayAllLocations
        {
            get => S.Settings.DisplayAllLocations;
            set => S.Settings.DisplayAllLocations = value;
        }

        public bool AlwaysAllowClearing
        {
            get => S.Settings.AlwaysAllowClearing;
            set => S.Settings.AlwaysAllowClearing = value;
        }

        public bool PinLocationsOnItemCapture
        {
            get => S.Settings.PinLocationsOnItemCapture;
            set => S.Settings.PinLocationsOnItemCapture = value;
        }

        public bool AutoUnpinLocationsOnClear
        {
            get => S.Settings.AutoUnpinLocationsOnClear;
            set => S.Settings.AutoUnpinLocationsOnClear = value;
        }

        #endregion

    }

    /// <summary>
    /// Surface exposed to Lua scripts as the global <c>Layout</c> object. Phase 5
    /// of the TrackerSession refactor turns this into a session-injected facade
    /// (was a Singleton): one instance per session, constructed by ScriptManager
    /// when the Lua interpreter is (re)created. Mirrors the Phase 3 conversion
    /// of <see cref="TrackerScriptInterface"/>.
    /// </summary>
    class LayoutScriptInterface
    {
        public LayoutScriptInterface()
        {
        }

        static Session.TrackerSession S => Session.TrackerSession.Current;

        public Layout.Layout FindLayout(string key)
        {
            return S.Layouts.FindLayout(key);
        }

        public Layout.LayoutItem FindElement(string uid)
        {
            return S.Layouts.FindElement(uid);
        }

        public string GetColorForAccessibility(AccessibilityLevel accessibility)
        {
            switch (accessibility)
            {
                case AccessibilityLevel.None:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_None;

                case AccessibilityLevel.Cleared:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Cleared;

                case AccessibilityLevel.Inspect:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Inspect;
                
                case AccessibilityLevel.Partial:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Partial;

                case AccessibilityLevel.SequenceBreak:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_SequenceBreak;

                case AccessibilityLevel.Normal:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Normal;                
            }

            return null;
        }
    }

    public class LoggingBlock : IDisposable
    {
        public LoggingBlock()
        {
            TrackerSession.Current.Scripts.LogIndent++;
        }

        public void Dispose()
        {
            TrackerSession.Current.Scripts.LogIndent--;
        }
    }

    /// <summary>
    /// Per-session bindings for a shared <see cref="Scripting.LuaItem"/>. The
    /// LuaFunction / LuaTable references stored here are bound to a specific
    /// NLua.Lua interpreter — which means they must live on the per-session
    /// <see cref="ScriptManager"/>, not on the (shared) LuaItem instance, so a
    /// forked session's Lua can hold its own rebound copies.
    /// </summary>
    public sealed class LuaItemBindings
    {
        public LuaTable ItemState;
        public LuaFunction OnLeftClick;
        public LuaFunction OnRightClick;
        public LuaFunction ProvidesCode;
        public LuaFunction CanProvideCode;
        public LuaFunction AdvanceToCode;
        public LuaFunction Save;
        public LuaFunction Load;
        public LuaFunction PropertyChanged;
    }

    public class ScriptManager : ObservableObject, ICodeProvider
    {
        public class LogLine
        {
            public string Text { get; set; }
            public string Color { get; set; }
        }

        ObservableCollection<LogLine> mLogOutput = new ObservableCollection<LogLine>();
        DelegateCommand mClearLogCommand;

        static readonly string SystemLua =
@"

import = function () end

function print(...)
    local printResult = """"
    for i, v in ipairs(table.pack(...)) do
        printResult = printResult..tostring(v).. ""\t""
    end
    if string.len(printResult) > 0 then
        _output(printResult)
    end
 end

-- Safe-call wrapper: invokes a function via xpcall so that debug.traceback
-- captures the Lua call stack at the point of failure.  Returns:
--   true, result1, result2, ...   on success
--   false, errorMessageWithTraceback   on failure
function _safe_call(fn, ...)
    local args = table.pack(...)
    return xpcall(function() return fn(table.unpack(args, 1, args.n)) end, debug.traceback)
end
 ";

        IGamePackage mPackage;
        Lua mLua;
        TrackerScriptInterface mTrackerInterface;
        LayoutScriptInterface mLayoutInterface;

        [NLua.LuaHide]
        public IEnumerable<LogLine> LogOutput
        {
            get { return mLogOutput; }
        }

        public DelegateCommand ClearLogCommand
        {
            get { return mClearLogCommand; }
        }

        static readonly string kFirstIndentString = "   ";
        static readonly string kIndentString = "   ";
        static readonly string kLastIndentString = "   ";

        string mIndentText = "";
        int mLogIndent = 0;
        public int LogIndent
        {
            get { return mLogIndent; }
            set
            {
                mLogIndent = Math.Max(value, 0);

                mIndentText = "";
                for (int i = 0; i < mLogIndent; ++i)
                {
                    if (i == 0)
                        mIndentText += kFirstIndentString;
                    else if (i == mLogIndent - 1)
                        mIndentText += kLastIndentString;
                    else
                        mIndentText += kIndentString;
                }
            }
        }

        Dictionary<string, object> mGlobals = new Dictionary<string, object>();

        [NLua.LuaHide]
        public void SetGlobalObject(string key, object value)
        {
            try
            {
                mGlobals[key] = value;

                if (mLua != null)
                    mLua[key] = value;
            }
            catch
            {
            }
        }

        // Phase 7e: cache pack script sources as we load them so a fork can
        // rebuild its own Lua interpreter by replaying the same sources
        // against a fresh NLua instance. On fork replay, Tracker.AddItems /
        // AddLocations / AddMaps / AddLayouts become no-ops (pack graph is
        // aliased so it's already populated) and ScriptHost:CreateLuaItem
        // returns the N'th shared LuaItem rather than creating a new one.
        // The replay's side-effect is to re-bind every LuaItem's LuaFunction
        // fields — mBindings — against the fork's fresh NLua instance.
        private readonly List<(string path, byte[] source)> mLoadedScriptSources = new List<(string, byte[])>();

        [NLua.LuaHide]
        public IReadOnlyList<(string path, byte[] source)> LoadedScriptSources => mLoadedScriptSources;

        // Shared LuaItem instances in creation order. During fork replay, each
        // ScriptHost:CreateLuaItem call returns mLuaItems[mReplayIndex++]
        // instead of constructing a new LuaItem — so shared identity is
        // preserved while the fork's Lua gets a chance to rebind the
        // *Func properties.
        private readonly List<Scripting.LuaItem> mLuaItems = new List<Scripting.LuaItem>();

        [NLua.LuaHide]
        public IReadOnlyList<Scripting.LuaItem> LuaItems => mLuaItems;

        // Per-session LuaItem bindings. Keyed by the shared LuaItem instance;
        // values are fresh on each session so forks hold their own fork-Lua-
        // bound LuaFunctions.
        private readonly Dictionary<Scripting.LuaItem, LuaItemBindings> mBindings = new Dictionary<Scripting.LuaItem, LuaItemBindings>();

        [NLua.LuaHide]
        public LuaItemBindings GetLuaItemBindings(Scripting.LuaItem item)
        {
            if (item == null) return null;
            if (!mBindings.TryGetValue(item, out var b))
            {
                b = new LuaItemBindings();
                mBindings[item] = b;
            }
            return b;
        }

        // Replay-mode plumbing: used by Rebuild() to re-execute cached pack
        // script sources against a fresh Lua without double-populating the
        // aliased pack graph.
        private bool mInReplayMode;
        private int mReplayLuaItemIndex;

        [NLua.LuaHide]
        public bool IsReplayMode => mInReplayMode;

        [NLua.LuaHide]
        private object[] LoadScript(IGamePackage package, string path)
        {
            Output(string.Format("Loading Script: {0}", path));
            using (LoggingBlock block = new LoggingBlock())
            {
                try
                {
                    object[] result = null;

                    using (Stream s = package.Open(path))
                    {
                        if (s != null && s.Length > 0)
                        {
                            byte[] buffer = new byte[s.Length];
                            if (s.Read(buffer, 0, buffer.Length) == buffer.Length)
                            {
                                // Only cache on initial (non-replay) load.
                                // Replay re-enters LoadScript transitively when
                                // pack Lua calls ScriptHost:LoadScript from
                                // inside init.lua; we must still execute those
                                // nested scripts so the caller's globals line
                                // up, but re-caching would corrupt the source
                                // list we're iterating.
                                if (!mInReplayMode)
                                    mLoadedScriptSources.Add((path, buffer));
                                result = mLua.DoString(buffer, path);
                            }
                        }
                        else
                        {
                            Output("Script not found");
                        }
                    }

                    return result;
                }
                catch (Exception e)
                {
                    Output(string.Format("A C# exception occurred while loading script: {0}", path));
                    using (LoggingBlock excBlock = new LoggingBlock())
                    {
                        Output(e.Message);
                    }
                    return null;
                }
            }
        }

        public object[] LoadScript(string path)
        {
            return LoadScript(mPackage, path);
        }

        public ScriptManager()
        {
            mClearLogCommand = new DelegateCommand(ClearLogOutput);
        }

        private void ClearLogOutput(object obj = null)
        {
            mLogOutput.Clear();
        }

        IMemoryWatchService mMemoryService = null;
        public void SetMemoryWatchService(IMemoryWatchService service)
        {
            mMemoryService = service;
        }

        INotificationService mNotificationService = null;
        public void SetNotificationService(INotificationService service)
        {
            mNotificationService = service;
        }

        [NLua.LuaHide]
        public void Load(IGamePackage package)
        {
            mPackage = package;
            if (mPackage != null)
            {
                //  Dispose our previous Lua instance
                DisposeObjectAndDefault(ref mLua);
                mLoadedScriptSources.Clear();
                mLuaItems.Clear();
                mBindings.Clear();

                mLua = new Lua();
                mLua.DebugHook += MLua_DebugHook;
                mLua.HookException += MLua_HookException;
                mLua.RegisterFunction("_output", this, this.GetType().GetMethod("OutputRaw"));

                //  Remove disallowed os methods
                try
                {
                    using (LuaTable os = (LuaTable)mLua["os"])
                    {
                        os["execute"] = null;
                        os["exit"] = null;
                        os["setlocale"] = null;
                    }
                }
                catch
                {
                }

                if (!mPackage.FlaggedAsUnsafe)
                {
                    try
                    {
                        using (LuaTable os = (LuaTable)mLua["os"])
                        {
                            os["tmpname"] = null;
                            os["rename"] = null;
                            os["getenv"] = null;
                            os["remove"] = null;
                        }
                    }
                    catch
                    {
                        mLua["os"] = null;
                    }

                    mLua["io"] = null;
                }

                // Phase 3: TrackerScriptInterface is per-session, not a singleton —
                // built fresh whenever the Lua interpreter is (re)created so that
                // Lua's `Tracker` global resolves through the owning session
                // rather than reaching for static .Instance accessors.
                // Phase 7: the script interfaces are stateless w.r.t. the owning
                // session — they resolve via TrackerSession.Current on each call
                // so Lua invoked inside a fork scope sees the fork's state.
                mTrackerInterface = new TrackerScriptInterface();
                mLua["Tracker"] = mTrackerInterface;
                mLayoutInterface = new LayoutScriptInterface();
                mLua["Layout"] = mLayoutInterface;
                mLua["AccessibilityLevel"] = new AccessibilityLevel();
                mLua["NotificationType"] = new NotificationType();
                mLua["ScriptHost"] = this;
                mLua["ImageReference"] = new ImageReferenceProvider();

                foreach (var entry in mGlobals)
                {
                    mLua[entry.Key] = entry.Value;
                }

                mLua.DoString(SystemLua);

                LoadScript(mPackage, "scripts/init.lua");
            }
        }

        [NLua.LuaHide]
        public void OutputRaw(string text, string color = "Goldenrod")
        {
            if (text != null)
            {
                using (StringReader reader = new StringReader(text))
                {
                    string s = reader.ReadLine();
                    while (s != null)
                    {
                        while (mLogOutput.Count > 500)
                            mLogOutput.RemoveAt(0);

                        mLogOutput.Add(new LogLine() { Text = string.Format("{0}{1}", mIndentText, s), Color = color });
                        s = reader.ReadLine();
                    }
                }
            }
        }


        [NLua.LuaHide]
        public void Output(string text)
        {
            if (text != null)
                OutputRaw(text, "DarkGray");
        }

        [NLua.LuaHide]
        public void Output(string format, params object[] args)
        {
            if (format != null)
                OutputRaw(string.Format(format, args), "DarkGray");
        }

        [NLua.LuaHide]
        public void OutputException(Exception e)
        {
            JsonReaderException jsonException = e as JsonReaderException;
            LuaException luaException = e as LuaException;
            if (jsonException != null)
            {
                TrackerSession.Current.Scripts.OutputError("JSON Parse Error");
                using (new LoggingBlock())
                {
                    TrackerSession.Current.Scripts.OutputError(jsonException.Message);

                    if (!string.IsNullOrWhiteSpace(jsonException.HelpLink))
                        OutputError("  For more information, see: {0}", jsonException.HelpLink);
                }
            }
            else if (luaException != null)
            {
                TrackerSession.Current.Scripts.OutputError("Lua Execution Error");
                using (new LoggingBlock())
                {
                    TrackerSession.Current.Scripts.OutputError(luaException.Message);
                }
            }
            else
            {
                OutputError("Exception: {0}\n{1}", e.Message, e.StackTrace);
            }
        }

        [NLua.LuaHide]
        public void OutputError(string text)
        {
            if (text != null)
                OutputRaw(text, "Red");
        }

        [NLua.LuaHide]
        public void OutputError(string format, params object[] args)
        {
            if (format != null)
                OutputError(string.Format(format, args));
        }

        [NLua.LuaHide]
        public void OutputWarning(string text)
        {
            if (text != null)
                OutputRaw(text, "Yellow");
        }

        [NLua.LuaHide]
        public void OutputWarning(string format, params object[] args)
        {
            if (format != null)
                OutputWarning(string.Format(format, args));
        }

        private void MLua_HookException(object sender, NLua.Event.HookExceptionEventArgs e)
        {
            if (e != null)
                OutputError(e.ToString());

            System.Diagnostics.Debug.WriteLine(e);
        }

        private void MLua_DebugHook(object sender, NLua.Event.DebugHookEventArgs e)
        {
            if (e != null)
                OutputError(e.ToString());

            System.Diagnostics.Debug.WriteLine(e);
        }

        [NLua.LuaHide]
        public void Reset()
        {
            if (mLua != null)
            {
                mLua.Close();
                mLua = null;
            }

            ClearLogOutput();
        }

        /// <summary>
        /// Invokes a LuaFunction via xpcall with debug.traceback as the error handler.
        /// On success, returns the function's results. On failure, throws a LuaException
        /// whose message includes the full Lua call stack at the point of failure.
        /// </summary>
        [NLua.LuaHide]
        public object[] SafeCall(LuaFunction func, params object[] args)
        {
            if (mLua == null)
                throw new InvalidOperationException("No Lua environment is loaded");

            using (LuaFunction safeCall = mLua["_safe_call"] as LuaFunction)
            {
                if (safeCall == null)
                    return func.Call(args);  // Fallback if _safe_call not available

                // Build argument array: _safe_call(func, arg1, arg2, ...)
                object[] callArgs = new object[args.Length + 1];
                callArgs[0] = func;
                Array.Copy(args, 0, callArgs, 1, args.Length);

                object[] result = safeCall.Call(callArgs);

                if (result == null || result.Length == 0)
                    return null;

                bool ok = Convert.ToBoolean(result[0]);
                if (!ok)
                {
                    string errorMsg = result.Length > 1 ? result[1]?.ToString() : "Unknown Lua error";
                    throw new LuaException(errorMsg);
                }

                // Strip the leading 'true' status from the results
                if (result.Length <= 1)
                    return null;

                object[] actualResults = new object[result.Length - 1];
                Array.Copy(result, 1, actualResults, 0, actualResults.Length);
                return actualResults;
            }
        }

        [NLua.LuaHide]
        public object[] ExecuteLuaString(string luaCode)
        {
            if (mLua == null)
                throw new InvalidOperationException("No Lua environment is loaded");
            return mLua.DoString(luaCode);
        }

        [NLua.LuaHide]
        public object GetLuaGlobal(string name)
        {
            if (mLua == null)
                throw new InvalidOperationException("No Lua environment is loaded");
            return mLua[name];
        }

        [NLua.LuaHide]
        public bool IsLuaLoaded
        {
            get { return mLua != null; }
        }

        [NLua.LuaHide]
        public object FindObjectForCode(string code)
        {
            return null;
        }

        struct CacheEntry
        {
            public uint count;
            public AccessibilityLevel maxAccessibility;
        }

        private Dictionary<string, CacheEntry> mExpressionCache = new Dictionary<string, CacheEntry>();

        [NLua.LuaHide]
        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            maxAccessibility = AccessibilityLevel.Normal;
            uint count = 0;

            //  First check the cache to see if we've processed this already since the last invalidate
            CacheEntry cachedResult;
            if (mExpressionCache.TryGetValue(code, out cachedResult))
            {
                maxAccessibility = cachedResult.maxAccessibility;
                return cachedResult.count;
            }

            try
            {
                string[] tokens = code.Split('|');
                for (int i = 0; i < tokens.Length; ++i)
                {
                    tokens[i] = tokens[i].Trim();
                }

                using (LuaFunction func = mLua[tokens[0]] as LuaFunction)
                {
                    if (func != null)
                    {
                        object[] result;

                        IEnumerable<string> args = tokens.Skip(1);
                        if (args != null && args.Any())
                        {
                            result = SafeCall(func, args.ToArray<object>());
                        }
                        else
                        {
                            result = SafeCall(func);
                        }

                        if (result == null)
                        {
                            OutputError("Lua function `{0}` did not return a count. All Lua functions used as logical expressions must return a count.", code);
                        }
                        else
                        {
                            if (result.Length >= 2)
                            {
                                AccessibilityLevel? luaLevel = result[1] as AccessibilityLevel?;
                                if (luaLevel != null && luaLevel.HasValue)
                                    maxAccessibility = luaLevel.Value;
                            }

                            count = Convert.ToUInt32(result.First());
                        }
                    }
                    else
                    {
                        OutputError("Couldn't execute lua function `{0}` because it does not exist", code);
                    }
                }
            }
            catch (Exception e)
            {
                Output(e.ToString());
            }

            //  Cache the results
            mExpressionCache[code] = new CacheEntry() { count = count, maxAccessibility = maxAccessibility };

            return count;
        }

        [LuaHide]
        internal void ClearExpressionCache()
        {
            mExpressionCache.Clear();
        }

        bool mbInPostLogicUpdate = false;

        public enum StandardCallback
        {
            AccessibilityUpdating,
            AccessibilityUpdated,
            StartLoadingSaveFile,
            FinishLoadingSaveFile,
            PackReady,
            AutoTrackerStarted,
            AutoTrackerStopped
        }

        [LuaHide]
        public void InvokeStandardCallback(StandardCallback callback)
        {
            string functionName = null;
            switch (callback)
            {
                case StandardCallback.AccessibilityUpdating:
                    functionName = "tracker_on_accessibility_updating";
                    break;

                case StandardCallback.AccessibilityUpdated:
                    functionName = "tracker_on_accessibility_updated";
                    break;

                case StandardCallback.StartLoadingSaveFile:
                    functionName = "tracker_on_begin_loading_save_file";
                    break;

                case StandardCallback.FinishLoadingSaveFile:
                    functionName = "tracker_on_finish_loading_save_file";
                    break;

                case StandardCallback.PackReady:
                    functionName = "tracker_on_pack_ready";
                    break;

                case StandardCallback.AutoTrackerStarted:
                    functionName = "autotracker_started";
                    break;

                case StandardCallback.AutoTrackerStopped:
                    functionName = "autotracker_stopped";
                    break;
            }

            if (!mbInPostLogicUpdate)
            {
                try
                {
                    mbInPostLogicUpdate = true;

                    if (!string.IsNullOrWhiteSpace(functionName))
                    {
                        using (LuaFunction func = mLua[functionName] as LuaFunction)
                        {
                            if (func != null)
                                SafeCall(func);
                        }
                    }
                }
                catch (Exception e)
                {
                    OutputException(e);
                }
                finally
                { 
                    mbInPostLogicUpdate = false;
                }
            }
        }

        public IMemorySegment AddMemoryWatch(string name, ulong startAddress, ulong length, LuaFunction callback, int period = 1000)
        {
            if (mMemoryService != null)
            {
                return mMemoryService.AddMemoryWatch(name, startAddress, length,
                (IMemorySegment segment) => // Callback
                {
                    try
                    {
                        using (new LocationDatabase.SuspendRefreshScope())
                        {
                            if (callback != null)
                            {
                                object[] results = SafeCall(callback, segment);

                                if (results != null && results.Length > 0)
                                    return Convert.ToBoolean(results.First());
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Output(e.ToString());
                    }

                    return true;
                },
                (IMemorySegment segment) => // Dispose Callback
                {
                    if (callback != null)
                        callback.Dispose();

                }, period);
            }

            return null;
        }

        public void RemoveMemoryWatch(IMemorySegment segment)
        {
            if (mMemoryService != null)
                mMemoryService.RemoveMemoryWatch(segment);
        }

        public INotificationService NotificationService
        {
            get { return mNotificationService; }
        }

        public void PushMarkdownNotification(NotificationType type, string markdown, int timeout = -1)
        {
            if (mNotificationService != null)
                mNotificationService.PushMarkdownNotification(type, markdown, timeout);
        }

        public LuaItem CreateLuaItem()
        {
            if (mInReplayMode)
            {
                // Fork replay: return the N'th shared LuaItem. The fork's Lua
                // then assigns OnLeftClickFunc / ProvidesCodeFunc / ... on it,
                // which lands in *this* ScriptManager's bindings dict
                // (fork-Lua-bound), leaving the parent's bindings untouched.
                if (mReplayLuaItemIndex < mLuaItems.Count)
                {
                    return mLuaItems[mReplayLuaItemIndex++];
                }

                // Pack nondeterminism: more CreateLuaItem calls in replay than
                // original. Log and fall through to construct a new one so we
                // don't crash the simulation outright.
                OutputWarning("CreateLuaItem: replay count exceeded original; constructing new item (pack nondeterminism).");
            }

            LuaItem item = new LuaItem();
            TrackerSession.Current.Items.RegisterItem(item);
            mLuaItems.Add(item);
            if (mInReplayMode)
                mReplayLuaItemIndex = mLuaItems.Count;

            return item;
        }

        /// <summary>
        /// Fork-scoped Lua rebuild (Phase 7d/7e). Constructs a fresh NLua.Lua
        /// bound to the current session (expected to be the fork's session:
        /// caller invokes this inside <c>fork.EnterScope()</c>), wires the
        /// standard globals, and replays every cached pack script source
        /// against it. Pack scripts re-execute and re-assign every LuaItem's
        /// *Func / ItemState properties — those assignments now land in the
        /// fork's <see cref="mBindings"/> dict with fresh fork-Lua-bound
        /// LuaFunction/LuaTable refs.
        ///
        /// Prerequisites: this ScriptManager is a fresh instance on a fork
        /// session; it has inherited the parent's package + cached script
        /// sources + LuaItem instance list via <see cref="InheritFrom"/>.
        /// </summary>
        [NLua.LuaHide]
        public void Rebuild()
        {
            if (mPackage == null)
                throw new InvalidOperationException("ScriptManager.Rebuild() requires an inherited package; call InheritFrom() first.");

            DisposeObjectAndDefault(ref mLua);
            mBindings.Clear();
            mExpressionCache.Clear();

            mLua = new Lua();
            mLua.DebugHook += MLua_DebugHook;
            mLua.HookException += MLua_HookException;
            mLua.RegisterFunction("_output", this, this.GetType().GetMethod("OutputRaw"));

            try
            {
                using (LuaTable os = (LuaTable)mLua["os"])
                {
                    os["execute"] = null;
                    os["exit"] = null;
                    os["setlocale"] = null;
                }
            }
            catch { }

            if (!mPackage.FlaggedAsUnsafe)
            {
                try
                {
                    using (LuaTable os = (LuaTable)mLua["os"])
                    {
                        os["tmpname"] = null;
                        os["rename"] = null;
                        os["getenv"] = null;
                        os["remove"] = null;
                    }
                }
                catch { mLua["os"] = null; }

                mLua["io"] = null;
            }

            mTrackerInterface = new TrackerScriptInterface();
            mLua["Tracker"] = mTrackerInterface;
            mLayoutInterface = new LayoutScriptInterface();
            mLua["Layout"] = mLayoutInterface;
            mLua["AccessibilityLevel"] = new AccessibilityLevel();
            mLua["NotificationType"] = new NotificationType();
            mLua["ScriptHost"] = this;
            mLua["ImageReference"] = new ImageReferenceProvider();

            foreach (var entry in mGlobals)
                mLua[entry.Key] = entry.Value;

            mLua.DoString(SystemLua);

            // Replay cached pack sources. During replay, Tracker.AddX no-ops
            // and CreateLuaItem returns shared instances.
            //
            // We only invoke the FIRST cached source (pack's init.lua entry
            // point). Any transitive ScriptHost:LoadScript calls inside
            // init.lua still resolve via LoadScript() and execute against our
            // fresh Lua — so globals are defined in their original order
            // relative to init.lua's control flow. The outer loop avoids
            // double-executing by only ever replaying the entry point.
            mInReplayMode = true;
            mReplayLuaItemIndex = 0;
            try
            {
                if (mLoadedScriptSources.Count > 0)
                {
                    var (path, bytes) = mLoadedScriptSources[0];
                    try
                    {
                        mLua.DoString(bytes, path);
                    }
                    catch (Exception e)
                    {
                        Output(string.Format("Exception replaying script '{0}' on fork: {1}", path, e.Message));
                    }
                }
            }
            finally
            {
                mInReplayMode = false;
            }
        }

        /// <summary>
        /// Copies package reference, cached script sources, and the list of
        /// shared LuaItem instances from a parent ScriptManager onto this
        /// (fork) ScriptManager. Does not copy bindings — those are
        /// regenerated by <see cref="Rebuild"/>.
        /// </summary>
        [NLua.LuaHide]
        public void InheritFrom(ScriptManager parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            mPackage = parent.mPackage;
            mLoadedScriptSources.Clear();
            mLoadedScriptSources.AddRange(parent.mLoadedScriptSources);
            mLuaItems.Clear();
            mLuaItems.AddRange(parent.mLuaItems);
        }
    }
}
