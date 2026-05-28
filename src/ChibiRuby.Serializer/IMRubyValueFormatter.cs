namespace ChibiRuby.Serializer;

public interface IMRubyValueFormatter;

public interface IMRubyValueFormatter<T> : IMRubyValueFormatter
{
    MRubyValue Serialize(T value, MRubyState mrb, MRubyValueSerializerOptions options);
    T Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options);
}
