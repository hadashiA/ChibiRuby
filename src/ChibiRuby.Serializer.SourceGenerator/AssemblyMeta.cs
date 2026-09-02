using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ChibiRuby.Serializer.SourceGenerator;

/// <summary>
/// Equatable, assembly-wide facts consumed by the generated module initializer:
/// whether ModuleInitializerAttribute needs a polyfill, and the registration statements
/// derived from <c>[assembly: MRubyFormattable(typeof(...))]</c> root declarations.
/// </summary>
sealed record AssemblyMeta(
    bool HasModuleInitializerAttribute,
    EquatableArray<string> RootStatements,
    EquatableArray<DiagnosticInfo> Diagnostics) : IEquatable<AssemblyMeta>
{
    public static AssemblyMeta Create(Compilation compilation, CancellationToken cancellationToken)
    {
        var hasModuleInitializer = compilation.GetTypeByMetadataName(
            "System.Runtime.CompilerServices.ModuleInitializerAttribute") is not null;

        var formattableAttribute = compilation.GetTypeByMetadataName("ChibiRuby.Serializer.MRubyFormattableAttribute");
        var mrubyObjectAttribute = compilation.GetTypeByMetadataName("ChibiRuby.Serializer.MRubyObjectAttribute");

        var statements = new SortedSet<string>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        if (formattableAttribute is not null && mrubyObjectAttribute is not null)
        {
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, formattableAttribute))
                {
                    continue;
                }
                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not ITypeSymbol rootType)
                {
                    continue;
                }
                if (rootType is INamedTypeSymbol { IsUnboundGenericType: true } ||
                    rootType.TypeKind == TypeKind.TypeParameter)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.FormattableTypeMustBeClosed,
                        attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                        rootType.ToDisplayString()));
                    continue;
                }
                BuiltinFormatterWalker.Collect(rootType, mrubyObjectAttribute, statements);
            }
        }

        return new AssemblyMeta(
            hasModuleInitializer,
            new EquatableArray<string>(statements.ToImmutableArray()),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }
}
