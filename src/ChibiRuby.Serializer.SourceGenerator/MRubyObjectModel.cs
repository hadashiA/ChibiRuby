using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ChibiRuby.Serializer.SourceGenerator;

/// <summary>
/// Value-equatable wrapper around <see cref="ImmutableArray{T}"/> so that records
/// containing it participate correctly in the incremental generator cache.
/// </summary>
readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    readonly ImmutableArray<T> array;

    public EquatableArray(ImmutableArray<T> array) => this.array = array;

    public int Count => array.IsDefault ? 0 : array.Length;
    public T this[int index] => array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (array.IsDefault) return other.array.IsDefault;
        if (other.array.IsDefault) return false;
        if (array.Length != other.array.Length) return false;
        for (var i = 0; i < array.Length; i++)
        {
            if (!array[i].Equals(other.array[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (array.IsDefault) return 0;
        var hash = 17;
        foreach (var item in array)
        {
            hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
        }
        return hash;
    }

    public IEnumerator<T> GetEnumerator() =>
        (array.IsDefault ? Enumerable.Empty<T>() : array).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Equatable, compilation-independent representation of a diagnostic source location,
/// so diagnostics can be carried through the incremental cache and re-materialized later.
/// </summary>
sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(Location location)
    {
        if (location.Kind != LocationKind.SourceFile || location.SourceTree is null)
        {
            return null;
        }
        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }
}

/// <summary>Equatable diagnostic descriptor + args that can be cached and reported at output time.</summary>
sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs) : IEquatable<DiagnosticInfo>
{
    public Diagnostic ToDiagnostic()
    {
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        return Diagnostic.Create(Descriptor, location, MessageArgs.Cast<object?>().ToArray());
    }

    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params string[] messageArgs) =>
        new(descriptor,
            location is null ? null : LocationInfo.CreateFrom(location),
            new EquatableArray<string>(messageArgs.ToImmutableArray()));
}

/// <summary>Equatable member info used to emit the formatter for a single serializable member.</summary>
sealed record MRubyMemberModel(
    string Name,
    string FullTypeName,
    string KeyName,
    string DefaultValueExpr,
    bool IsConstructorParameter) : IEquatable<MRubyMemberModel>;

/// <summary>
/// Fully value-equatable model for a single <c>[MRubyObject]</c> type. Carries everything the
/// emitter needs, so <see cref="System.Collections.Generic.IEqualityComparer{T}"/>-based caching
/// in the incremental pipeline can skip code generation when nothing relevant changed.
/// </summary>
sealed record MRubyObjectModel(
    string HintName,
    string TypeName,
    string FullTypeName,
    string? Namespace,
    string TypeDeclarationKeyword,
    bool IsValueType,
    bool HasSelectedConstructor,
    EquatableArray<string> ConstructorParameterNames,
    EquatableArray<MRubyMemberModel> Members,
    EquatableArray<DiagnosticInfo> Diagnostics,
    bool HasError,
    // Registration statements for member formatter instantiations (collections, enums, ...)
    // emitted into __RegisterMRubyValueFormatter so AOT builds never hit MakeGenericType.
    EquatableArray<string> EagerRegistrations,
    // Whether the assembly-level generated module initializer can call this type's
    // __RegisterMRubyValueFormatter directly (non-generic and accessible in the assembly).
    bool EmitInInitializer) : IEquatable<MRubyObjectModel>
{
    public static MRubyObjectModel Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        try
        {
            return CreateCore(context, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorOnly(DiagnosticInfo.Create(
                DiagnosticDescriptors.UnexpectedErrorDescriptor, null, ex.ToString()));
        }
    }

    static MRubyObjectModel ErrorOnly(params DiagnosticInfo[] diagnostics) =>
        new(string.Empty, string.Empty, string.Empty, null, "class", false, false,
            EquatableArray<string>.Empty,
            EquatableArray<MRubyMemberModel>.Empty,
            new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()),
            HasError: true,
            EagerRegistrations: EquatableArray<string>.Empty,
            EmitInInitializer: false);

    static MRubyObjectModel CreateCore(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var references = ReferenceSymbols.Create(context.SemanticModel.Compilation);
        if (references is null)
        {
            return ErrorOnly();
        }

        var typeMeta = new MRubyObjectTypeMeta(
            (TypeDeclarationSyntax)context.TargetNode,
            (INamedTypeSymbol)context.TargetSymbol,
            context.Attributes.First(),
            references);

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // --- structural validation (mirrors original TryEmitMRubyObjectType) ---
        var structuralError = false;
        if (!typeMeta.IsPartial())
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.MustBePartial,
                typeMeta.Syntax.Identifier.GetLocation(),
                typeMeta.Symbol.Name));
            structuralError = true;
        }
        if (typeMeta.IsNested())
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.NestedNotAllow,
                typeMeta.Syntax.Identifier.GetLocation(),
                typeMeta.Symbol.Name));
            structuralError = true;
        }
        if (typeMeta.Symbol.IsAbstract)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.AbstractNotAllow,
                typeMeta.Syntax.Identifier.GetLocation(),
                typeMeta.TypeName));
            structuralError = true;
        }

        if (structuralError)
        {
            return ErrorOnly(diagnostics.ToArray());
        }

        cancellationToken.ThrowIfCancellationRequested();

        // --- constructor selection (mirrors TryGetConstructor) ---
        if (!TryGetConstructor(typeMeta, references, diagnostics,
                out var hasSelectedConstructor, out var constructedMembers))
        {
            return ErrorOnly(diagnostics.ToArray());
        }

        // --- setter member validation (mirrors TryEmitDeserializeMethod) ---
        var constructedSet = new HashSet<ISymbol>(constructedMembers.Select(x => x.Symbol), SymbolEqualityComparer.Default);
        foreach (var member in typeMeta.MemberMetas)
        {
            if (constructedSet.Contains(member.Symbol)) continue;
            switch (member)
            {
                case { IsProperty: true, IsSettable: false }:
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.MRubyObjectPropertyMustHaveSetter,
                        member.GetLocation(typeMeta.Syntax),
                        typeMeta.TypeName, member.Name));
                    return ErrorOnly(diagnostics.ToArray());
                case { IsField: true, IsSettable: false }:
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.MRubyObjectFieldCannotBeReadonly,
                        member.GetLocation(typeMeta.Syntax),
                        typeMeta.TypeName, member.Name));
                    return ErrorOnly(diagnostics.ToArray());
            }
        }

        // --- build the equatable member models (order preserved) ---
        var memberModels = ImmutableArray.CreateBuilder<MRubyMemberModel>(typeMeta.MemberMetas.Count);
        foreach (var member in typeMeta.MemberMetas)
        {
            memberModels.Add(new MRubyMemberModel(
                member.Name,
                member.FullTypeName,
                member.KeyName,
                member.EmitDefaultValue(),
                IsConstructorParameter: constructedSet.Contains(member.Symbol)));
        }

        var typeDeclarationKeyword = (typeMeta.Symbol.IsRecord, typeMeta.Symbol.IsValueType) switch
        {
            (true, true) => "record struct",
            (true, false) => "record",
            (false, true) => "struct",
            (false, false) => "class",
        };

        var eagerRegistrations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var member in typeMeta.MemberMetas)
        {
            BuiltinFormatterWalker.Collect(member.MemberType, references, eagerRegistrations);
        }
        // The registration for the type itself is emitted unconditionally; a statement produced
        // for a self-typed member would just be a duplicate of it.
        eagerRegistrations.Remove($"{typeMeta.FullTypeName}.__RegisterMRubyValueFormatter();");

        var compilation = context.SemanticModel.Compilation;
        var emitInInitializer = typeMeta.Symbol is { IsGenericType: false } symbol &&
                                compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);

        var ns = typeMeta.Symbol.ContainingNamespace;
        var hintName = typeMeta.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_");

        return new MRubyObjectModel(
            HintName: hintName,
            TypeName: typeMeta.TypeName,
            FullTypeName: typeMeta.FullTypeName,
            Namespace: ns.IsGlobalNamespace ? null : ns.ToDisplayString(),
            TypeDeclarationKeyword: typeDeclarationKeyword,
            IsValueType: typeMeta.Symbol.IsValueType,
            HasSelectedConstructor: hasSelectedConstructor,
            ConstructorParameterNames: new EquatableArray<string>(constructedMembers.Select(x => x.Name).ToImmutableArray()),
            Members: new EquatableArray<MRubyMemberModel>(memberModels.ToImmutable()),
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()),
            HasError: false,
            EagerRegistrations: new EquatableArray<string>(eagerRegistrations.ToImmutableArray()),
            EmitInInitializer: emitInInitializer);
    }

    static bool TryGetConstructor(
        MRubyObjectTypeMeta typeMeta,
        ReferenceSymbols reference,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out bool hasSelectedConstructor,
        out IReadOnlyList<MRubyObjectMemberMeta> constructedMembers)
    {
        IMethodSymbol? selectedConstructor;
        if (typeMeta.Constructors.Count <= 0)
        {
            hasSelectedConstructor = false;
            constructedMembers = [];
            return true;
        }

        if (typeMeta.Constructors.Count == 1)
        {
            selectedConstructor = typeMeta.Constructors[0];
        }
        else
        {
            var ctorWithAttrs = typeMeta.Constructors
                .Where(x => x.ContainsAttribute(reference.MRubyConstructorAttribute))
                .ToArray();

            switch (ctorWithAttrs.Length)
            {
                case 1:
                    selectedConstructor = ctorWithAttrs[0];
                    break;
                case > 1:
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.MultipleConstructorAttribute,
                        typeMeta.Syntax.Identifier.GetLocation(),
                        typeMeta.Symbol.Name));
                    hasSelectedConstructor = false;
                    constructedMembers = [];
                    return false;
                default:
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.MultipleConstructorWithoutAttribute,
                        typeMeta.Syntax.Identifier.GetLocation(),
                        typeMeta.Symbol.Name));
                    hasSelectedConstructor = false;
                    constructedMembers = [];
                    return false;
            }
        }

        var parameterMembers = new List<MRubyObjectMemberMeta>();
        var error = false;
        foreach (var parameter in selectedConstructor.Parameters)
        {
            var matchedMember = typeMeta.MemberMetas
                .FirstOrDefault(member => parameter.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase));
            if (matchedMember != null)
            {
                matchedMember.IsConstructorParameter = true;
                if (parameter.HasExplicitDefaultValue)
                {
                    matchedMember.HasExplicitDefaultValueFromConstructor = true;
                    matchedMember.ExplicitDefaultValueFromConstructor = parameter.ExplicitDefaultValue;
                }
                parameterMembers.Add(matchedMember);
            }
            else
            {
                var location = selectedConstructor.Locations.FirstOrDefault() ??
                               typeMeta.Syntax.Identifier.GetLocation();
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.ConstructorHasNoMatchedParameter,
                    location,
                    typeMeta.Symbol.Name, parameter.Name));
                error = true;
            }
        }

        hasSelectedConstructor = true;
        constructedMembers = parameterMembers;
        return !error;
    }
}
