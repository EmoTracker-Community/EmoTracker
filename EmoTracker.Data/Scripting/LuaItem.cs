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

// LuaItem's exception-logging routes through GetCallbackScriptManager()
// — the holder's per-state ScriptManager. There is no fall-back ambient
// state; if a LuaItem has no OwnerState its exception simply isn't logged.
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
        LuaFunction mGetAllProvidedCodes;

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

        /// <summary>
        /// Pack-registerable callback: returns the static set of every code
        /// this LuaItem could ever provide via <see cref="ProvidesCodeFunc"/>,
        /// across every state of the item. The pack assigns a function whose
        /// signature is <c>function(self) → table-of-strings</c>; the table
        /// values are extracted (keys are ignored, so both array-style
        /// <c>{ "lamp", "fire" }</c> and set-style
        /// <c>{ lamp = true, fire = true }</c> tables are accepted).
        ///
        /// <para>
        /// When registered, lets <see cref="ItemDatabase"/> place the
        /// LuaItem in its static code → providers index alongside non-Lua
        /// items, so accessibility-rule lookups for codes the item does NOT
        /// declare here skip it entirely (no per-lookup
        /// <see cref="ProvidesCode"/> Lua call). Leave unregistered to keep
        /// fully dynamic dispatch (the legacy fallback).
        /// </para>
        /// </summary>
        public LuaFunction GetAllProvidedCodesFunc
        {
            get { return mGetAllProvidedCodes; }
            set
            {
                LuaFunction prevValue = mGetAllProvidedCodes;
                if (SetProperty(ref mGetAllProvidedCodes, value))
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
            DisposeObjectAndDefault(ref mGetAllProvidedCodes);
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
                GetCallbackScriptManager()?.OutputException(e);
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
                GetCallbackScriptManager()?.OutputException(e);
            }

            return false;
        }

        public override void OnLeftClick()
        {
            try
            {
                if (OnLeftClickFunc != null)
                {
                    using (new LocationDatabase.SuspendRefreshScope((this.OwnerState as Sessions.TrackerState)?.Locations))
                    {
                        GetCallbackScriptManager().SafeCall(OnLeftClickFunc, this);
                    }
                }
            }
            catch (Exception e)
            {
                GetCallbackScriptManager()?.OutputException(e);
            }
        }

        public override void OnRightClick()
        {
            try
            {
                if (OnRightClickFunc != null)
                {
                    using (new LocationDatabase.SuspendRefreshScope((this.OwnerState as Sessions.TrackerState)?.Locations))
                    {
                        GetCallbackScriptManager().SafeCall(OnRightClickFunc, this);
                    }
                }
            }
            catch (Exception e)
            {
                GetCallbackScriptManager()?.OutputException(e);
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
                GetCallbackScriptManager()?.OutputException(e);
            }

            return 0;
        }

        /// <summary>
        /// If the pack registered <see cref="GetAllProvidedCodesFunc"/>,
        /// invoke it and unpack the returned Lua table into a string list.
        /// Otherwise return null (legacy behavior — the item is treated as
        /// dynamic by <see cref="ItemDatabase"/> and gets brute-force
        /// queried on every <see cref="ProvidesCode"/> lookup).
        ///
        /// <para>
        /// The Lua callback may return either an array-style table
        /// (<c>{ "lamp", "fire" }</c>) or a set-style table
        /// (<c>{ lamp = true, fire = true }</c>); in the set-style case the
        /// keys (not the values) are used as codes. Numeric / boolean keys
        /// are skipped — only string entries become codes.
        /// </para>
        /// </summary>
        public override IEnumerable<string> GetAllProvidedCodes()
        {
            // No callback → indeterminate. Return null to preserve legacy
            // ItemDatabase behavior (brute-force ProvidesCode dispatch).
            if (mGetAllProvidedCodes == null) return null;

            try
            { 
                object[] result = GetCallbackScriptManager().SafeCall(mGetAllProvidedCodes, this);
                if (result == null || result.Length == 0) return null;

                var codes = new List<string>();
                if (result[0] is LuaTable table)
                {
                    // Array-style values first: tables typically index as
                    // 1, 2, 3, ... so iterate Values yielding "lamp", "fire".
                    foreach (var v in table.Values)
                    {
                        if (v is string s && !string.IsNullOrEmpty(s))
                            codes.Add(s);
                    }
                    // If no string values were found, fall back to keys —
                    // accommodates set-style { lamp = true, fire = true }
                    // tables where the codes are stored on the key side.
                    if (codes.Count == 0)
                    {
                        foreach (var k in table.Keys)
                        {
                            if (k is string s && !string.IsNullOrEmpty(s))
                                codes.Add(s);
                        }
                    }
                }
                else if (result[0] is string single && !string.IsNullOrEmpty(single))
                {
                    // Convenience: callback may return a single comma-
                    // separated code string instead of a table.
                    foreach (var part in single.Split(','))
                    {
                        var trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) codes.Add(trimmed);
                    }
                }
                return codes;
            }
            catch (Exception e)
            {
                GetCallbackScriptManager()?.OutputException(e);
                // Fail safe: signal "indeterminate" so the item stays in
                // the dynamic-dispatch list and ProvidesCode is still
                // consulted at lookup time.
                return null;
            }
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
                GetCallbackScriptManager()?.OutputException(e);
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
                            GetCallbackScriptManager()?.OutputException(e);
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
                GetCallbackScriptManager()?.OutputException(e);
            }

            return false;
        }

        public bool Set(string key, object value)
        {
            // If Lua hands us a data-model object (Item, Location, Section, ...)
            // store a fork-safe ModelReference rather than the raw C# reference.
            // Storing the raw reference would pin the source state's instance into
            // every fork's MutableData via the COW boundary deep-copy — fork
            // mutations on the same key would still see the original state's
            // object. ModelReference carries only the DefinitionId across the
            // boundary; resolution at Get-time chases the calling LuaItem's
            // own state's resolver (see ResolveStoredValue below).
            object storeValue = value is ModelTypeBase mt
                ? (object)new ModelReference<ModelTypeBase>(this, mt)
                : value;

            return SetTransactableProperty(storeValue, (v) =>
            {
                try
                {
                    if (PropertyChangedFunc != null)
                        GetCallbackScriptManager().SafeCall(PropertyChangedFunc, this, key, ResolveStoredValue(v));
                }
                catch (Exception e)
                {
                    GetCallbackScriptManager()?.OutputException(e);
                }

            }, key);
        }

        public object Get(string key, bool forceReadFromOpenTransaction = false)
        {
            return ResolveStoredValue(GetTransactableProperty<object>(key, forceReadFromOpenTransaction));
        }

        /// <summary>
        /// Unwraps a stored value: if it is a <see cref="ModelReference{T}"/>
        /// (placed there by <see cref="Set(string, object)"/> when Lua handed
        /// us a model object), resolves it through THIS LuaItem's current
        /// <see cref="ModelTypeBase.GetModelResolver"/> and returns the
        /// resolved instance. All other values pass through unchanged.
        ///
        /// <para>
        /// Resolution uses <c>this.GetModelResolver()</c> rather than
        /// <c>mref.Target</c> because <see cref="ModelReference{T}.DeepCopy"/>
        /// (invoked on the COW boundary when <see cref="ModelTypeBase.MutableData"/>
        /// is forked) preserves the original holder back-reference. A fork's
        /// deep-copied ModelReference therefore has <c>mHolder</c> pointing at
        /// the SOURCE LuaItem; resolving through <c>mref.Target</c> would
        /// chase the source state's resolver and return the source state's
        /// instance. Resolving through <c>this</c> guarantees the fork sees
        /// its own state's instance.
        /// </para>
        /// </summary>
        object ResolveStoredValue(object value)
        {
            if (value is ModelReference<ModelTypeBase> mref)
            {
                if (mref.IsEmpty) return null;
                return this.GetModelResolver()?.Resolve<ModelTypeBase>(mref.DefinitionId);
            }
            return value;
        }

        // -------- Phase 5 fork plumbing -----------------------------------

        /// <summary>
        /// Phase 5 callback dispatch helper. Returns the
        /// <see cref="ScriptManager"/> that owns this LuaItem's
        /// interpreter — i.e. the manager whose <c>mLua</c> contains the
        /// LuaTable / LuaFunction this item's reference fields point at.
        /// In Phase 5 this is the singleton <see cref="Sessions.SessionContext.ActiveState?.Scripts"/>
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
        /// Falls back to <see cref="Sessions.SessionContext.ActiveState?.Scripts"/> when the
        /// holder-aware path returns the no-op
        /// <c>NullScriptManager</c> (test scenarios where
        /// <c>ScriptManagerHost.Current</c> isn't installed) — same
        /// observable behavior as the pre-Phase-5
        /// <c>Sessions.SessionContext.ActiveState?.Scripts</c> lazy-create.
        /// </para>
        /// </summary>
        ScriptManager GetCallbackScriptManager()
        {
            return mOwnerScriptManager
                ?? (this.GetScriptManager() as ScriptManager);
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

            // LuaItem's mItemState + 8 callback fields aren't stored as
            // named globals on _G — pack scripts hand them to LuaItem via
            // C# bindings and the Lua side typically loses its only
            // reference once the constructing function returns. So
            // LuaStateCloner.CloneAll never visits them, and Resolve
            // (which queries the post-clone identity map) returns null.
            // Use CloneValue, which clones on demand and registers the
            // result in the identity map for later Resolve calls (e.g.
            // closures captured by other LuaItems pointing to the same
            // table).
            mItemState              = (LuaTable)    cloner.CloneValue(source.mItemState);
            mOnLeftClick            = (LuaFunction) cloner.CloneValue(source.mOnLeftClick);
            mOnRightClick           = (LuaFunction) cloner.CloneValue(source.mOnRightClick);
            mProvidesCode           = (LuaFunction) cloner.CloneValue(source.mProvidesCode);
            mCanProvideCode         = (LuaFunction) cloner.CloneValue(source.mCanProvideCode);
            mAdvanceToCode          = (LuaFunction) cloner.CloneValue(source.mAdvanceToCode);
            mGetAllProvidedCodes    = (LuaFunction) cloner.CloneValue(source.mGetAllProvidedCodes);
            mSave                   = (LuaFunction) cloner.CloneValue(source.mSave);
            mLoad                   = (LuaFunction) cloner.CloneValue(source.mLoad);
            mPropertyChanged        = (LuaFunction) cloner.CloneValue(source.mPropertyChanged);

            mOwnerScriptManager = ownerScriptManager;
        }
    }
}
