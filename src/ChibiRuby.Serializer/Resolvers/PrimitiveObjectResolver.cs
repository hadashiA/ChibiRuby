namespace ChibiRuby.Serializer;

public class PrimitiveObjectResolver : IMRubyValueFormatterResolver
{
    public static readonly PrimitiveObjectResolver Instance = new();

    static class FormatterCache<T>
    {
        public static readonly IMRubyValueFormatter<T> Formatter;

        static FormatterCache()
        {
            Formatter = (IMRubyValueFormatter<T>)PrimitiveObjectFormatter.Instance;
        }
    }

    public IMRubyValueFormatter<T> GetFormatter<T>()
    {
        return FormatterCache<T>.Formatter;
    }
}
