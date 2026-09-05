#pragma warning disable RS2008

using Microsoft.CodeAnalysis;

namespace ChibiRuby.Serializer.SourceGenerator;

static class DiagnosticDescriptors
{
    const string Category = "ChibiRuby.Serializer.SourceGenerator";

    public static readonly DiagnosticDescriptor UnexpectedErrorDescriptor = new(
        id: "MRBCS001",
        title: "Unexpected error during source code generation",
        messageFormat: "Unexpected error occurred during source code code generation: {0}",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustBePartial = new(
        id: "MRBCS002",
        title: "[MRubyObject] type declaration must be partial",
        messageFormat: "The implementation of type '{0}' must be partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NestedNotAllow = new(
        id: "MRBCS003",
        title: "[MRubyObject] type must not be nested type",
        messageFormat: "The implementation of type '{0}' must not be nested type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AbstractNotAllow = new(
        id: "MRBS004",
        title: "[MRubyObject] type must not be abstract or interface",
        messageFormat: "The implementation of type '{0}' must not be abstract or interface type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MRubyObjectPropertyMustHaveSetter = new(
        id: "MRBCS005",
        title: "A mruby serializable property with must have setter",
        messageFormat: "The MRubyObject '{0}' property '{1}' must have setter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MRubyObjectFieldCannotBeReadonly = new(
        id: "MRBCS006",
        title: "A mruby serializable field cannot be readonly",
        messageFormat: "The MRubyObject '{0}' field '{1}' cannot be readonly",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor MultipleConstructorAttribute = new(
        id: "MRBCS007",
        title: "[MRubyConstructor] exists in multiple constructors",
        messageFormat: "Multiple [MRubyConstructor] exists in '{0}' but allows only single ctor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleConstructorWithoutAttribute = new(
        id: "MRBCS008",
        title: "Require [MRubyConstructor] when exists multiple constructors",
        messageFormat: "The MRubyObject '{0}' must annotate with [MRubyConstructor] when exists multiple constructors",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FormattableTypeMustBeClosed = new(
        id: "MRBCS010",
        title: "[assembly: MRubyFormattable] type must be a closed constructed type",
        messageFormat: "The MRubyFormattable type '{0}' must be a closed (fully constructed) type; open generic types cannot be registered ahead of time",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructorHasNoMatchedParameter = new(
        id: "MRBCS009",
        title: "MRubyObject's constructor has no matched parameter",
        messageFormat: "The MRubyObject '{0}' constructor's parameter '{1}' must match a serialized member name(case-insensitive)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

}
