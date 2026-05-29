namespace ChibiRuby.Serializer;

public static partial class MRubyValueSerializer
{
    public static MRubyValue Serialize<T>(T value, MRubyState state, MRubyValueSerializerOptions? options = null)
    {
        options ??= MRubyValueSerializerOptions.Default;
        return options.Resolver.GetFormatterWithVerify<T>()
            .Serialize(value, state, options);
    }

    public static T? Deserialize<T>(MRubyValue value, MRubyState state, MRubyValueSerializerOptions? options = null)
    {
        options ??= MRubyValueSerializerOptions.Default;
        return options.Resolver.GetFormatterWithVerify<T>()
            .Deserialize(value, state, options);
    }
}
