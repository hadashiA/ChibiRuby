namespace ChibiRuby.Serializer;

public class RObjectFormatter<T> : IMRubyValueFormatter<T> where T : RObject
{
    public static readonly RObjectFormatter<T> Instance = new();

    public MRubyValue Serialize(T value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return new MRubyValue(value);
    }

    public T Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return value.As<T>();
    }
}
