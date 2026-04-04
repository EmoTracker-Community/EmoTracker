using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Core.Transactions.Processors
{
    public class LocalTransactionProcessorWithUndo : IUndoableTransactionProcessor
    {
        class Transaction : ITransaction, ITransactionScope
        {
            #region -- ITransactionScope --

            ITransaction ITransactionScope.Transaction
            {
                get { return this; }
            }

            #endregion

            #region -- ITransaction --

            TransactionStatus mTransactionStatus = TransactionStatus.Pending;
            public TransactionStatus Status
            {
                get { return mTransactionStatus; }
            }

            enum ReadSource
            {
                Original,
                Target
            }

            ReadSource mReadSource = ReadSource.Target;

            public bool HasPropertyValue(TransactableObject src, string propertyName)
            {
                KeyValuePair<TransactableObject, string> key = new KeyValuePair<TransactableObject, string>(src, propertyName);
                return mValueStates.ContainsKey(key);
            }

            public T GetPropertyValue<T>(TransactableObject src, string propertyName)
            {
                try
                {
                    KeyValuePair<TransactableObject, string> key = new KeyValuePair<TransactableObject, string>(src, propertyName);

                    ValueStateBase fieldValueBase;
                    if (mValueStates.TryGetValue(key, out fieldValueBase))
                    {
                        ValueState<T> fieldValue = (ValueState<T>)fieldValueBase;
                        switch (mReadSource)
                        {
                            case ReadSource.Original:
                                return fieldValue.OriginalValue;

                            case ReadSource.Target:
                                return fieldValue.TargetValue;
                        }
                    }
                }
                catch
                {
                }

                return default(T);
            }

            #endregion

            abstract class ValueStateBase
            {
                public TransactableObject Source;
                public Action<ITransaction> ProcessedAction;
            }

            class ValueState<T> : ValueStateBase
            {
                public T OriginalValue;
                public T TargetValue;

                public ValueState(TransactableObject source, T originalValue, T targetValue, Action<ITransaction> action)
                {
                    Source = source;
                    ProcessedAction = action;
                    OriginalValue = originalValue;
                    TargetValue = targetValue;
                }
            }

            LocalTransactionProcessorWithUndo mProcessor;
            Dictionary<KeyValuePair<TransactableObject, string>, ValueStateBase> mValueStates = new Dictionary<KeyValuePair<TransactableObject, string>, ValueStateBase>();

            public Transaction(LocalTransactionProcessorWithUndo processor)
            {
                mProcessor = processor;
            }

            public bool Empty
            {
                get { return mValueStates.Count == 0; }
            }

            public void WriteProperty<T>(TransactableObject source, string fieldName, T value, Action<ITransaction> action = null)
            {
                KeyValuePair<TransactableObject, string> key = new KeyValuePair<TransactableObject, string>(source, fieldName);
                mValueStates[key] = new ValueState<T>(source, source.GetTransactableProperty<T>(fieldName), value, action);
            }

            public void Dispose()
            {
                mProcessor.ProcessTransaction(this);
            }

            public void Commit()
            {
                mTransactionStatus = TransactionStatus.Completed;
                foreach (var entry in mValueStates)
                {
                    entry.Value.ProcessedAction(this);
                }
            }

            public void Undo()
            {
                using (new LocationDatabase.SuspendRefreshScope())
                {
                    mReadSource = ReadSource.Original;
                    foreach (var entry in mValueStates)
                    {
                        entry.Value.ProcessedAction(this);
                    }
                }
            }
        }

        List<Transaction> mUndoStack = new List<Transaction>();
        Transaction mOpenTransaction = null;

        static readonly int MaxUndoSteps = 250;

        public ITransactionScope OpenTransaction()
        {
            if (mOpenTransaction == null)
            {
                mOpenTransaction = new Transaction(this);
                return mOpenTransaction;
            }

            return null;
        }

        public ITransactionScope CurrentScope
        {
            get { return mOpenTransaction; }
        }

        public void WriteProperty<T>(TransactableObject obj, string fieldName, T value, Action<ITransaction> transactionStateCallback = null)
        {
            using (OpenTransaction())
            {
                mOpenTransaction.WriteProperty<T>(obj, fieldName, value, transactionStateCallback);
            }
        }

        void ProcessTransaction(Transaction transaction)
        {
            try
            {
                Transaction toProcess = mOpenTransaction;
                mOpenTransaction = null;

                if (!toProcess.Empty)
                {
                    mUndoStack.Add(toProcess);
                    toProcess.Commit();

                    //  Constraint the maximum depth of the undo stack
                    while (mUndoStack.Count > MaxUndoSteps)
                    {
                        mUndoStack.RemoveAt(0);
                    }
                }
            }
            finally
            {
                //  Ensure that our current transaction is cleared out even in the
                //  event of an exception
                mOpenTransaction = null;
            }
        }

        public void ClearUndoHistory()
        {
            if (mOpenTransaction != null)
                throw new InvalidOperationException("Cannot clear undo stack while a transaction is open for write");

            mUndoStack.Clear();
        }

        public void Undo()
        {
            Transaction undo = mUndoStack.LastOrDefault();
            if (undo != null)
            {
                mUndoStack.Remove(undo);
                undo.Undo();
            }
        }
    }
}
