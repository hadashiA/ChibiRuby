using System;

namespace ChibiRuby.Serializer;

public class UriFormatter : IMRubyValueFormatter<Uri>
{
    public static readonly UriFormatter Instance = new();

    public MRubyValue Serialize(Uri value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return mrb.NewString($"{value}");
    }

    public Uri Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.String, "Uri");
        var str = mrb.Stringify(value);

        if (Uri.TryCreate(str.ToString(), UriKind.RelativeOrAbsolute, out var uri))
        {
            return uri;
        }

        throw new MRubySerializationException($"Cannot convert `{str}` to {nameof(Uri)}");
    }
}