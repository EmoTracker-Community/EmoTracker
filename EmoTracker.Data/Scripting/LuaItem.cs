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

// Phase 6 step 11: LuaItem's ScriptManager.Instance accesses are wrapped
// pcall logging — they fire on every Lua callback exception. Per-state
// Lua state lands when each TrackerState allocates its own interpreter
// (deferred follow-up); for now logging routes through the singleton.
#pragma warning disable CS0618

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
                    GetCallbackScriptManager().SafeCall(AdvanceToCodeFunc, this, code);
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
                    object[] result = GetCallbackScriptManager().SafeCall(CanProvideCodeFunc, this, code);
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
                        GetCallbackScriptManager().SafeCall(OnLeftClickFunc, this);
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
                        GetCallbackScriptManager().SafeCall(OnRightClickFunc, this);
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
                    object[] result = GetCallbackScriptManager().SafeCall(ProvidesCodeFunc, this, code);
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
                    object[] results = GetCallbackScriptManager().SafeCall(SaveFunc, this);
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

                    object[] results = GetCallbackScriptManager().SafeCall(LoadFunc, this, dataMap);
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
                        GetCallbackScriptManager().SafeCall(PropertyChangedFunc, this, key, v);
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
        /// Phase 5 callback dispatch helper. Returns the
        /// <see cref="ScriptManager"/> that owns this LuaItem's
        /// interpreter — i.e. the manager whose <c>mLua</c> contains the
        /// LuaTable / LuaFunction this item's reference fields point at.
        /// In Phase 5 this is the singleton <see cref="ScriptManager.Current"/>
        /// for items in the source state and the rewire-bound fork
        /// manager for forked items (set by
        /// <see cref="ScriptManager.RewireForkedLuaItem"/>).
        ///
        /// <para>
        /// Why this routing matters: invoking a fork-side
        /// <see cref="LuaFunction"/> through the source's
        /// <c>_safe_call</c> wrapper is a cross-interpreter operation
        /// that NLua doesn't reliably handle. The
        /// <see cref="GetScriptManager"/> override (Phase 6 per-state)
        /// or the explicit owner set by
        /// <see cref="ScriptManager.RewireForkedLuaItem"/> guarantees
        /// the SafeCall happens in the same interpreter the
        /// LuaFunction lives in.
        /// </para>
        ///
        /// <para>
        /// Falls back to <see cref="ScriptManager.Current"/> when the
        /// holder-aware path returns the no-op
        /// <c>NullScriptManager</c> (test scenarios where
        /// <c>ScriptManagerHost.Current</c> isn't installed) — same
        /// observable behavior as the pre-Phase-5
        /// <c>ScriptManager.Instance</c> lazy-create.
        /// </para>
        /// </summary>
        ScriptManager GetCallbackScriptManager()
        {
            return mOwnerScriptManager
                ?? (this.GetScriptManager() as ScriptManager)
                ?? ScriptManager.Current;
        }

        /// <summary>
        /// Owner-pinned ScriptManager for fork bookkeeping. Set by
        /// <see cref="ScriptManager.RewireForkedLuaItem"/> when this
        /// item is forked, so fork-side callback invocations resolve
        /// through the fork's interpreter rather than the source's
        /// (or the singleton's) <c>_safe_call</c>. Null on items in
        /// the source state, where the singleton fallback is correct.
        /// </summary>
        ScriptManager mOwnerScriptManager;

        /// <summary>
        /// Phase 5: when a <see cref="LuaItem"/> is forked, its 9 NLua
        /// reference fields (<see cref="ItemState"/> + 8 LuaFunction
        /// callbacks) are deliberately left null on the destination
        /// rather than copied verbatim from the source. The fork's
        /// fields are populated by
        /// <see cref="EmoTracker.Data.ScriptManager.RewireForkedLuaItem"/>,
        /// which the orchestrator (Phase 6's TrackerState fork; in
        /// Phase 5 the tests + future migration helpers) MUST call
        /// after <see cref="ScriptManager.Fork"/> populates the cloner.
        ///
        /// <para>
        /// Why null-default rather than copy-verbatim? A copy-verbatim
        /// fork holds source-interpreter references; if the rewire
        /// step is forgotten, callback invocations either silently fire
        /// on the wrong state's interpreter (wrong data mutated) or
        /// fail with a cross-interpreter NLua error. Null-default lets
        /// the existing null-checks in <see cref="OnLeftClick"/> /
        /// <see cref="ProvidesCode"/> / etc. silently no-op until
        /// rewire happens — orphan-free by construction.
        /// </para>
        /// </summary>
        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            // Reference fields stay null on the destination. The
            // RewireForkedLuaItem orchestration step assigns them via
            // ForkCloner.Resolve(source.X). Until then, the fork's
            // callback methods (OnLeftClick / etc.) silently no-op via
            // their existing null checks.
        }

        /// <summary>
        /// Internal rewire entry point invoked by
        /// <see cref="ScriptManager.RewireForkedLuaItem"/>. Reads each
        /// of the 9 reference fields off <paramref name="source"/> (the
        /// LuaItem being forked from) through the supplied
        /// <paramref name="cloner"/>, assigning the destination clone
        /// to this instance's matching field. Also records the fork's
        /// owning ScriptManager so subsequent
        /// <see cref="OnLeftClick"/> / <see cref="Save"/> / etc. fire
        /// against the right interpreter.
        /// </summary>
        internal void RewireWithCloner(LuaStateCloner cloner, LuaItem source, ScriptManager ownerScriptManager)
        {
            if (cloner == null || source == null) return;

            mItemState        = cloner.Resolve(source.mItemState);
            mOnLeftClick      = cloner.Resolve(source.mOnLeftClick);
            mOnRightClick     = cloner.Resolve(source.mOnRightClick);
            mProvidesCode     = cloner.Resolve(source.mProvidesCode);
            mCanProvideCode   = cloner.Resolve(source.mCanProvideCode);
            mAdvanceToCode    = cloner.Resolve(source.mAdvanceToCode);
            mSave             = cloner.Resolve(source.mSave);
            mLoad             = cloner.Resolve(source.mLoad);
            mPropertyChanged  = cloner.Resolve(source.mPropertyChanged);

            mOwnerScriptManager = ownerScriptManager;
        }
    }
}
