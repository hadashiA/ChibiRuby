namespace ChibiRuby.Serializer;

public class SymbolFormatter : IMRubyValueFormatter<Symbol>
{
    public static readonly SymbolFormatter Instance = new();

    public MRubyValue Serialize(Symbol value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public Symbol Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Symbol, state: mrb);
        return value.SymbolValue;
    }
}