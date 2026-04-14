using EmoTracker.Core;
using EmoTracker.Data.Items;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using NLua;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Scripting
{
    [JsonTypeTags("lua"), DisallowCreationFromTag]
    public class LuaItem : ItemBase
    {
        // Phase 7d: LuaItem's LuaFunction/LuaTable fields are bound to a
        // specific NLua.Lua interpreter. On fork, the fork's ScriptManager
        // holds its own fresh interpreter (and is a new ScriptManager
        // instance), so those refs must live on the per-session ScriptManager
        // rather than on this (shared) LuaItem instance. Every accessor here
        // routes through TrackerSession.Current.Scripts.GetLuaItemBindings(this).
        //
        // Consequence: LuaItem properties silently follow the active fork
        // scope, which is exactly what we want — Lua code executed against
        // the fork's interpreter sees the fork's rebound functions; parent
        // code sees parent's.
        private LuaItemBindings B => TrackerSession.Current.Scripts.GetLuaItemBindings(this);

        public LuaTable ItemState
        {
            get { return B?.ItemState; }
            set
            {
                var bindings = B;
                if (bindings == null) return;
                LuaTable prev = bindings.ItemState;
                if (!object.ReferenceEquals(prev, value))
                {
                    bindings.ItemState = value;
                    NotifyPropertyChanged();
                    DisposeObject(prev);
                }
            }
        }

        public LuaFunction OnLeftClickFunc
        {
            get { return B?.OnLeftClick; }
            set { var b = B; if (b == null) return; var prev = b.OnLeftClick; if (!object.ReferenceEquals(prev, value)) { b.OnLeftClick = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction OnRightClickFunc
        {
            get { return B?.OnRightClick; }
            set { var b = B; if (b == null) return; var prev = b.OnRightClick; if (!object.ReferenceEquals(prev, value)) { b.OnRightClick = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction ProvidesCodeFunc
        {
            get { return B?.ProvidesCode; }
            set { var b = B; if (b == null) return; var prev = b.ProvidesCode; if (!object.ReferenceEquals(prev, value)) { b.ProvidesCode = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction CanProvideCodeFunc
        {
            get { return B?.CanProvideCode; }
            set { var b = B; if (b == null) return; var prev = b.CanProvideCode; if (!object.ReferenceEquals(prev, value)) { b.CanProvideCode = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction AdvanceToCodeFunc
        {
            get { return B?.AdvanceToCode; }
            set { var b = B; if (b == null) return; var prev = b.AdvanceToCode; if (!object.ReferenceEquals(prev, value)) { b.AdvanceToCode = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction SaveFunc
        {
            get { return B?.Save; }
            set { var b = B; if (b == null) return; var prev = b.Save; if (!object.ReferenceEquals(prev, value)) { b.Save = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction LoadFunc
        {
            get { return B?.Load; }
            set { var b = B; if (b == null) return; var prev = b.Load; if (!object.ReferenceEquals(prev, value)) { b.Load = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }

        public LuaFunction PropertyChangedFunc
        {
            get { return B?.PropertyChanged; }
            set { var b = B; if (b == null) return; var prev = b.PropertyChanged; if (!object.ReferenceEquals(prev, value)) { b.PropertyChanged = value; NotifyPropertyChanged(); DisposeObject(prev); } }
        }


        public LuaItem()
        {
        }

        public override void Dispose()
        {
            // Only dispose this session's bindings. Other sessions (e.g. a
            // parent if this is invoked from a fork tear-down, or vice versa)
            // manage their own via ScriptManager disposal.
            var b = B;
            if (b != null)
            {
                DisposeObjectAndDefault(ref b.ItemState);
                DisposeObjectAndDefault(ref b.OnLeftClick);
                DisposeObjectAndDefault(ref b.OnRightClick);
                DisposeObjectAndDefault(ref b.ProvidesCode);
                DisposeObjectAndDefault(ref b.CanProvideCode);
                DisposeObjectAndDefault(ref b.AdvanceToCode);
                DisposeObjectAndDefault(ref b.Save);
                DisposeObjectAndDefault(ref b.Load);
                DisposeObjectAndDefault(ref b.PropertyChanged);
            }

            base.Dispose();
        }

        public override void AdvanceToCode(string code = null)
        {
            try
            {
                if (AdvanceToCodeFunc != null)
                    TrackerSession.Current.Scripts.SafeCall(AdvanceToCodeFunc, this, code);
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }
        }

        public override bool CanProvideCode(string code)
        {
            try
            {
                if (CanProvideCodeFunc != null)
                {
                    object[] result = TrackerSession.Current.Scripts.SafeCall(CanProvideCodeFunc, this, code);
                    if (result != null && result.Length > 0)
                        return Convert.ToBoolean(result.First());
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }

            return false;
        }

        public override void OnLeftClick()
        {
            try
            {
                if (OnLeftClickFunc != null)
                {
                    using (new LocationDatabase.SuspendRefreshScope())
                    {
                        TrackerSession.Current.Scripts.SafeCall(OnLeftClickFunc, this);
                    }
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }
        }

        public override void OnRightClick()
        {
            try
            {
                if (OnRightClickFunc != null)
                {
                    using (new LocationDatabase.SuspendRefreshScope())
                    {
                        TrackerSession.Current.Scripts.SafeCall(OnRightClickFunc, this);
                    }
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }
        }

        public override uint ProvidesCode(string code)
        {
            try
            {
                if (ProvidesCodeFunc != null)
                {
                    object[] result = TrackerSession.Current.Scripts.SafeCall(ProvidesCodeFunc, this, code);
                    if (result != null && result.Length > 0)
                        return Convert.ToUInt32(result.First());
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }

            return 0;
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
        }

        protected override bool Save(JObject data)
        {
            try
            {
                if (SaveFunc != null)
                {
                    object[] results = TrackerSession.Current.Scripts.SafeCall(SaveFunc, this);
                    if (results != null && results.Length > 0)
                    {
                        LuaTable saveData = results.First() as LuaTable;
                        if (saveData != null)
                        {
                            var iter = saveData.GetEnumerator();
                            if (iter != null)
                            {
                                do
                                {
                                    if (!iter.MoveNext())
                                        break;

                                    if (iter.Key != null)
                                    {
                                        string key = iter.Key.ToString();
                                        if (iter.Value != null)
                                        {
                                            data[key] = JToken.FromObject(iter.Value);
                                        }
                                    }
                                } while (true);
                            }

                            saveData.Dispose();
                            return true;
                        }
                    }
                }
                else
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }

            return false;
        }

        protected override bool Load(JObject data)
        {
            try
            {
                if (LoadFunc != null)
                {
                    Dictionary<string, object> dataMap = new Dictionary<string, object>();
                    foreach (var entry in data)
                    {
                        try
                        {
                            dataMap[entry.Key] = entry.Value.ToObject<object>();
                        }                        
                        catch (Exception e)
                        {                        
                            TrackerSession.Current.Scripts.OutputException(e);
                        }
                    }

                    object[] results = TrackerSession.Current.Scripts.SafeCall(LoadFunc, this, dataMap);
                    if (results != null && results.Length > 0)
                        return Convert.ToBoolean(results.First());

                    return true;
                }
            }
            catch (Exception e)
            {
                TrackerSession.Current.Scripts.OutputException(e);
            }

            return false;
        }

        public bool Set(string key, object value)
        {
            return SetTransactableProperty(value, (v) =>
            {
                try
                {
                    if (PropertyChangedFunc != null)
                        TrackerSession.Current.Scripts.SafeCall(PropertyChangedFunc, this, key, v);
                }
                catch (Exception e)
                {
                    TrackerSession.Current.Scripts.OutputException(e);
                }

            }, key);
        }

        public object Get(string key, bool forceReadFromOpenTransaction = false)
        {
            return GetTransactableProperty<object>(key, forceReadFromOpenTransaction);
        }
    }
}
