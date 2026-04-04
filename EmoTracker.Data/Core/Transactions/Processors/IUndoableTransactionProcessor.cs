using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Core.Transactions.Processors
{
    public interface IUndoableTransactionProcessor : ITransactionProcessor
    {
        void ClearUndoHistory();

        void Undo();
    }
}
