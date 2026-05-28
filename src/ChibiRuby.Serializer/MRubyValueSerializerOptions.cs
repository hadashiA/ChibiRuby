namespace ChibiRuby.Serializer;

public class MRubyValueSerializerOptions
{
    public static MRubyValueSerializerOptions Default => new()
    {
        Resolver = StandardResolver.Instance
    };

    public IMRubyValueFormatterResolver Resolver { get; set; } = StandardResolver.Instance;

    public MRubyValueSerializerOptions Clone() => new()
    {
        Resolver = Resolver,
    };

    public MRubyValueSerializerOptions WithResolver(IMRubyValueFormatterResolver resolver)
    {
        if (Resolver == resolver)
        {
            return this;
        }
        var result = Clone();
        result.Resolver = resolver;
        return result;
    }
}
