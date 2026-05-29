namespace ChibiRuby.Serializer;

public class Float32Formatter : IMRubyValueFormatter<float>
{
    public static readonly Float32Formatter Instance = new();

    public MRubyValue Serialize(float value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return new MRubyValue(value);
    }

    public float Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value.VType switch
        {
            MRubyVType.Float => (float)value.FloatValue,
            MRubyVType.Integer => value.IntegerValue,
            _ => throw new MRubySerializationException($"cannot be deserialize as float. ({value.VType})"),
        };

    }
}

public class Float64Formatter : IMRubyValueFormatter<double>
{
    public static readonly Float64Formatter Instance = new();

    public MRubyValue Serialize(double value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return new MRubyValue(value);
    }

    public double Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value.VType switch
        {
            MRubyVType.Float => value.FloatValue,
            MRubyVType.Integer => value.IntegerValue,
            _ => throw new MRubySerializationException($"cannot deserialize as double: {value.VType}")
        };
    }
}

public class DecimalFormatter : IMRubyValueFormatter<decimal>
{
    public static readonly DecimalFormatter Instance = new();

    public MRubyValue Serialize(decimal value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return new MRubyValue((double)value);
    }

    public decimal Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value.VType switch
        {
            MRubyVType.Float => (decimal)value.FloatValue,
            MRubyVType.Integer => value.IntegerValue,
            _ => throw new MRubySerializationException($"cannot deserialize as decimal: {value.VType}")
        };
    }
}
