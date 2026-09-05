using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace ChibiRuby.Serializer.SourceGenerator;

/// <summary>
/// Walks a member/root type and collects registration statements for every formatter
/// instantiation the type needs, fully constructed at compile time
/// (e.g. <c>GeneratedResolver.Register(new ListFormatter&lt;int&gt;());</c>).
/// Emitting these keeps trimmed / NativeAOT / IL2CPP builds off the runtime
/// MakeGenericType path in BuiltinResolver, which cannot instantiate value-type
/// generic arguments that were never compiled.
/// </summary>
static class BuiltinFormatterWalker
{
    const string Ns = "global::ChibiRuby.Serializer.";

    // targetOriginalDefinition (e.g. List`1) -> formatter definition (e.g. ListFormatter`1),
    // discovered from the referenced ChibiRuby.Serializer assembly, cached per assembly symbol.
    static readonly ConditionalWeakTable<IAssemblySymbol, Dictionary<INamedTypeSymbol, INamedTypeSymbol>> MapCache = new();

    /// <summary>
    /// Collects registration statements for <paramref name="type"/> into <paramref name="statements"/>.
    /// </summary>
    public static void Collect(ITypeSymbol type, ReferenceSymbols references, ISet<string> statements)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
            {
                var formatter = array.Rank switch
                {
                    1 => "ArrayFormatter",
                    2 => "TwoDimensionalArrayFormatter",
                    3 => "ThreeDimensionalArrayFormatter",
                    4 => "FourDimensionalArrayFormatter",
                    _ => null,
                };
                if (formatter is null)
                {
                    return;
                }
                statements.Add(Register($"{Ns}{formatter}<{Display(array.ElementType)}>"));
                Collect(array.ElementType, references, statements);
                return;
            }
            case INamedTypeSymbol named:
            {
                if (named.TypeKind == TypeKind.Enum)
                {
                    statements.Add(Register($"{Ns}EnumAsStringFormatter<{Display(named)}>"));
                    return;
                }

                if (named.GetAttributes().Any(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, references.MRubyObjectAttribute)))
                {
                    // A [MRubyObject] type registers itself (and its member formatters) from its
                    // generated __RegisterMRubyValueFormatter. Calling it here roots the closed
                    // instantiation for AOT; the method's re-entrancy guard makes this cycle-safe.
                    statements.Add($"{DisplayBare(named)}.__RegisterMRubyValueFormatter();");
                    foreach (var arg in named.TypeArguments)
                    {
                        Collect(arg, references, statements);
                    }
                    return;
                }

                if (named is { IsGenericType: true, IsUnboundGenericType: false })
                {
                    if (GetFormatterMap(references).TryGetValue(named.OriginalDefinition, out var formatter))
                    {
                        var args = string.Join(", ", named.TypeArguments.Select(Display));
                        statements.Add(Register($"{FormatterTypeName(formatter)}<{args}>"));
                    }
                    foreach (var arg in named.TypeArguments)
                    {
                        Collect(arg, references, statements);
                    }
                }
                return;
            }
            default:
                return; // type parameters etc. resolve at the closed instantiation
        }
    }

    static Dictionary<INamedTypeSymbol, INamedTypeSymbol> GetFormatterMap(ReferenceSymbols references)
    {
        var formatterInterface = references.MRubyValueFormatterInterface;
        return MapCache.GetValue(
            formatterInterface.ContainingAssembly,
            assembly => CreateFormatterMap(assembly, formatterInterface));
    }

    /// <summary>
    /// Derives the generic-formatter map from the serializer assembly itself, by convention:
    /// a public, non-abstract generic class <c>F&lt;T1..Tn&gt;</c> with a public parameterless
    /// constructor that implements <c>IMRubyValueFormatter&lt;Target&gt;</c>, where
    /// <c>Target</c> is a generic type constructed exactly from <c>T1..Tn</c> in order
    /// (e.g. <c>ListFormatter&lt;T&gt; : IMRubyValueFormatter&lt;List&lt;T&gt;?&gt;</c>),
    /// maps <c>Target</c>'s definition to <c>F</c>. This is the same shape BuiltinResolver
    /// instantiates at runtime via MakeGenericType, so the two stay in sync by construction.
    /// Formatters over a bare type parameter (EnumAsStringFormatter, RObjectFormatter) and
    /// array formatters do not match and keep their dedicated handling above.
    /// </summary>
    static Dictionary<INamedTypeSymbol, INamedTypeSymbol> CreateFormatterMap(
        IAssemblySymbol serializerAssembly,
        INamedTypeSymbol formatterInterface)
    {
        var map = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var formatter in EnumerateTypes(serializerAssembly.GlobalNamespace))
        {
            if (formatter is not
                {
                    TypeKind: TypeKind.Class,
                    IsAbstract: false,
                    IsGenericType: true,
                    // generated code in user assemblies must be able to `new` it
                    DeclaredAccessibility: Accessibility.Public,
                })
            {
                continue;
            }
            if (!formatter.InstanceConstructors.Any(x =>
                    x.Parameters.Length == 0 && x.DeclaredAccessibility == Accessibility.Public))
            {
                continue;
            }

            foreach (var implemented in formatter.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, formatterInterface))
                {
                    continue;
                }
                if (implemented.TypeArguments[0] is not INamedTypeSymbol { IsGenericType: true } target ||
                    target.TypeArguments.Length != formatter.TypeParameters.Length)
                {
                    continue;
                }

                var argumentsMatch = true;
                for (var i = 0; i < target.TypeArguments.Length; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(target.TypeArguments[i], formatter.TypeParameters[i]))
                    {
                        argumentsMatch = false;
                        break;
                    }
                }
                if (!argumentsMatch)
                {
                    continue;
                }

                var key = target.OriginalDefinition;
                // Deterministic pick if two formatters ever target the same type.
                if (!map.TryGetValue(key, out var existing) ||
                    string.CompareOrdinal(FormatterTypeName(formatter), FormatterTypeName(existing)) < 0)
                {
                    map[key] = formatter;
                }
            }
        }
        return map;
    }

    static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol child:
                    foreach (var type in EnumerateTypes(child))
                    {
                        yield return type;
                    }
                    break;
                case INamedTypeSymbol type:
                    yield return type; // formatters are top-level; no need to walk nested types
                    break;
            }
        }
    }

    static string FormatterTypeName(INamedTypeSymbol formatter) =>
        $"global::{formatter.ContainingNamespace.ToDisplayString()}.{formatter.Name}";

    static string Register(string formatterType) =>
        $"{Ns}GeneratedResolver.Register(new {formatterType}());";

    static string Display(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // Display without a top-level nullable annotation, for use as a receiver of a static call.
    static string DisplayBare(ITypeSymbol t) =>
        t.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
