using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Core.Transactions.Processors
{
#if false
    public class TrivialProcessor : ITransactionProcessor
    {
        class TrivialTransactionState : TransactionState
        {
        }

        public ITransactionScope OpenTransaction()
        {
            return null;
        }

        public void WriteProperty<T>(TransactableObject obj, string fieldName, T value, Action<TransactionState> transactionStateCallback = null)
        {
            if (transactionStateCallback != null)
            {
                var transactionState = new TrivialTransactionState()
                {
                    Status = TransactionStatus.Completed,
                };
                transactionState.Properties[fieldName] = value;
                transactionStateCallback(transactionState);
            }
        }
    }
#endif
}
