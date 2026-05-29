using System;

namespace ChibiRuby.Serializer;

public class VersionFormatter : IMRubyValueFormatter<Version>
{
    public static readonly VersionFormatter Instance = new();

    public MRubyValue Serialize(Version value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return mrb.NewString($"{value}");
    }

    public Version Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.String, "Guid");
        var str = mrb.Stringify(value);

        if (Version.TryParse(str.ToString(), out var version))
        {
            return version;
        }
        throw new MRubySerializationException($"Cannot convert `{str}` to {nameof(Version)}");
    }
}