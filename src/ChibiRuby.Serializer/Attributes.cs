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
