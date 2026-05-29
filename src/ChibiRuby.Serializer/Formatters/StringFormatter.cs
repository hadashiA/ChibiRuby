using System;

namespace ChibiRuby.Serializer;

public class NullableStringFormatter : IMRubyValueFormatter<string?>
{
    public static readonly NullableStringFormatter Instance = new();
    public MRubyValue Serialize(string? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null)
        {
            return default;
        }
        return state.NewString($"{value}");
    }

    public string? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil)
        {
            return null;
        }
        return state.Stringify(value).ToString();
    }
}

public class ByteArrayFormatter : IMRubyValueFormatter<byte[]?>
{
    public static readonly ByteArrayFormatter Instance = new();

    public MRubyValue Serialize(byte[]? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        return state.NewString(value.AsSpan());
    }

    public byte[]? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.String);
        return value.As<RString>().AsSpan().ToArray();
    }
}
