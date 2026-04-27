using System;

namespace EmoTracker.Data.Core.Transactions
{
    public enum TransactionStatus
    {
        Unknown,
        Pending,
        Completed,
        Retrying,
        Rejected
    }

    public interface ITransaction
    {
        TransactionStatus Status { get; }

        bool HasPropertyValue(ITransactableObject src, string propertyName);

        T GetPropertyValue<T>(ITransactableObject src, string propertyName);
    }

    public interface ITransactionScope : IDisposable
    {
        ITransaction Transaction { get; }
    }

    public interface ITransactionProcessor
    {
        /// <summary>
        /// Opens a new transaction, which will be closed upon the returned ITransactionScope being
        /// disposed. Obtained from a model via <c>model.OpenTransaction()</c>; the static
        /// <c>TransactionProcessor.Current</c> slot was retired in favour of per-state
        /// processors owned by <c>TrackerState.Transactions</c>.
        /// </summary>
        ITransactionScope OpenTransaction();

        ITransactionScope CurrentScope { get; }

        /// <summary>
        /// Attempt to write a new property value (attached to obj) into the current transaction, or
        /// open/close a new transaction if necessary.
        /// </summary>
        void WriteProperty<T>(ITransactableObject obj, string fieldName, T value, Action<ITransaction> transactionStateCallback = null);
    }
}
