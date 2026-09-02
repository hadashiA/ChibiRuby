using System.Collections.Generic;
using System.Linq;
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

    // Keep in sync with BuiltinResolver.KnownGenericTypes.
    static readonly Dictionary<string, string> KnownGenericFormatters = new()
    {
        { "System.Nullable`1", "NullableFormatter" },
        { "System.Collections.Generic.KeyValuePair`2", "KeyValuePairFormatter" },

        { "System.Tuple`1", "TupleFormatter" },
        { "System.Tuple`2", "TupleFormatter" },
        { "System.Tuple`3", "TupleFormatter" },
        { "System.Tuple`4", "TupleFormatter" },
        { "System.Tuple`5", "TupleFormatter" },
        { "System.ValueTuple`1", "ValueTupleFormatter" },
        { "System.ValueTuple`2", "ValueTupleFormatter" },
        { "System.ValueTuple`3", "ValueTupleFormatter" },
        { "System.ValueTuple`4", "ValueTupleFormatter" },
        { "System.ValueTuple`5", "ValueTupleFormatter" },

        { "System.Collections.Generic.List`1", "ListFormatter" },
        { "System.Collections.Generic.Stack`1", "StackFormatter" },
        { "System.Collections.Generic.Queue`1", "QueueFormatter" },
        { "System.Collections.Generic.LinkedList`1", "LinkedListFormatter" },
        { "System.Collections.Generic.HashSet`1", "HashSetFormatter" },
        { "System.Collections.Generic.SortedSet`1", "SortedSetFormatter" },

        { "System.Collections.ObjectModel.Collection`1", "CollectionFormatter" },
        { "System.Collections.ObjectModel.ReadOnlyCollection`1", "ReadOnlyCollectionFormatter" },
        { "System.Collections.Concurrent.BlockingCollection`1", "BlockingCollectionFormatter" },
        { "System.Collections.Concurrent.ConcurrentQueue`1", "ConcurrentQueueFormatter" },
        { "System.Collections.Concurrent.ConcurrentStack`1", "ConcurrentStackFormatter" },
        { "System.Collections.Concurrent.ConcurrentBag`1", "ConcurrentBagFormatter" },

        { "System.Collections.Generic.Dictionary`2", "DictionaryFormatter" },
        { "System.Collections.Generic.SortedDictionary`2", "SortedDictionaryFormatter" },
        { "System.Collections.Concurrent.ConcurrentDictionary`2", "ConcurrentDictionaryFormatter" },

        { "System.Collections.Generic.IEnumerable`1", "InterfaceEnumerableFormatter" },
        { "System.Collections.Generic.ICollection`1", "InterfaceCollectionFormatter" },
        { "System.Collections.Generic.IReadOnlyCollection`1", "InterfaceReadOnlyCollectionFormatter" },
        { "System.Collections.Generic.IList`1", "InterfaceListFormatter" },
        { "System.Collections.Generic.IReadOnlyList`1", "InterfaceReadOnlyListFormatter" },
        { "System.Collections.Generic.IDictionary`2", "InterfaceDictionaryFormatter" },
        { "System.Collections.Generic.IReadOnlyDictionary`2", "InterfaceReadOnlyDictionaryFormatter" },
        { "System.Collections.Generic.ISet`1", "InterfaceSetFormatter" },
    };

    /// <summary>
    /// Collects registration statements for <paramref name="type"/> into <paramref name="statements"/>.
    /// Returns true when the walk produced at least one statement or the type is covered by its own
    /// generated registration.
    /// </summary>
    public static bool Collect(ITypeSymbol type, INamedTypeSymbol mrubyObjectAttribute, ISet<string> statements)
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
                    return false;
                }
                statements.Add(Register($"{Ns}{formatter}<{Display(array.ElementType)}>"));
                Collect(array.ElementType, mrubyObjectAttribute, statements);
                return true;
            }
            case INamedTypeSymbol named:
            {
                if (named.TypeKind == TypeKind.Enum)
                {
                    statements.Add(Register($"{Ns}EnumAsStringFormatter<{Display(named)}>"));
                    return true;
                }

                if (named.GetAttributes().Any(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, mrubyObjectAttribute)))
                {
                    // A [MRubyObject] type registers itself (and its member formatters) from its
                    // generated __RegisterMRubyValueFormatter. Calling it here roots the closed
                    // instantiation for AOT; the method's re-entrancy guard makes this cycle-safe.
                    statements.Add($"{DisplayBare(named)}.__RegisterMRubyValueFormatter();");
                    foreach (var arg in named.TypeArguments)
                    {
                        Collect(arg, mrubyObjectAttribute, statements);
                    }
                    return true;
                }

                if (named is { IsGenericType: true, IsUnboundGenericType: false })
                {
                    var handled = false;
                    var metadataName = $"{named.ConstructedFrom.ContainingNamespace.ToDisplayString()}.{named.ConstructedFrom.MetadataName}";
                    if (KnownGenericFormatters.TryGetValue(metadataName, out var formatterName))
                    {
                        var args = string.Join(", ", named.TypeArguments.Select(Display));
                        statements.Add(Register($"{Ns}{formatterName}<{args}>"));
                        handled = true;
                    }
                    foreach (var arg in named.TypeArguments)
                    {
                        handled |= Collect(arg, mrubyObjectAttribute, statements);
                    }
                    return handled;
                }
                return false;
            }
            default:
                return false; // type parameters etc. resolve at the closed instantiation
        }
    }

    static string Register(string formatterType) =>
        $"{Ns}GeneratedResolver.Register(new {formatterType}());";

    static string Display(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // Display without a top-level nullable annotation, for use as a receiver of a static call.
    static string DisplayBare(ITypeSymbol t) =>
        t.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
