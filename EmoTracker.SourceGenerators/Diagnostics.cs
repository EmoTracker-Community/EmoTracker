using Microsoft.CodeAnalysis;

namespace EmoTracker.SourceGenerators
{
    internal static class Diagnostics
    {
        const string Category = "EmoTracker.DataModel";

        public static readonly DiagnosticDescriptor MultipleKVAttributes = new DiagnosticDescriptor(
            id: "EMODM001",
            title: "Property declares multiple KV attributes",
            messageFormat: "Property '{0}' declares more than one of [KVImmutable], [KVMutable], [KVTransactable], [KVOverridable]; pick exactly one",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MustBePartialProperty = new DiagnosticDescriptor(
            id: "EMODM002",
            title: "KV-attributed property must be partial",
            messageFormat: "Property '{0}' is annotated with a KV attribute but is not declared 'partial'; the generator emits the implementation half",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new DiagnosticDescriptor(
            id: "EMODM003",
            title: "Containing type must be partial",
            messageFormat: "The class containing KV-attributed properties must be declared 'partial' so the generator can emit a sibling declaration; '{0}' is not",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KVImmutableHasSetter = new DiagnosticDescriptor(
            id: "EMODM004",
            title: "[KVImmutable] property must not declare a setter",
            messageFormat: "Property '{0}' is annotated with [KVImmutable] but declares a setter; immutable definition data is read-only",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KVMutableMustHaveSetter = new DiagnosticDescriptor(
            id: "EMODM005",
            title: "[KVMutable] property must declare a setter",
            messageFormat: "Property '{0}' is annotated with [KVMutable] / [KVTransactable] / [KVOverridable] but declares no setter; the generator has nothing to emit",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KVTransactableNotOnTransactableBase = new DiagnosticDescriptor(
            id: "EMODM006",
            title: "[KVTransactable] requires TransactableModelTypeBase",
            messageFormat: "Property '{0}' on type '{1}' is annotated with [KVTransactable] but the enclosing type does not derive from TransactableModelTypeBase",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MustDeriveFromModelTypeBase = new DiagnosticDescriptor(
            id: "EMODM007",
            title: "KV-attributed property requires ModelTypeBase",
            messageFormat: "Property '{0}' on type '{1}' is annotated with a KV attribute but the enclosing type does not derive from ModelTypeBase",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OnChangedTargetMissing = new DiagnosticDescriptor(
            id: "EMODM008",
            title: "[OnChanged] target method not found",
            messageFormat: "Property '{0}' references method '{1}' via [OnChanged] but no parameterless instance method with that name was found on '{2}' or its base types",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
