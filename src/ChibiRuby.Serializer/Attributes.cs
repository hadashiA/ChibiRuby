using System;

namespace ChibiRuby.Serializer;

public class PreserveAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class MRubyObjectAttribute : PreserveAttribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class MRubyMemberAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class MRubyIgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Constructor)]
public class MRubyConstructorAttribute : Attribute;

/// <summary>
/// Declares a serializable root type that does not appear as a member of any [MRubyObject] type,
/// so the source generator can emit ahead-of-time-safe formatter registrations for it.
/// Use this for types passed only at call sites (e.g. <c>Deserialize&lt;List&lt;int&gt;&gt;</c>)
/// in NativeAOT / IL2CPP builds.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MRubyFormattableAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;
}
