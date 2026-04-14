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
        LuaTable mItemState;

        LuaFunction mOnLeftClick;
        LuaFunction mOnRightClick;
        LuaFunction mProvidesCode;
        LuaFunction mCanProvideCode;
        LuaFunction mAdvanceToCode;

        LuaFunction mSave;
        LuaFunction mLoad;

        LuaFunction mPropertyChanged;

        public LuaTable ItemState
        {
            get { return mItemState; }
            set
            {
                LuaTable prevValue = mItemState;
                if (SetProperty(ref mItemState, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction OnLeftClickFunc
        {
            get { return mOnLeftClick; }
            set
            {
                LuaFunction prevValue = mOnLeftClick;
                if (SetProperty(ref mOnLeftClick, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction OnRightClickFunc
        {
            get { return mOnRightClick; }
            set
            {
                LuaFunction prevValue = mOnRightClick;
                if (SetProperty(ref mOnRightClick, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction ProvidesCodeFunc
        {
            get { return mProvidesCode; }
            set
            {
                LuaFunction prevValue = mProvidesCode;
                if (SetProperty(ref mProvidesCode, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction CanProvideCodeFunc
        {
            get { return mCanProvideCode; }
            set
            {
                LuaFunction prevValue = mCanProvideCode;
                if (SetProperty(ref mCanProvideCode, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction AdvanceToCodeFunc
        {
            get { return mAdvanceToCode; }
            set
            {
                LuaFunction prevValue = mAdvanceToCode;
                if (SetProperty(ref mAdvanceToCode, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction SaveFunc
        {
            get { return mSave; }
            set
            {
                LuaFunction prevValue = mSave;
                if (SetProperty(ref mSave, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction LoadFunc
        {
            get { return mLoad; }
            set
            {
                LuaFunction prevValue = mLoad;
                if (SetProperty(ref mLoad, value))
                    DisposeObject(prevValue);
            }
        }

        public LuaFunction PropertyChangedFunc
        {
            get { return mPropertyChanged; }
            set
            {
                LuaFunction prevValue = mPropertyChanged;
                if (SetProperty(ref mPropertyChanged, value))
                    DisposeObject(prevValue);
            }
        }


        public LuaItem()
        {
        }

        public override void Dispose()
        {
            DisposeObjectAndDefault(ref mItemState);
            DisposeObjectAndDefault(ref mOnLeftClick);
            DisposeObjectAndDefault(ref mOnRightClick);
            DisposeObjectAndDefault(ref mProvidesCode);
            DisposeObjectAndDefault(ref mCanProvideCode);
            DisposeObjectAndDefault(ref mAdvanceToCode);
            DisposeObjectAndDefault(ref mSave);
            DisposeObjectAndDefault(ref mLoad);
            DisposeObjectAndDefault(ref mPropertyChanged);

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
