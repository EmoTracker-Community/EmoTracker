using System.Runtime.CompilerServices;

namespace EmoTracker.Data.Core.Transactions
{
    /// <summary>
    /// Common contract implemented by anything that can be a write target for the
    /// <see cref="TransactionProcessor"/> — both the legacy <see cref="TransactableObject"/>
    /// (which stores transactable values in its own dictionary) and the data-model-v2
    /// <c>TransactableModelTypeBase</c> (which routes them into a key-value store).
    ///
    /// The processor uses an instance only as a Dictionary key (reference identity) and
    /// to read the current value of a property when capturing the original-value side of
    /// an undoable transaction. Anything else lives on the concrete type.
    /// </summary>
    public interface ITransactableObject
    {
        /// <summary>
        /// Reads the current value of a transactable property by name.
        /// </summary>
        /// <typeparam name="T">The property's value type.</typeparam>
        /// <param name="propertyName">The name of the property; defaults to the caller's name when invoked from inside a property body.</param>
        /// <param name="bForceReadFromOpenTransaction">When true, prefer the value pending in the currently-open transaction over the committed value if present.</param>
        T GetTransactableProperty<T>([CallerMemberName] string propertyName = null, bool bForceReadFromOpenTransaction = false);
    }
}
