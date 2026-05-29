namespace ChibiRuby.Serializer;

public class NullableFormatter<T> : IMRubyValueFormatter<T?> where T : struct
{
    public MRubyValue Serialize(T? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null)
        {
            return default;
        }
        return options.Resolver.GetFormatterWithVerify<T>()
            .Serialize(value.Value, state, options);
    }

    public T? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil)
        {
            return null;
        }
        return options.Resolver.GetFormatterWithVerify<T>()
            .Deserialize(value, state, options);
    }
}