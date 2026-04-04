using System;

namespace EmoTracker.Data.Core.Transactions
{
    public enum TransactablePropertyReadBehavior
    {
        Default,
        AllowOpenTransactionRead
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class TransactablePropertyReadBehaviorAttribute : Attribute
    {
        public TransactablePropertyReadBehavior ReadBehavior { get; private set; }

        public TransactablePropertyReadBehaviorAttribute(TransactablePropertyReadBehavior behavior)
        {
            ReadBehavior = behavior;
        }
    }
}