using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Core.Transactions.Processors
{
#if false
    public class NullProcessor : ITransactionProcessor
    {
        class NullTransactionState : ITransactionState
        {
        }

        public ITransactionScope OpenTransaction()
        {
            return null;
        }

        public void WriteProperty<T>(TransactableObject obj, string fieldName, T value, Action<ITransactionState> transactionStateCallback = null)
        {
            if (transactionStateCallback != null)
            {
                transactionStateCallback(new NullTransactionState()
                {
                    Status = TransactionStatus.Rejected
                });
            }
        }
    }
#endif
}
