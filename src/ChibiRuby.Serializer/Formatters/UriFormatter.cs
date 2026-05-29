using System;

namespace ChibiRuby.Serializer;

public class GuidFormatter : IMRubyValueFormatter<Guid>
{
    public static readonly GuidFormatter Instance = new();

    public MRubyValue Serialize(Guid value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return mrb.NewString($"{value}");
    }

    public Guid Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.String, "Guid");
        var str = mrb.Stringify(value);

        if (Guid.TryParse(str.ToString(), out var guid))
        {
            return guid;
        }

        throw new MRubySerializationException($"Cannot convert `{str}` to {typeof(Guid)}");
    }
}