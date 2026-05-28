namespace ChibiRuby.Serializer;

public class BooleanFormatter : IMRubyValueFormatter<bool>
{
    public static readonly BooleanFormatter Instance = new();

    public MRubyValue Serialize(bool value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value ? MRubyValue.True : MRubyValue.False;
    }

    public bool Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value.Truthy;
    }
}
