using EmoTracker.Core;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.Data.Scripting;
using Newtonsoft.Json;
using NLua;
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

    public class ScriptManager : ObservableSingleton<ScriptManager>, ICodeProvider
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
                                result = mLua.DoString(buffer);
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

                mLua["Tracker"] = TrackerScriptInterface.Instance;
                mLua["Layout"] = LayoutScriptInterface.Instance;
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
                            result = func.Call(args.ToArray<object>());
                        }
                        else
                        {
                            result = func.Call();
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
                                func.Call();
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
                            LocationDatabase.Instance.SuspendRefresh = true;

                            if (callback != null)
                            {
                                object[] results = callback.Call(segment);

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
    }
}
