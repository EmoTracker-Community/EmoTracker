using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Items;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using NLua;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Data.Scripting
{
    [JsonTypeTags("lua"), DisallowCreationFromTag]
    public partial class LuaItem : ItemBase
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
                    ScriptManager.Instance.SafeCall(AdvanceToCodeFunc, this, code);
            }
            catch (Exception e)
            {
                ScriptManager.Instance.OutputException(e);
            }
        }

        public override bool CanProvideCode(string code)
        {
            try
            {
                if (CanProvideCodeFunc != null)
                {
                    object[] result = ScriptManager.Instance.SafeCall(CanProvideCodeFunc, this, code);
                    if (result != null && result.Length > 0)
                        return Convert.ToBoolean(result.First());
                }
            }
            catch (Exception e)
            {
                ScriptManager.Instance.OutputException(e);
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
                        ScriptManager.Instance.SafeCall(OnLeftClickFunc, this);
                    }
                }
            }
            catch (Exception e)
            {
                ScriptManager.Instance.OutputException(e);
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
                        ScriptManager.Instance.SafeCall(OnRightClickFunc, this);
                    }
                }
            }
            catch (Exception e)
            {
                ScriptManager.Instance.OutputException(e);
            }
        }

        public override uint ProvidesCode(string code)
        {
            try
            {
                if (ProvidesCodeFunc != null)
                {
                    object[] result = ScriptManager.Instance.SafeCall(ProvidesCodeFunc, this, code);
                    if (result != null && result.Length > 0)
                        return Convert.ToUInt32(result.First());
                }
            }
            catch (Exception e)
            {
                ScriptManager.Instance.OutputException(e);
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
                    object[] results = ScriptManager.Instance.SafeCall(SaveFunc, this);
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
                ScriptManager.Instance.OutputException(e);
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
                            ScriptManager.Instance.OutputException(e);
                        }
                    }

                    object[] results = ScriptManager.Instance.SafeCall(LoadFunc, this, dataMap);
                    if (results != null && results.Length > 0)
                        return Convert.ToBoolean(results.First());

                    return true;
                }
            }
            catch (Exception e)
            {
                ScriptManager.Instance.OutputException(e);
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
                        ScriptManager.Instance.SafeCall(PropertyChangedFunc, this, key, v);
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
                }

            }, key);
        }

        public object Get(string key, bool forceReadFromOpenTransaction = false)
        {
            return GetTransactableProperty<object>(key, forceReadFromOpenTransaction);
        }

        // -------- Phase 5 fork plumbing -----------------------------------

        /// <summary>
        /// Phase 5: when a <see cref="LuaItem"/> is forked, its 9 NLua
        /// reference fields (<see cref="ItemState"/> + 8 LuaFunction
        /// callbacks) are copied verbatim from the source. This leaves
        /// the fork temporarily holding orphan references into the
        /// source's interpreter — calling <see cref="OnLeftClick"/> at
        /// this point would resolve through the source's mLua, which
        /// is incorrect for per-state isolation.
        /// <para>
        /// The fork orchestrator (Phase 6's TrackerState fork; in
        /// Phase 5 the tests + future migration helpers) MUST follow up
        /// by calling <see cref="EmoTracker.Data.ScriptManager.RewireForkedLuaItem"/>
        /// on the fork's <see cref="ScriptManager"/>, passing this
        /// item and the source. That method runs each field through
        /// the cloner's <see cref="LuaStateCloner.Resolve"/> path and
        /// rebinds the fork's references to the destination interpreter's
        /// clones.
        /// </para>
        /// <para>
        /// Why this two-step shape (verbatim copy + explicit rewire)
        /// rather than rewiring inside <c>OnForked</c> directly?
        /// At <c>OnForked</c> time the fork's owning <c>ScriptManager</c>
        /// — and therefore the cloner — isn't reachable from a plain
        /// <see cref="ItemBase.Fork"/> call. The orchestrator that knows
        /// about both the source and destination managers is the right
        /// place to wire the rewire; <c>OnForked</c>'s job is just to
        /// keep the fork's fields non-null so the rewire has values to
        /// remap.
        /// </para>
        /// </summary>
        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (LuaItem)source;

            // Copy NLua reference fields verbatim — they point at the
            // source's interpreter at this point. RewireForkedLuaItem
            // (called by the orchestrator after ScriptManager.Fork
            // populates the cloner) replaces them with destination clones.
            mItemState = src.mItemState;
            mOnLeftClick = src.mOnLeftClick;
            mOnRightClick = src.mOnRightClick;
            mProvidesCode = src.mProvidesCode;
            mCanProvideCode = src.mCanProvideCode;
            mAdvanceToCode = src.mAdvanceToCode;
            mSave = src.mSave;
            mLoad = src.mLoad;
            mPropertyChanged = src.mPropertyChanged;
        }

        /// <summary>
        /// Internal rewire entry point invoked by <see cref="ScriptManager.RewireForkedLuaItem"/>.
        /// Walks every NLua reference field through the supplied cloner and
        /// replaces it with the destination clone. After this returns, the
        /// fork's references all point at the fork's interpreter — calls to
        /// <see cref="OnLeftClick"/> etc. fire on the right state.
        /// </summary>
        internal void RewireWithCloner(LuaStateCloner cloner)
        {
            if (cloner == null) return;

            mItemState = cloner.Resolve(mItemState);
            mOnLeftClick = cloner.Resolve(mOnLeftClick);
            mOnRightClick = cloner.Resolve(mOnRightClick);
            mProvidesCode = cloner.Resolve(mProvidesCode);
            mCanProvideCode = cloner.Resolve(mCanProvideCode);
            mAdvanceToCode = cloner.Resolve(mAdvanceToCode);
            mSave = cloner.Resolve(mSave);
            mLoad = cloner.Resolve(mLoad);
            mPropertyChanged = cloner.Resolve(mPropertyChanged);
        }
    }
}
