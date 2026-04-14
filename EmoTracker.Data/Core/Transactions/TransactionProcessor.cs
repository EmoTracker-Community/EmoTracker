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

        bool HasPropertyValue(TransactableObject src, string propertyName);

        T GetPropertyValue<T>(TransactableObject src, string propertyName);
    }

    public interface ITransactionScope : IDisposable
    {
        ITransaction Transaction { get; }
    }

    public interface ITransactionProcessor
    {
        /// <summary>
        /// Opens a new transaction, which will be closed upon the returned ITransactionScope being
        /// disposed.
        /// </summary>
        /// <returns></returns>
        /// <example>
        /// using (TransactionProcessor.Current.OpenTransaction())
        /// {
        ///     // Do work
        /// }
        /// </example>
        ITransactionScope OpenTransaction();

        ITransactionScope CurrentScope { get; }

        /// <summary>
        /// Attempt to write a new property value (attached to obj) into the current transaction, or
        /// open/close a new transaction if necessary.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="fieldName"></param>
        /// <param name="value"></param>
        void WriteProperty<T>(TransactableObject obj, string fieldName, T value, Action<ITransaction> transactionStateCallback = null);
    }

    public static class TransactionProcessor
    {
        // Phase 7: Current now resolves via the AsyncLocal-scoped TrackerSession
        // so fork scopes get their own transaction processor automatically.
        // The mProcessor fallback covers the narrow window during application
        // startup before CreateCurrent() publishes Default, and design-time
        // previewers that call SetTransactionProcessor explicitly.
        static ITransactionProcessor mProcessor;

        public static ITransactionProcessor Current
        {
            get
            {
                var session = Session.TrackerSession.Current;
                if (session?.Transactions != null)
                    return session.Transactions;
                return mProcessor;
            }
        }

        public static void SetTransactionProcessor(ITransactionProcessor processor)
        {
            mProcessor = processor;
        }
    }
}
