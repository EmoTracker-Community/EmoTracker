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
        static ITransactionProcessor mProcessor;

        public static ITransactionProcessor Current
        {
            get { return mProcessor; }
        }

        public static void SetTransactionProcessor(ITransactionProcessor processor)
        {
            if (Current != null)
            {
                //  Do error checking; e.g. check for open transaction scopes, etc.
            }

            mProcessor = processor;
        }
    }
}
