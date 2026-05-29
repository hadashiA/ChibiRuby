namespace ChibiRuby.Serializer;

public class ByteFormatter : IMRubyValueFormatter<byte>
{
    public static readonly ByteFormatter Instance = new();

    public MRubyValue Serialize(byte value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public byte Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((byte)value.IntegerValue);
    }
}

public class SByteFormatter : IMRubyValueFormatter<sbyte>
{
    public static readonly SByteFormatter Instance = new();

    public MRubyValue Serialize(sbyte value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public sbyte Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((sbyte)value.IntegerValue);
    }
}

public class CharFormatter : IMRubyValueFormatter<char>
{
    public static readonly CharFormatter Instance = new();

    public MRubyValue Serialize(char value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public char Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((char)value.IntegerValue);
    }
}

public class Int16Formatters : IMRubyValueFormatter<short>
{
    public static readonly Int16Formatters Instance = new();

    public MRubyValue Serialize(short value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public short Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((short)value.IntegerValue);
    }
}

public class Int32Formatters : IMRubyValueFormatter<int>
{
    public static readonly Int32Formatters Instance = new();

    public MRubyValue Serialize(int value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public int Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((int)value.IntegerValue);
    }
}

public class Int64Formatter : IMRubyValueFormatter<long>
{
    public static readonly Int64Formatter Instance = new();

    public MRubyValue Serialize(long value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public long Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return value.IntegerValue;
    }
}

public class UInt16Formatter : IMRubyValueFormatter<ushort>
{
    public static readonly UInt16Formatter Instance = new();

    public MRubyValue Serialize(ushort value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public ushort Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((ushort)value.IntegerValue);
    }
}

public class UInt32Formatter : IMRubyValueFormatter<uint>
{
    public static readonly UInt32Formatter Instance = new();

    public MRubyValue Serialize(uint value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public uint Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((uint)value.IntegerValue);
    }
}

public class UInt64Formatter : IMRubyValueFormatter<ulong>
{
    public static readonly UInt64Formatter Instance = new();

    public MRubyValue Serialize(ulong value, MRubyState state, MRubyValueSerializerOptions options)
    {
        return value;
    }

    public ulong Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
        return checked((ulong)value.IntegerValue);
    }
}