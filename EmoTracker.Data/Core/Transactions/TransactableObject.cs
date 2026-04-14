using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EmoTracker.Data.Core.Transactions
{
    public class TransactableObject : ObservableObject
    {
        static Dictionary<Type, Dictionary<string, bool>> mGlobalReadFromOpenTransactionCache = new Dictionary<Type, Dictionary<string, bool>>();

        Dictionary<string, bool> mLocalReadFromOpenTransactionCache;
        readonly Dictionary<string, object> mOwnPropertyStore = new Dictionary<string, object>();

        /// <summary>
        /// Backing store for transactable property values. Defaults to a per-instance
        /// dictionary; subclasses may override to source the dictionary from an
        /// external session-owned store (see <see cref="EmoTracker.Data.Items.ItemBase"/>
        /// → <see cref="EmoTracker.Data.Items.ItemStateStore"/>) so that the mutable
        /// state of a logical object can be moved or cloned independently of the
        /// object identity itself.
        /// </summary>
        protected virtual Dictionary<string, object> PropertyStore => mOwnPropertyStore;

        private bool ShouldReadFromOpenTransaction(System.Type hostType, string propertyName)
        {
            bool bAllowRead = false;

            if (mLocalReadFromOpenTransactionCache == null)
            {
                if (!mGlobalReadFromOpenTransactionCache.TryGetValue(hostType, out mLocalReadFromOpenTransactionCache))
                    mGlobalReadFromOpenTransactionCache[hostType] = mLocalReadFromOpenTransactionCache = new Dictionary<string, bool>();
            }

            if (mLocalReadFromOpenTransactionCache.TryGetValue(propertyName, out bAllowRead))
                return bAllowRead;

            PropertyInfo propInfo = hostType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propInfo != null)
            {
                TransactablePropertyReadBehaviorAttribute a = Attribute.GetCustomAttribute(propInfo, typeof(TransactablePropertyReadBehaviorAttribute)) as TransactablePropertyReadBehaviorAttribute;
                if (a != null)
                {
                    bAllowRead = a.ReadBehavior == TransactablePropertyReadBehavior.AllowOpenTransactionRead;
                }
            }

            mLocalReadFromOpenTransactionCache[propertyName] = bAllowRead;
            return bAllowRead;
        }

        private T GetCurrentTransactablePropertyValue<T>(string propertyName)
        {
            object abstractValue;
            if (PropertyStore.TryGetValue(propertyName, out abstractValue))
            {
                try
                {
                    T value = (T)abstractValue;
                    return value;
                }
                catch
                {
                    throw new InvalidCastException(string.Format("Failed to read property '{0}' as type `{1}`", propertyName, typeof(T)));
                }
            }

            return default(T);
        }

        public T GetTransactableProperty<T>([CallerMemberName] string propertyName = null, bool bForceReadFromOpenTransaction = false)
        {
            if (bForceReadFromOpenTransaction || ShouldReadFromOpenTransaction(this.GetType(), propertyName))
            {
                ITransactionScope scope = TransactionProcessor.Current.CurrentScope;
                if (scope != null)
                {
                    if (scope.Transaction != null && scope.Transaction.HasPropertyValue(this, propertyName))
                        return scope.Transaction.GetPropertyValue<T>(this, propertyName);
                }
            }

            return GetCurrentTransactablePropertyValue<T>(propertyName);
        }
        protected bool ForceSetTransactableProperty<T>(T value, Action<T> onTransactionProcessed = null, [CallerMemberName] string propertyName = null)
        {
            if (!SetTransactableProperty(value, onTransactionProcessed, propertyName) && onTransactionProcessed != null)
                onTransactionProcessed(value);

            return true;
        }


        /// <summary>
        /// Reads a session-local ephemeral value from the same
        /// <see cref="PropertyStore"/> backing <see cref="SetTransactableProperty{T}"/>
        /// — but bypasses the transaction processor. Intended for derived /
        /// cached values that must not appear in the undo stack yet still need
        /// to be session-local so a fork can hold independent results
        /// (e.g. cached accessibility levels recomputed by
        /// <c>Location.RefreshAccessibility</c>).
        /// </summary>
        protected T GetSessionLocal<T>([CallerMemberName] string propertyName = null)
        {
            return GetCurrentTransactablePropertyValue<T>(propertyName);
        }

        /// <summary>
        /// Writes a session-local ephemeral value into <see cref="PropertyStore"/>
        /// without opening a transaction. Fires INPC on change. See
        /// <see cref="GetSessionLocal{T}"/>.
        /// </summary>
        protected bool SetSessionLocal<T>(T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(GetCurrentTransactablePropertyValue<T>(propertyName), value))
                return false;
            NotifyPropertyChanging(propertyName);
            PropertyStore[propertyName] = value;
            NotifyPropertyChanged(propertyName);
            return true;
        }

        protected bool SetTransactableProperty<T>(T value, Action<T> onTransactionProcessed = null, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(GetTransactableProperty<T>(propertyName), value))
                return false;

            TransactionProcessor.Current.WriteProperty(this, propertyName, value, (transactionState) =>
            {
                if (transactionState.Status == TransactionStatus.Completed)
                {
                    try
                    {
                        T resultValue = transactionState.GetPropertyValue<T>(this, propertyName);
                        NotifyPropertyChanging(propertyName);
                            
                        PropertyStore[propertyName] = resultValue;
                        onTransactionProcessed?.Invoke(resultValue);

                        NotifyPropertyChanged(propertyName);
                    }
                    catch
                    {
                    }
                }
            });

            return true;
        }
    }
}
