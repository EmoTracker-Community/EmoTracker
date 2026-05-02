using System;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Marks a partial property whose body should be source-generated to read from
    /// <c>ImmutableData</c>. Getter only — applying <see cref="KVImmutableAttribute"/>
    /// to a property that declares a setter is a generator-time error.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class KVImmutableAttribute : Attribute
    {
        /// <summary>
        /// Optional override for the storage key. Defaults to the property name.
        /// </summary>
        public string Key { get; set; }
    }

    /// <summary>
    /// Marks a partial property whose body should be source-generated to read from /
    /// write to <c>MutableData</c> on the enclosing model type.
    /// Mutating writes raise <c>PropertyChanging</c> and <c>PropertyChanged</c>, write
    /// the new value through the store's per-key COW boundary, and invoke any
    /// <see cref="OnChangedAttribute"/>-declared callback.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class KVMutableAttribute : Attribute
    {
        /// <summary>
        /// Optional override for the storage key. Defaults to the property name.
        /// </summary>
        public string Key { get; set; }
    }

    /// <summary>
    /// Marks a partial property whose body should be source-generated to route
    /// through <c>GetTransactableProperty</c> / <c>SetTransactableProperty</c> on the
    /// enclosing <c>TransactableModelTypeBase</c> — and therefore through the
    /// <c>TransactionProcessor</c> for undo/redo support.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class KVTransactableAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a partial property whose body should be source-generated to expose a
    /// per-state override over a definition-time default. The definition value is
    /// stored in <c>ImmutableData</c> at parse time (under key <c>Foo__def</c> for
    /// a property named <c>Foo</c>); per-state setter writes go to <c>MutableData</c>
    /// (under key <c>Foo</c>, per-key COW). The getter returns the override when
    /// present (detected by <see cref="MutableKeyValueStore"/>'s ContainsKey, so an
    /// explicit override-to-null is honored as "force null" — distinct from "no
    /// override"); otherwise it returns the definition default.
    ///
    /// <para>
    /// Designed for the layout-element pattern: properties whose value comes from
    /// the parse step but consumers may want to mutate at runtime per-state
    /// (e.g. <c>Width</c>, <c>Background</c>, <c>DockLocation</c>). For properties
    /// where there's no meaningful definition default — i.e. the value is purely
    /// runtime state with no parse-time origin — use <see cref="KVMutableAttribute"/>
    /// instead.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class KVOverridableAttribute : Attribute
    {
        /// <summary>
        /// Optional override for the storage key base. Defaults to the property
        /// name. Immutable storage uses <c>{Key}__def</c>; mutable (override)
        /// storage uses <c>{Key}</c>.
        /// </summary>
        public string Key { get; set; }
    }

    /// <summary>
    /// Names a parameterless instance method (typically <c>protected void UpdateXxx()</c>)
    /// to be invoked after the annotated property's value has changed.
    ///
    /// <para>
    /// The named method must exist on the enclosing class (or a base) and take no
    /// parameters; missing methods or wrong signatures produce generator diagnostics.
    /// On <c>[KVTransactable]</c> properties the callback is wired into the post-
    /// processed action of the transaction; on <c>[KVMutable]</c> properties it is
    /// invoked synchronously inside the setter, after the store write and before the
    /// post-change <c>PropertyChanged</c> notification.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class OnChangedAttribute : Attribute
    {
        /// <summary>The name of the parameterless instance method to invoke.</summary>
        public string MethodName { get; }

        public OnChangedAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
