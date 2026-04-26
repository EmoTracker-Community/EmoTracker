using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EmoTracker.Data.Core.DataModel
{
    /// <summary>
    /// Adds the <see cref="ITransactableObject"/> contract to <see cref="ModelTypeBase"/>:
    /// transactable property reads/writes route through the
    /// <see cref="TransactionProcessor"/> (so they participate in undo/redo) but the
    /// committed value lands in <see cref="ModelTypeBase.MutableData"/> rather than in
    /// the legacy <c>TransactableObject.mPropertyStore</c> dictionary.
    ///
    /// <para>
    /// The public surface mirrors <see cref="TransactableObject"/>'s
    /// <c>GetTransactableProperty</c> / <c>SetTransactableProperty</c> verbatim so existing
    /// call sites and the <c>[KVTransactable]</c> source generator output share the same
    /// accessors.
    /// </para>
    /// </summary>
    public abstract class TransactableModelTypeBase : ModelTypeBase, ITransactableObject
    {
        // [TransactablePropertyReadBehavior] reflection cache, identical in spirit to
        // TransactableObject's so behavior is preserved when these properties are
        // referenced through the legacy attribute mechanism. Keyed by host runtime type.
        static readonly Dictionary<Type, Dictionary<string, bool>> sGlobalReadFromOpenTransactionCache
            = new Dictionary<Type, Dictionary<string, bool>>();

        Dictionary<string, bool> mLocalReadFromOpenTransactionCache;

        bool ShouldReadFromOpenTransaction(Type hostType, string propertyName)
        {
            if (mLocalReadFromOpenTransactionCache == null)
            {
                if (!sGlobalReadFromOpenTransactionCache.TryGetValue(hostType, out mLocalReadFromOpenTransactionCache))
                    sGlobalReadFromOpenTransactionCache[hostType] = mLocalReadFromOpenTransactionCache = new Dictionary<string, bool>();
            }

            if (mLocalReadFromOpenTransactionCache.TryGetValue(propertyName, out var bAllowRead))
                return bAllowRead;

            bool resolved = false;
            PropertyInfo propInfo = hostType.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propInfo != null)
            {
                if (Attribute.GetCustomAttribute(propInfo, typeof(TransactablePropertyReadBehaviorAttribute))
                        is TransactablePropertyReadBehaviorAttribute a)
                {
                    resolved = (a.ReadBehavior == TransactablePropertyReadBehavior.AllowOpenTransactionRead);
                }
            }

            mLocalReadFromOpenTransactionCache[propertyName] = resolved;
            return resolved;
        }

        /// <summary>
        /// Reads a transactable property from the current scope (when its
        /// <see cref="TransactablePropertyReadBehaviorAttribute"/> opts in, or when
        /// the caller forces it via <paramref name="bForceReadFromOpenTransaction"/>),
        /// otherwise from the committed <see cref="ModelTypeBase.MutableData"/> store.
        /// </summary>
        public T GetTransactableProperty<T>(
            [CallerMemberName] string propertyName = null,
            bool bForceReadFromOpenTransaction = false)
        {
            if (bForceReadFromOpenTransaction || ShouldReadFromOpenTransaction(this.GetType(), propertyName))
            {
                // Phase 6: read-from-open-scope checks the active processor
                // (owner state's first, then ambient) — not just the static
                // Current — so per-state pending writes are visible to the
                // matching property reads on the same state's models.
                ITransactionScope scope = ResolveActiveProcessor()?.CurrentScope;
                if (scope?.Transaction != null && scope.Transaction.HasPropertyValue(this, propertyName))
                    return scope.Transaction.GetPropertyValue<T>(this, propertyName);
            }

            return MutableData.GetValue<T>(propertyName, default(T));
        }

        /// <summary>
        /// Resolves the active <see cref="ITransactionProcessor"/> for transactable
        /// writes on this model. Phase 6: prefers
        /// <see cref="TrackerState.Transactions"/> when this model has been claimed
        /// by a state (<see cref="ModelTypeBase.OwnerState"/> set), so its writes
        /// participate in that state's per-state undo stack rather than the global
        /// singleton's. Falls back to <see cref="TransactionProcessor.Current"/>
        /// for unowned models — the path every Phase 0–5 model uses today.
        ///
        /// <para>
        /// The <c>OwnerState as TrackerState</c> cast is the place where the
        /// Core-side <see cref="ITrackerStateContext"/> marker meets the
        /// Data-side <see cref="TrackerState"/> concrete type. Only TrackerState
        /// exposes a transaction processor; if a different
        /// <see cref="ITrackerStateContext"/> implementation lands later that
        /// also has one, this resolver needs updating.
        /// </para>
        /// </summary>
        ITransactionProcessor ResolveActiveProcessor()
        {
            return (this.OwnerState as TrackerState)?.Transactions
                ?? TransactionProcessor.Current;
        }

        /// <summary>
        /// Routes the write through the active transaction processor — the
        /// owning <see cref="TrackerState"/>'s when set, the global
        /// <see cref="TransactionProcessor.Current"/> otherwise. On commit, the
        /// transaction's callback writes the resulting value into
        /// <see cref="ModelTypeBase.MutableData"/>, raises <c>PropertyChanging</c> /
        /// <c>PropertyChanged</c>, and invokes <paramref name="onTransactionProcessed"/>.
        /// Returns true iff a write was queued (i.e. the new value differs from the
        /// committed one); returns false (and skips the callback) on no-op writes.
        /// </summary>
        protected bool SetTransactableProperty<T>(
            T value,
            Action<T> onTransactionProcessed = null,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(GetTransactableProperty<T>(propertyName), value))
                return false;

            ResolveActiveProcessor().WriteProperty(this, propertyName, value, (transactionState) =>
            {
                if (transactionState.Status == TransactionStatus.Completed)
                {
                    try
                    {
                        T resultValue = transactionState.GetPropertyValue<T>(this, propertyName);
                        NotifyPropertyChanging(propertyName);

                        // Writeback target: MutableData rather than the legacy
                        // mPropertyStore dictionary on TransactableObject. Per-key COW
                        // ensures forks see only their own committed values; the
                        // boundary deep-copy in the store ensures the transaction's
                        // captured reference cannot be mutated by callers later.
                        MutableData.SetValue(propertyName, resultValue);

                        onTransactionProcessed?.Invoke(resultValue);

                        NotifyPropertyChanged(propertyName);
                    }
                    catch
                    {
                        // Swallow to match legacy TransactableObject behavior — a malformed
                        // transaction state must never crash the writeback path.
                    }
                }
            });

            return true;
        }

        /// <summary>
        /// As <see cref="SetTransactableProperty{T}"/>, but invokes
        /// <paramref name="onTransactionProcessed"/> even when the new value matches the
        /// committed value (i.e. for "force-fire side effect" use cases).
        /// </summary>
        protected bool ForceSetTransactableProperty<T>(
            T value,
            Action<T> onTransactionProcessed = null,
            [CallerMemberName] string propertyName = null)
        {
            if (!SetTransactableProperty(value, onTransactionProcessed, propertyName) && onTransactionProcessed != null)
                onTransactionProcessed(value);

            return true;
        }

        // Re-expose the protected MutableData store under a name the source-generator-
        // emitted code can use without having to know about the protected member's
        // exact name. Concrete leaves' [KVTransactable]/[KVMutable]/[KVImmutable]
        // generator output is emitted as part of the same partial class, so it has
        // access to MutableData/ImmutableData directly. This stub exists only to
        // keep the inherited members visible to IDE consumers.

        // -------- Phase 6 transaction scope API ------------------------------

        /// <summary>
        /// Phase 6: opens a transaction scope on this model's owning state's
        /// transaction processor (or the ambient one when this model isn't
        /// owned by a state). While the scope is open, transactable writes
        /// on any model whose owner is the same processor are batched into
        /// one undoable entry.
        ///
        /// <para>
        /// Per plan §6.2 this is the ONLY way to open a scope — the public
        /// API does not expose <c>state.Transactions.OpenTransaction()</c>
        /// — so writes inside an open scope can't accidentally cross state
        /// boundaries by stylistic mistake. (Cross-state writes that bypass
        /// the public API surface — reflection, test harnesses — go through
        /// the runtime sentinel landing in a follow-up step.)
        /// </para>
        /// </summary>
        public ITransactionScope OpenTransaction()
        {
            return ResolveActiveProcessor().OpenTransaction();
        }
    }
}
