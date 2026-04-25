using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace EmoTracker.SourceGenerators
{
    /// <summary>
    /// Roslyn incremental generator that emits implementation halves for partial
    /// properties annotated with one of <c>[KVImmutable]</c> / <c>[KVMutable]</c> /
    /// <c>[KVTransactable]</c>, optionally combined with <c>[OnChanged(nameof(Method))]</c>.
    ///
    /// <para>
    /// The generator runs over every project that references this assembly as an
    /// Analyzer; it produces no output for projects that do not declare any
    /// KV-attributed properties.
    /// </para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class KVPropertyGenerator : IIncrementalGenerator
    {
        const string AttrNS = "EmoTracker.Core.DataModel";
        const string KVImmutable = AttrNS + ".KVImmutableAttribute";
        const string KVMutable = AttrNS + ".KVMutableAttribute";
        const string KVTransactable = AttrNS + ".KVTransactableAttribute";
        const string OnChanged = AttrNS + ".OnChangedAttribute";

        const string ModelTypeBase = AttrNS + ".ModelTypeBase";
        const string TransactableModelTypeBase = "EmoTracker.Data.Core.DataModel.TransactableModelTypeBase";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Pull all properties that carry at least one of our marker attributes.
            // We then re-validate during transform — the predicate is the cheap path.
            var properties = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidatePropertyDeclaration(node),
                transform: static (ctx, _) => TryGetPropertyModel(ctx))
                .Where(static p => p != null)
                .Select(static (p, _) => p!);

            // Group by containing type so each emitted partial covers everything
            // declared in one place. Diagnostics ride along on the Property model.
            var grouped = properties.Collect();

            context.RegisterSourceOutput(grouped, static (spc, models) =>
            {
                if (models.IsDefaultOrEmpty) return;
                Emit(spc, models);
            });
        }

        // ---------------------------------------------------------------- syntax

        static bool IsCandidatePropertyDeclaration(SyntaxNode node)
        {
            if (node is not PropertyDeclarationSyntax p) return false;
            if (p.AttributeLists.Count == 0) return false;
            // Only properties that the user declared 'partial' are candidates.
            // We keep non-partial KV-attributed properties around so the transform
            // can emit a diagnostic for them, BUT cheap-filter is: any attribute.
            return true;
        }

        static PropertyModel? TryGetPropertyModel(GeneratorSyntaxContext ctx)
        {
            var prop = (PropertyDeclarationSyntax)ctx.Node;
            var sm = ctx.SemanticModel;

            var symbol = sm.GetDeclaredSymbol(prop) as IPropertySymbol;
            if (symbol == null) return null;

            // Locate KV-marker attributes on this property.
            int kvCount = 0;
            KVKind kvKind = KVKind.None;
            string? kvKeyOverride = null;

            foreach (var ad in symbol.GetAttributes())
            {
                var fullName = ad.AttributeClass?.ToDisplayString();
                if (fullName == null) continue;
                if (fullName == KVImmutable)    { kvKind = KVKind.Immutable;   kvCount++; kvKeyOverride = TryReadStringNamedArg(ad, "Key"); }
                else if (fullName == KVMutable) { kvKind = KVKind.Mutable;     kvCount++; kvKeyOverride = TryReadStringNamedArg(ad, "Key"); }
                else if (fullName == KVTransactable) { kvKind = KVKind.Transactable; kvCount++; }
            }

            if (kvCount == 0) return null;

            var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

            if (kvCount > 1)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.MultipleKVAttributes, prop.Identifier.GetLocation(), symbol.Name));

            bool isPartial = prop.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
            if (!isPartial)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.MustBePartialProperty, prop.Identifier.GetLocation(), symbol.Name));

            // Containing type must also be partial so we can emit our half.
            var containingType = symbol.ContainingType;
            bool containerPartial = containingType.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax() is TypeDeclarationSyntax tds && tds.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!containerPartial)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.ContainingTypeMustBePartial, prop.Identifier.GetLocation(), containingType.Name));

            // Inheritance constraints. Look up the well-known base types in the
            // current compilation rather than relying on string compares against
            // syntax — works across project references.
            var modelBaseSymbol = sm.Compilation.GetTypeByMetadataName(ModelTypeBase);
            var transactableBaseSymbol = sm.Compilation.GetTypeByMetadataName(TransactableModelTypeBase);

            bool inheritsModelBase = InheritsFrom(containingType, modelBaseSymbol);
            bool inheritsTransactableBase = InheritsFrom(containingType, transactableBaseSymbol);

            if (!inheritsModelBase)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.MustDeriveFromModelTypeBase, prop.Identifier.GetLocation(), symbol.Name, containingType.Name));

            if (kvKind == KVKind.Transactable && !inheritsTransactableBase)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.KVTransactableNotOnTransactableBase, prop.Identifier.GetLocation(), symbol.Name, containingType.Name));

            // Setter expectations.
            bool hasSetter = symbol.SetMethod != null;
            bool hasGetter = symbol.GetMethod != null;
            if (kvKind == KVKind.Immutable && hasSetter)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.KVImmutableHasSetter, prop.Identifier.GetLocation(), symbol.Name));
            if (kvKind != KVKind.Immutable && !hasSetter)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.KVMutableMustHaveSetter, prop.Identifier.GetLocation(), symbol.Name));

            // [OnChanged] resolution.
            string? onChangedMethodName = null;
            foreach (var ad in symbol.GetAttributes())
            {
                var fullName = ad.AttributeClass?.ToDisplayString();
                if (fullName != OnChanged) continue;
                if (ad.ConstructorArguments.Length >= 1 && ad.ConstructorArguments[0].Value is string s)
                {
                    onChangedMethodName = s;
                }
                break;
            }

            if (onChangedMethodName != null)
            {
                bool found = HasParameterlessInstanceMethod(containingType, onChangedMethodName);
                if (!found)
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.OnChangedTargetMissing, prop.Identifier.GetLocation(), symbol.Name, onChangedMethodName, containingType.Name));
            }

            string storageKey = kvKeyOverride ?? symbol.Name;

            return new PropertyModel(
                ContainingNamespace: containingType.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : containingType.ContainingNamespace.ToDisplayString(),
                ContainingTypeFullName: BuildContainingTypeNestingChain(containingType),
                ContainingTypeKeyword: containingType.IsRecord ? "record" : "class",
                ContainingTypeIsAbstract: containingType.IsAbstract,
                PropertyName: symbol.Name,
                PropertyTypeFullName: symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyAccessibility: AccessibilityToKeyword(symbol.DeclaredAccessibility),
                Kind: kvKind,
                StorageKey: storageKey,
                HasSetter: hasSetter,
                HasGetter: hasGetter,
                OnChangedMethodName: onChangedMethodName,
                Diagnostics: diagnostics.ToImmutable());
        }

        static string AccessibilityToKeyword(Accessibility a) => a switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.Private => "private",
            _ => "public",
        };

        // Returns a list of (typeKind, name, type-parameters?) tuples from the outermost
        // containing type inward, so we can emit nested type declarations correctly.
        static ImmutableArray<TypeNestingFrame> BuildContainingTypeNestingChain(INamedTypeSymbol type)
        {
            var stack = new List<TypeNestingFrame>();
            for (INamedTypeSymbol? cur = type; cur != null; cur = cur.ContainingType)
            {
                stack.Add(new TypeNestingFrame(
                    Keyword: cur.IsRecord ? "record" : (cur.TypeKind == TypeKind.Struct ? "struct" : "class"),
                    Name: cur.Name,
                    TypeParameters: cur.TypeParameters.Length == 0
                        ? null
                        : "<" + string.Join(", ", cur.TypeParameters.Select(tp => tp.Name)) + ">"));
            }
            stack.Reverse();
            return stack.ToImmutableArray();
        }

        static string? TryReadStringNamedArg(AttributeData ad, string name)
        {
            foreach (var na in ad.NamedArguments)
                if (na.Key == name && na.Value.Value is string s)
                    return s;
            return null;
        }

        static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
        {
            if (baseType == null) return false;
            for (var cur = (INamedTypeSymbol?)type; cur != null; cur = cur.BaseType)
                if (SymbolEqualityComparer.Default.Equals(cur, baseType))
                    return true;
            return false;
        }

        static bool HasParameterlessInstanceMethod(INamedTypeSymbol type, string name)
        {
            for (var cur = (INamedTypeSymbol?)type; cur != null; cur = cur.BaseType)
            {
                foreach (var m in cur.GetMembers(name).OfType<IMethodSymbol>())
                {
                    if (m.MethodKind != MethodKind.Ordinary) continue;
                    if (m.IsStatic) continue;
                    if (m.Parameters.Length != 0) continue;
                    return true;
                }
            }
            return false;
        }

        // ----------------------------------------------------------- emission

        static void Emit(SourceProductionContext spc, ImmutableArray<PropertyModel> models)
        {
            // Report all collected diagnostics first.
            foreach (var m in models)
                foreach (var d in m.Diagnostics)
                    spc.ReportDiagnostic(Diagnostic.Create(d.Descriptor, d.Location, d.MessageArgs));

            // Skip emission for any property that has its own errors.
            var emittable = models.Where(m => m.Diagnostics.IsDefaultOrEmpty || !m.Diagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)).ToArray();
            if (emittable.Length == 0) return;

            // Group by containing type so we can emit one file per partial class.
            var byType = emittable
                .GroupBy(m => (m.ContainingNamespace, NestingKey: string.Join(".", m.ContainingTypeFullName.Select(f => f.Name))));

            foreach (var group in byType)
            {
                var first = group.First();
                var sb = new StringBuilder();

                sb.AppendLine("// <auto-generated />");
                sb.AppendLine("// Generated by EmoTracker.SourceGenerators.KVPropertyGenerator.");
                sb.AppendLine("// Do not edit this file by hand.");
                sb.AppendLine("#nullable disable");
                sb.AppendLine("#pragma warning disable CS0108 // member hides inherited member; the partial-property half is intentional");
                sb.AppendLine();
                sb.AppendLine("using System;");
                sb.AppendLine("using System.Collections.Generic;");
                sb.AppendLine();
                int indent = 0;

                if (first.ContainingNamespace != null)
                {
                    sb.Append(Ind(indent)).Append("namespace ").Append(first.ContainingNamespace).AppendLine();
                    sb.Append(Ind(indent)).AppendLine("{");
                    indent++;
                }

                // Open each containing type as partial.
                foreach (var frame in first.ContainingTypeFullName)
                {
                    sb.Append(Ind(indent)).Append("partial ").Append(frame.Keyword).Append(' ').Append(frame.Name);
                    if (frame.TypeParameters != null) sb.Append(frame.TypeParameters);
                    sb.AppendLine();
                    sb.Append(Ind(indent)).AppendLine("{");
                    indent++;
                }

                foreach (var m in group)
                {
                    EmitProperty(sb, indent, m);
                }

                // Close types and namespace.
                for (int i = 0; i < first.ContainingTypeFullName.Length; i++)
                {
                    indent--;
                    sb.Append(Ind(indent)).AppendLine("}");
                }
                if (first.ContainingNamespace != null)
                {
                    indent--;
                    sb.Append(Ind(indent)).AppendLine("}");
                }

                // Hint name: namespace.Type.kvprops.g.cs (sanitized).
                string hint = (first.ContainingNamespace ?? "global") + "." +
                              string.Join(".", first.ContainingTypeFullName.Select(f => f.Name)) +
                              ".kvprops.g.cs";
                spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
            }
        }

        static void EmitProperty(StringBuilder sb, int indent, PropertyModel m)
        {
            string ind = Ind(indent);
            string typeName = m.PropertyTypeFullName;
            string keyLiteral = "\"" + m.StorageKey.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            switch (m.Kind)
            {
                case KVKind.Immutable:
                    // Getter only: read from ImmutableData.
                    sb.AppendLine();
                    sb.Append(ind).Append(m.PropertyAccessibility).Append(" partial ").Append(typeName).Append(' ').Append(m.PropertyName).AppendLine();
                    sb.Append(ind).Append("    => this.ImmutableData.GetValue<").Append(typeName).Append(">(").Append(keyLiteral).Append(", default(").Append(typeName).AppendLine("));");
                    break;

                case KVKind.Mutable:
                    sb.AppendLine();
                    sb.Append(ind).Append(m.PropertyAccessibility).Append(" partial ").Append(typeName).Append(' ').Append(m.PropertyName).AppendLine();
                    sb.Append(ind).AppendLine("{");
                    sb.Append(ind).Append("    get => this.MutableData.GetValue<").Append(typeName).Append(">(").Append(keyLiteral).Append(", default(").Append(typeName).AppendLine("));");
                    sb.Append(ind).AppendLine("    set");
                    sb.Append(ind).AppendLine("    {");
                    sb.Append(ind).Append("        var __current = this.MutableData.GetValue<").Append(typeName).Append(">(").Append(keyLiteral).Append(", default(").Append(typeName).AppendLine("));");
                    sb.Append(ind).Append("        if (!global::System.Collections.Generic.EqualityComparer<").Append(typeName).AppendLine(">.Default.Equals(__current, value))");
                    sb.Append(ind).AppendLine("        {");
                    sb.Append(ind).Append("            this.NotifyPropertyChanging(nameof(").Append(m.PropertyName).AppendLine("));");
                    sb.Append(ind).Append("            this.MutableData.SetValue<").Append(typeName).Append(">(").Append(keyLiteral).AppendLine(", value);");
                    if (m.OnChangedMethodName != null)
                    {
                        sb.Append(ind).Append("            this.").Append(m.OnChangedMethodName).AppendLine("();");
                    }
                    sb.Append(ind).Append("            this.NotifyPropertyChanged(nameof(").Append(m.PropertyName).AppendLine("));");
                    sb.Append(ind).AppendLine("        }");
                    sb.Append(ind).AppendLine("    }");
                    sb.Append(ind).AppendLine("}");
                    break;

                case KVKind.Transactable:
                    sb.AppendLine();
                    sb.Append(ind).Append(m.PropertyAccessibility).Append(" partial ").Append(typeName).Append(' ').Append(m.PropertyName).AppendLine();
                    sb.Append(ind).AppendLine("{");
                    sb.Append(ind).Append("    get => this.GetTransactableProperty<").Append(typeName).Append(">(nameof(").Append(m.PropertyName).AppendLine("));");
                    sb.Append(ind).AppendLine("    set");
                    sb.Append(ind).AppendLine("    {");
                    if (m.OnChangedMethodName != null)
                    {
                        sb.Append(ind).Append("        this.SetTransactableProperty<").Append(typeName).Append(">(value, _ => this.").Append(m.OnChangedMethodName).Append("(), nameof(").Append(m.PropertyName).AppendLine("));");
                    }
                    else
                    {
                        sb.Append(ind).Append("        this.SetTransactableProperty<").Append(typeName).Append(">(value, null, nameof(").Append(m.PropertyName).AppendLine("));");
                    }
                    sb.Append(ind).AppendLine("    }");
                    sb.Append(ind).AppendLine("}");
                    break;
            }
        }

        static string Ind(int n) => new string(' ', n * 4);
    }

    internal enum KVKind
    {
        None,
        Immutable,
        Mutable,
        Transactable,
    }

    internal sealed record PropertyModel(
        string? ContainingNamespace,
        ImmutableArray<TypeNestingFrame> ContainingTypeFullName,
        string ContainingTypeKeyword,
        bool ContainingTypeIsAbstract,
        string PropertyName,
        string PropertyTypeFullName,
        string PropertyAccessibility,
        KVKind Kind,
        string StorageKey,
        bool HasSetter,
        bool HasGetter,
        string? OnChangedMethodName,
        ImmutableArray<DiagnosticInfo> Diagnostics);

    internal sealed record TypeNestingFrame(string Keyword, string Name, string? TypeParameters);

    internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, Location Location, params object?[] MessageArgs);
}
