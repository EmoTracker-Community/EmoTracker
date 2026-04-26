using EmoTracker.Core;
using EmoTracker.Core.DataModel;
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

    class TrackerScriptInterface : Singleton<TrackerScriptInterface>
    {
        public void AddItems(string path)
        {
            Tracker.Instance.AddItems(path);
        }

        public void AddMaps(string path)
        {
            Tracker.Instance.AddMaps(path);
        }

        public void AddLocations(string path)
        {
            Tracker.Instance.AddLocations(path);
        }

        public void AddLayouts(string path)
        {
            Tracker.Instance.AddLayouts(path);
        }

        public object FindObjectForCode(string code)
        {
            return Tracker.Instance.FindObjectForCode(code);
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            return Tracker.Instance.ProviderCountForCode(code, out maxAccessibility);
        }

        public string ActiveVariantUID
        {
            get { return Tracker.Instance.ActiveVariantUID; }
        }
        public Location RootLocation
        {
            get { return LocationDatabase.Instance.Root; }
        }

        #region -- Backwards Compatibility (Temp) --

        public bool DisplayAllLocations
        {
            get { return ApplicationSettings.Instance.DisplayAllLocations; }
            set { ApplicationSettings.Instance.DisplayAllLocations = value; }
        }

        public bool AlwaysAllowClearing
        {
            get { return ApplicationSettings.Instance.AlwaysAllowClearing; }
            set { ApplicationSettings.Instance.AlwaysAllowClearing = value; }
        }

        public bool PinLocationsOnItemCapture
        {
            get { return ApplicationSettings.Instance.PinLocationsOnItemCapture; }
            set { ApplicationSettings.Instance.PinLocationsOnItemCapture = value; }
        }

        public bool AutoUnpinLocationsOnClear
        {
            get { return ApplicationSettings.Instance.AutoUnpinLocationsOnClear; }
            set { ApplicationSettings.Instance.AutoUnpinLocationsOnClear = value; }
        }

        #endregion

    }

    class LayoutScriptInterface : Singleton<LayoutScriptInterface>
    {
        public Layout.Layout FindLayout(string key)
        {
            return Layout.LayoutManager.Instance.FindLayout(key);
        }

        public Layout.LayoutItem FindElement(string uid)
        {
            return Layout.LayoutManager.Instance.FindElement(uid);
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
            ScriptManager.Instance.LogIndent++;
        }

        public void Dispose()
        {
            ScriptManager.Instance.LogIndent--;
        }
    }

    /// <summary>
    /// Phase 5: <see cref="ScriptManager"/> is no longer an
    /// <see cref="ObservableSingleton{T}"/>; it is a regular instantiable
    /// class so Phase 6 can hold one per <c>TrackerState</c>. The static
    /// <see cref="Current"/> property tracks "the active primary"
    /// ScriptManager, defaulting to a single lazily-created instance for
    /// pre-Phase-6 callers; Phase 6's state-switch code reassigns it.
    ///
    /// <para>
    /// <see cref="Instance"/> remains as a transitional alias for
    /// <see cref="Current"/> so the existing ~97 <c>ScriptManager.Instance</c>
    /// callsites continue to work unchanged. New holder-aware code should
    /// prefer <see cref="ModelTypeBase.GetScriptManager"/> where a model
    /// reference is available, so per-state routing falls into place
    /// automatically once Phase 6 lands.
    /// </para>
    /// </summary>
    public class ScriptManager : ModelTypeBase, ICodeProvider, IScriptManager
    {
        // ---- Static current-instance plumbing (replaces ObservableSingleton) ----

        static ScriptManager mCurrent;

        /// <summary>
        /// The currently-active ScriptManager. Lazily created on first access
        /// (matching the pre-Phase-5 ObservableSingleton behavior). Phase 6
        /// reassigns this on state-switch via <see cref="SetCurrent"/>.
        /// </summary>
        public static ScriptManager Current
        {
            get
            {
                if (mCurrent == null)
                    mCurrent = new ScriptManager();
                return mCurrent;
            }
        }

        /// <summary>
        /// Replace the active <see cref="Current"/>. Phase 6 uses this on
        /// pack-load / state-switch; callers should not reassign in Phase 5.
        /// Passing null lets the next <see cref="Current"/> access lazily
        /// recreate (matches the pre-Phase-5 lazy semantics).
        /// </summary>
        public static void SetCurrent(ScriptManager scriptManager)
        {
            mCurrent = scriptManager;
        }

        /// <summary>
        /// Pre-Phase-5 alias for <see cref="Current"/>. Retained so the
        /// ~97 existing <c>ScriptManager.Instance</c> callsites keep
        /// compiling. Phase 6 retires this once UI / extension callsites
        /// are migrated to <see cref="ModelTypeBase.GetScriptManager"/>.
        /// </summary>
        public static ScriptManager Instance => Current;

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

                BootstrapInterpreter();

                LoadScript(mPackage, "scripts/init.lua");
            }
        }

        /// <summary>
        /// Allocates a fresh <see cref="Lua"/> interpreter on this manager
        /// and installs the standard EmoTracker scaffolding: the
        /// <c>_output</c> bridge for <see cref="OutputRaw"/>, the os/io
        /// sandbox, the C#-side bridge globals (Tracker / Layout / etc.),
        /// and the <see cref="SystemLua"/> Lua-side helpers (_safe_call,
        /// print, import). Used by both <see cref="Load"/> (which then runs
        /// <c>scripts/init.lua</c>) and the Phase 5 fork path (which then
        /// hands the freshly-bootstrapped interpreter to
        /// <see cref="LuaStateCloner"/> to migrate live state from the
        /// source manager). Exposed at <c>internal</c> so unit tests in
        /// <c>EmoTracker.SourceGenerators.Tests</c> can drive the
        /// bootstrap directly without a full <see cref="IGamePackage"/>
        /// fixture; production callers go through <see cref="Load"/>.
        /// </summary>
        internal void BootstrapInterpreter()
        {
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

            if (mPackage != null && !mPackage.FlaggedAsUnsafe)
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

            mLua["Tracker"] = TrackerScriptInterface.Instance;
            mLua["Layout"] = LayoutScriptInterface.Instance;
            mLua["AccessibilityLevel"] = new AccessibilityLevel();
            mLua["NotificationType"] = new NotificationType();
            mLua["ScriptHost"] = this;
            mLua["ImageReference"] = new ImageReferenceProvider();

            if (ApplicationSettings.Instance.SupportLua53VersionChecks)
                mLua["_VERSION"] = "Lua 5.3";

            foreach (var entry in mGlobals)
            {
                mLua[entry.Key] = entry.Value;
            }

            mLua.DoString(SystemLua);
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
                ScriptManager.Instance.OutputError("JSON Parse Error");
                using (new LoggingBlock())
                {
                    ScriptManager.Instance.OutputError(jsonException.Message);

                    if (!string.IsNullOrWhiteSpace(jsonException.HelpLink))
                        OutputError("  For more information, see: {0}", jsonException.HelpLink);
                }
            }
            else if (luaException != null)
            {
                ScriptManager.Instance.OutputError("Lua Execution Error");
                using (new LoggingBlock())
                {
                    ScriptManager.Instance.OutputError(luaException.Message);
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

            // ForkCloner holds Lua-side helpers on its mSource interpreter;
            // when this manager is the source, mLua is now closed and any
            // future Resolve() call would NRE on the closed interpreter.
            // Drop the cloner so callers see "no clone happened" rather
            // than crashing.
            ForkCloner = null;

            // mExpressionCache is a per-state cache of provider-count
            // results keyed on Lua-callable codes. After Reset the codes
            // would resolve through a fresh interpreter; drop stale
            // entries (per plan §5.9, the cloner inherits an empty cache
            // on fork via field initializer — keep that semantic on Reset
            // too).
            mExpressionCache.Clear();

            ClearLogOutput();
        }

        /// <summary>
        /// Disposes the manager — currently a thin wrapper over
        /// <see cref="Reset"/>. Future per-state managers in Phase 6
        /// will also tear down their bridge instances, memory-watch
        /// registrations, etc.; the override is here as the lifecycle
        /// hook those changes will plug into.
        /// </summary>
        public override void Dispose()
        {
            Reset();
            base.Dispose();
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

        /// <summary>
        /// Pre-Phase-5 nested enum, retained verbatim so the existing
        /// <c>ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.X, ...)</c>
        /// callsites keep compiling. Values mirror
        /// <see cref="EmoTracker.Core.DataModel.StandardCallback"/> exactly
        /// (same names, same order); the <see cref="IScriptManager"/>
        /// surface accepts the Core enum and casts internally.
        /// </summary>
        public enum StandardCallback
        {
            AccessibilityUpdating,
            AccessibilityUpdated,
            StartLoadingSaveFile,
            FinishLoadingSaveFile,
            PackReady,
            AutoTrackerStarted,
            AutoTrackerStopped,
            LocationUpdating,
            LocationUpdated
        }

        /// <summary>
        /// <see cref="IScriptManager"/> implementation: forwards to the
        /// existing <see cref="InvokeStandardCallback(StandardCallback, object[])"/>
        /// after casting between the Core-side and Data-side enums (which
        /// have identical underlying values). New holder-aware callsites
        /// (model.GetScriptManager().InvokeStandardCallback(...)) flow
        /// through this surface; legacy <c>ScriptManager.Instance.InvokeStandardCallback</c>
        /// callers go straight to the public overload.
        /// </summary>
        void IScriptManager.InvokeStandardCallback(EmoTracker.Core.DataModel.StandardCallback callback, params object[] args)
        {
            InvokeStandardCallback((StandardCallback)callback, args);
        }

        [LuaHide]
        public void InvokeStandardCallback(StandardCallback callback, params object[] args)
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

                case StandardCallback.LocationUpdating:
                    functionName = "tracker_on_location_updating";
                    break;

                case StandardCallback.LocationUpdated:
                    functionName = "tracker_on_location_updated";
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
                                SafeCall(func, args);
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
            LuaItem item = new LuaItem();
            ItemDatabase.Instance.RegisterItem(item);

            return item;
        }

        // -------- Phase 5 fork plumbing -----------------------------------

        /// <summary>
        /// The <see cref="LuaStateCloner"/> used during this manager's
        /// <see cref="OnForked"/> step. Step 7's <c>LuaItem.OnForked</c>
        /// reads this on the fork's manager to resolve its source-side
        /// <c>mItemState</c> / callback references to the destination's
        /// clones — without it those references would point at the
        /// source's now-orphaned interpreter and any callback would
        /// either no-op or exec against the wrong state.
        /// <para>
        /// Null on a freshly-created (non-forked) manager. Holds the
        /// cloner used during the most recent fork until the next fork
        /// or until the manager is reset / disposed.
        /// </para>
        /// </summary>
        internal LuaStateCloner ForkCloner { get; private set; }

        // Phase 6 step 8: extra bridge-identity entries to merge into the
        // bridge map during the next OnForked. Set by TrackerState.Fork so
        // closure upvalues that captured the source state's per-state model
        // instances (Items / Locations / Maps / etc.) get remapped to the
        // fork's instances at clone time. Consumed (cleared) by OnForked.
        internal IReadOnlyDictionary<object, object> PendingExtraBridges { get; set; }

        /// <summary>
        /// Phase 5: produces a fork of this manager whose Lua interpreter
        /// is a deep copy of this manager's live Lua state. The fork's
        /// interpreter shares definition data (mPackage, mGlobals,
        /// service refs) by reference but allocates a fresh
        /// <see cref="Lua"/> with its own bridge bindings; pack-author
        /// state from <c>scripts/init.lua</c> and any subsequent runtime
        /// mutations carry across via <see cref="LuaStateCloner.CloneAll"/>.
        /// </summary>
        public override ModelTypeBase Fork()
        {
            var copy = new ScriptManager();
            copy.InitializeAsForkOf(this);
            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ScriptManager)source;

            // Definition-tier state: share by reference per plan §5.2 — these
            // are pack-defined values that are constant across the fork
            // family.
            //
            // mGlobals is the externally-injected globals dict (populated
            // via SetGlobalObject during pack load). Shared-by-reference is
            // intentional: pack-load-time injections are constant across
            // forks. Note this means SetGlobalObject called on a fork
            // post-construction also mutates the source's view — that's
            // not per-state isolation. SetGlobalObject is intended for
            // pack-load-time use only; runtime per-state global mutation
            // goes through ExecuteLuaString or direct mLua[k] = v on the
            // fork's interpreter.
            //
            // mMemoryService is shared by reference. The watch
            // registrations a pack made (via AddMemoryWatch) on the source
            // continue to fire against the source's interpreter — they're
            // not re-registered on the fork. Plan §5.6 says watches should
            // be per-state; that lifecycle work is deferred to Phase 6
            // alongside per-state TrackerState which owns the watch
            // registration / deregistration story.
            mPackage = src.mPackage;
            mGlobals = src.mGlobals;
            mMemoryService = src.mMemoryService;
            mNotificationService = src.mNotificationService;

            // mExpressionCache — per plan §5.9, a forked manager's cache
            // starts empty rather than copying the source's. The field
            // initializer on the new ScriptManager instance already gives
            // us an empty Dictionary, so no explicit clear is needed; this
            // comment marks the intentional "empty on fork" choice so
            // future readers don't add a copy here.

            // Allocate a fresh interpreter on this fork and run the
            // scaffolding bootstrap (system Lua, bridge globals, sandbox).
            // After this returns, this.mLua is a viable destination for the
            // cloner to migrate the source's reachable Lua state into.
            DisposeObjectAndDefault(ref mLua);
            BootstrapInterpreter();

            // If the source has no Lua interpreter (Load was never called or
            // Reset was), there's nothing to clone — leave the fork's freshly-
            // bootstrapped interpreter as-is.
            if (src.mLua == null) return;

            // Build the bridge identity map: source.bridge → destination.bridge
            // for every C# bridge global ScriptManager binds. In Phase 5 the
            // bridges are still singletons, so each entry is just an identity
            // mapping (src.Tracker == dst.Tracker because both pull from
            // TrackerScriptInterface.Instance). Populating the map is still
            // the right thing for two reasons:
            //
            //   1. The cloner's CloneValue path warns when it falls through
            //      to "passing through reference" for unrecognized userdata.
            //      Without bridge entries, every closure that captures a
            //      bridge as an upvalue emits a noisy warning. With entries,
            //      the bridge-map lookup short-circuits cleanly.
            //
            //   2. Phase 6's per-state bridges will replace each
            //      TrackerScriptInterface.Instance with a state-bound
            //      instance — at which point src.Tracker ≠ dst.Tracker and
            //      this map's entries become the meaningful remap. The
            //      wiring point is in place from Phase 5 onward.
            var bridgeMap = new Dictionary<object, object>();
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "Tracker");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "Layout");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "AccessibilityLevel");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "NotificationType");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "ScriptHost");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "ImageReference");

            // Phase 6 step 8: merge extra bridge-identity entries — typically
            // (sourceModel → forkModel) pairs that TrackerState.Fork built
            // while walking the source's catalogs. Closures whose upvalues
            // captured a per-state model object (e.g. a pack-author closure
            // that holds a specific Item reference) get remapped to the
            // fork's corresponding model at clone time, satisfying plan
            // §5.9's "closures capturing per-state model objects" risk.
            if (PendingExtraBridges != null)
            {
                foreach (var kvp in PendingExtraBridges)
                {
                    if (!bridgeMap.ContainsKey(kvp.Key))
                        bridgeMap[kvp.Key] = kvp.Value;
                }
                // Consume — pending bridges are single-use per fork.
                PendingExtraBridges = null;
            }

            ForkCloner = new LuaStateCloner(src.mLua, mLua, bridgeMap, OutputWarning);
            ForkCloner.CloneAll();
        }

        // Helper: read the same-named global from src's and dst's interpreters
        // and add the (srcValue → dstValue) entry to the bridge map. No-op
        // if either side is null (the bridge isn't installed for some
        // reason). When src and dst hold the same C# reference (Phase 5's
        // singleton case), the map entry is identity — harmless and
        // satisfies the cloner's bridge-lookup short-circuit.
        static void AddBridgeMapping(Dictionary<object, object> map, Lua src, Lua dst, string globalName)
        {
            object srcVal = src[globalName];
            object dstVal = dst[globalName];
            if (srcVal != null && dstVal != null && !map.ContainsKey(srcVal))
            {
                map[srcVal] = dstVal;
            }
        }

        /// <summary>
        /// Phase 6 step 8: drives the cloner explicitly with a caller-
        /// supplied additional bridge-identity map. Used by
        /// <c>TrackerState.Fork()</c> which needs to extend the standard
        /// 6-bridge map with (sourceModel → forkModel) entries built
        /// during the catalog walk so closure upvalues capturing
        /// per-state model objects are remapped at clone time.
        ///
        /// <para>
        /// Prerequisite: <see cref="BootstrapInterpreter"/> has been
        /// called on this manager. The standard 6-bridge map is built
        /// from this manager's bridge globals (Tracker, Layout, etc.) +
        /// <paramref name="source"/>'s; <paramref name="extraBridges"/>
        /// entries are merged on top.
        /// </para>
        /// </summary>
        internal void RunCloneFrom(ScriptManager source, IReadOnlyDictionary<object, object> extraBridges)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (mLua == null)
                throw new InvalidOperationException("BootstrapInterpreter must be called before RunCloneFrom");
            if (source.mLua == null) return; // nothing to clone

            var bridgeMap = new Dictionary<object, object>();
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "Tracker");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "Layout");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "AccessibilityLevel");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "NotificationType");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "ScriptHost");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "ImageReference");

            if (extraBridges != null)
            {
                foreach (var kvp in extraBridges)
                {
                    if (!bridgeMap.ContainsKey(kvp.Key))
                        bridgeMap[kvp.Key] = kvp.Value;
                }
            }

            ForkCloner = new LuaStateCloner(source.mLua, mLua, bridgeMap, OutputWarning);
            ForkCloner.CloneAll();
        }

        /// <summary>
        /// Phase 5 step 7: rewires a forked <see cref="LuaItem"/>'s NLua
        /// reference fields (<see cref="LuaItem.ItemState"/> + the eight
        /// LuaFunction callbacks) from the source's interpreter to this
        /// fork's interpreter, using the cloner that ran during this
        /// manager's own <see cref="OnForked"/>.
        ///
        /// <para>
        /// Call sequence (orchestrated by Phase 6's TrackerState fork; in
        /// Phase 5 by tests / migration helpers):
        /// <list type="number">
        ///   <item><c>destManager = sourceManager.Fork()</c> — populates
        ///         <see cref="ForkCloner"/> on the destination.</item>
        ///   <item><c>destItem = sourceItem.Fork()</c> — runs
        ///         <see cref="LuaItem.OnForked"/>, which copies the
        ///         source's references verbatim onto destItem (so they're
        ///         not null but point at the source's interpreter).</item>
        ///   <item><c>destManager.RewireForkedLuaItem(destItem, sourceItem)</c>
        ///         — walks each reference through
        ///         <see cref="LuaStateCloner.Resolve"/>; destItem's
        ///         references now point at this fork's clones.</item>
        /// </list>
        /// </para>
        ///
        /// <para>
        /// The <paramref name="source"/> argument is the original LuaItem
        /// (still on the source state). It's needed so we know which
        /// source-side references to look up in the cloner — destItem's
        /// own references (post-OnForked) work too because OnForked copied
        /// them verbatim, but using <paramref name="source"/> directly is
        /// clearer and decouples the rewire from any speculative
        /// post-OnForked mutation on destItem.
        /// </para>
        ///
        /// <para>
        /// If <see cref="ForkCloner"/> is null (this manager was never
        /// forked, e.g. it's the source) or if the source's references
        /// weren't reachable from <c>_G</c> at clone time
        /// (<see cref="LuaStateCloner.Resolve"/> returns null), the
        /// corresponding fork-side field is set to null. Calling such a
        /// callback then no-ops — better than holding an orphan source
        /// reference.
        /// </para>
        /// </summary>
        public void RewireForkedLuaItem(LuaItem destination, LuaItem source)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ForkCloner == null) return;

            // RewireWithCloner reads each of the 9 reference fields off
            // source, runs them through the cloner, and assigns the
            // destination clone onto destination. It also pins
            // destination.mOwnerScriptManager = this so fork-side callback
            // invocations (OnLeftClick / Save / etc.) execute against
            // this fork's interpreter rather than the source's
            // _safe_call wrapper.
            destination.RewireWithCloner(ForkCloner, source, this);
        }
    }
}
