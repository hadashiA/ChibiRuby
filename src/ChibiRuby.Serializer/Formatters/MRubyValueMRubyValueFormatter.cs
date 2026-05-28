namespace ChibiRuby.Serializer;

public class MRubyValueMRubyValueFormatter : IMRubyValueFormatter<MRubyValue>
{
    public static readonly MRubyValueMRubyValueFormatter Instance = new();

    public MRubyValue Serialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public MRubyValue Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return value;
    }
}
