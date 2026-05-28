using System;
using System.Diagnostics;

namespace ChibiRuby;

// These attributes are read only by the ChibiRuby.SourceGenerator at compile time
// to emit .rbs signature files. [Conditional] strips them from emitted metadata
// (unless MRUBYCS_RBS_KEEP is defined), so they cost nothing in the shipped dll.

[Conditional("MRUBYCS_RBS_KEEP")]
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RubyDefAttribute : Attribute
{
    public RubyDefAttribute() { }
    public RubyDefAttribute(string signature) { Signature = signature; }

    public string? Signature;
}

[Conditional("MRUBYCS_RBS_KEEP")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RubyClassAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string Superclass { get; set; } = "Object";
    /// <summary>
    /// Comma-separated RBS type parameter list (e.g., "Elem" or "K, V").
    /// Emitted into the class header as `class Foo[Elem] < ...`.
    /// </summary>
    public string? TypeParameters { get; set; }
}

[Conditional("MRUBYCS_RBS_KEEP")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RubyModuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    /// <summary>
    /// Comma-separated RBS type parameter list (e.g., "Elem").
    /// </summary>
    public string? TypeParameters { get; set; }
}
