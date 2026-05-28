namespace ChibiRuby.Serializer;

public sealed class StandardResolver : IMRubyValueFormatterResolver
{
    public static readonly StandardResolver Instance = new();

    public static readonly IMRubyValueFormatterResolver[] DefaultResolvers =
    [
        BuiltinResolver.Instance,
        GeneratedResolver.Instance
    ];

    static class FormatterCache<T>
    {
        public static readonly IMRubyValueFormatter<T>? Formatter;

        static FormatterCache()
        {
            if (typeof(T) == typeof(object))
            {
                // final fallback
                Formatter = PrimitiveObjectResolver.Instance.GetFormatter<T>();
            }
            else
            {
                foreach (var item in DefaultResolvers)
                {
                    var f = item.GetFormatter<T>();
                    if (f != null)
                    {
                        Formatter = f;
                        return;
                    }
                }
            }
        }
    }

    public IMRubyValueFormatter<T>? GetFormatter<T>()
    {
        return FormatterCache<T>.Formatter;
    }
}