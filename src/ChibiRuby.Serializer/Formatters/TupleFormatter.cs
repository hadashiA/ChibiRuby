using System;
using System.Collections.Generic;

namespace ChibiRuby.Serializer;

class KeyValuePairFormatter<TKey, TValue> : IMRubyValueFormatter<KeyValuePair<TKey, TValue>>
{
    public MRubyValue Serialize(KeyValuePair<TKey, TValue> value, MRubyState state, MRubyValueSerializerOptions options)
    {
        var array = state.NewArray(2);
        array.Push(options.Resolver.GetFormatterWithVerify<TKey>().Serialize(value.Key, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<TValue>().Serialize(value.Value, state, options));
        return array;
    }

    public KeyValuePair<TKey, TValue> Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return default;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "KeyValuePair<,>", state);
        var array = value.As<RArray>();
        if (array.Length < 2)
        {
            throw new MRubySerializationException($"Array must have at least 2 elements for KeyValuePair<,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var key = options.Resolver.GetFormatterWithVerify<TKey>()
            .Deserialize(item1Value, state, options);
        var valueResult = options.Resolver.GetFormatterWithVerify<TValue>()
            .Deserialize(item2Value, state, options);
        return new KeyValuePair<TKey, TValue>(key, valueResult);
    }
}

class TupleFormatter<T1> : IMRubyValueFormatter<Tuple<T1>?>
{
    public MRubyValue Serialize(Tuple<T1>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(1);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        return array;
    }

    public Tuple<T1>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Tuple<>", state);
        var array = value.As<RArray>();
        if (array.Length < 1)
        {
            throw new MRubySerializationException($"Array must have at least 1 element for Tuple<>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        return new Tuple<T1>(item1!);
    }
}

class TupleFormatter<T1, T2> : IMRubyValueFormatter<Tuple<T1, T2>?>
{
    public MRubyValue Serialize(Tuple<T1, T2>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(2);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        return array;
    }

    public Tuple<T1, T2>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Tuple<,>", state);
        var array = value.As<RArray>();
        if (array.Length < 2)
        {
            throw new MRubySerializationException($"Array must have at least 2 elements for Tuple<,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        return new Tuple<T1, T2>(item1, item2);
    }
}

class TupleFormatter<T1, T2, T3> : IMRubyValueFormatter<Tuple<T1, T2, T3>?>
{
    public MRubyValue Serialize(Tuple<T1, T2, T3>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(3);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T3>().Serialize(value.Item3, state, options));
        return array;
    }

    public Tuple<T1, T2, T3>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Tuple<,,>", state);
        var array = value.As<RArray>();
        if (array.Length < 3)
        {
            throw new MRubySerializationException($"Array must have at least 3 elements for Tuple<,,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item3Value = state.Send(value, Names.OpAref, [2]);

        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        var item3 = options.Resolver.GetFormatterWithVerify<T3>()
            .Deserialize(item3Value, state, options);
        return new Tuple<T1, T2, T3>(item1, item2, item3);
    }
}

class TupleFormatter<T1, T2, T3, T4> : IMRubyValueFormatter<Tuple<T1, T2, T3, T4>?>
{
    public MRubyValue Serialize(Tuple<T1, T2, T3, T4>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(4);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T3>().Serialize(value.Item3, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T4>().Serialize(value.Item4, state, options));
        return array;
    }

    public Tuple<T1, T2, T3, T4>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Tuple<,,,>", state);
        var array = value.As<RArray>();
        if (array.Length < 4)
        {
            throw new MRubySerializationException($"Array must have at least 4 elements for Tuple<,,,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item3Value = state.Send(value, Names.OpAref, [2]);
        var item4Value = state.Send(value, Names.OpAref, [3]);

        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        var item3 = options.Resolver.GetFormatterWithVerify<T3>()
            .Deserialize(item3Value, state, options);
        var item4 = options.Resolver.GetFormatterWithVerify<T4>()
            .Deserialize(item4Value, state, options);
        return new Tuple<T1, T2, T3, T4>(item1, item2, item3, item4);
    }
}

class TupleFormatter<T1, T2, T3, T4, T5> : IMRubyValueFormatter<Tuple<T1, T2, T3, T4, T5>?>
{
    public MRubyValue Serialize(Tuple<T1, T2, T3, T4, T5>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(5);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T3>().Serialize(value.Item3, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T4>().Serialize(value.Item4, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T5>().Serialize(value.Item5, state, options));
        return array;
    }

    public Tuple<T1, T2, T3, T4, T5>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Tuple<,,,,>", state);
        var array = value.As<RArray>();
        if (array.Length < 5)
        {
            throw new MRubySerializationException($"Array must have at least 5 elements for Tuple<,,,,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item3Value = state.Send(value, Names.OpAref, [2]);
        var item4Value = state.Send(value, Names.OpAref, [3]);
        var item5Value = state.Send(value, Names.OpAref, [4]);

        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        var item3 = options.Resolver.GetFormatterWithVerify<T3>()
            .Deserialize(item3Value, state, options);
        var item4 = options.Resolver.GetFormatterWithVerify<T4>()
            .Deserialize(item4Value, state, options);
        var item5 = options.Resolver.GetFormatterWithVerify<T5>()
            .Deserialize(item5Value, state, options);
        return new Tuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
    }
}

class ValueTupleFormatter<T1> : IMRubyValueFormatter<ValueTuple<T1>>
{
    public MRubyValue Serialize(ValueTuple<T1> value, MRubyState state, MRubyValueSerializerOptions options)
    {
        var array = state.NewArray(1);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        return array;
    }

    public ValueTuple<T1> Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return default;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ValueTuple<>", state);
        var array = value.As<RArray>();
        if (array.Length < 1)
        {
            throw new MRubySerializationException($"Array must have at least 1 element for ValueTuple<>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        return new ValueTuple<T1>(item1!);
    }
}

class ValueTupleFormatter<T1, T2> : IMRubyValueFormatter<ValueTuple<T1, T2>>
{
    public MRubyValue Serialize(ValueTuple<T1, T2> value, MRubyState state, MRubyValueSerializerOptions options)
    {
        var array = state.NewArray(2);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        return array;
    }

    public ValueTuple<T1, T2> Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return default;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ValueTuple<,>", state);
        var array = value.As<RArray>();
        if (array.Length < 2)
        {
            throw new MRubySerializationException($"Array must have at least 2 elements for ValueTuple<,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        return new ValueTuple<T1, T2>(item1, item2);
    }
}

class ValueTupleFormatter<T1, T2, T3> : IMRubyValueFormatter<ValueTuple<T1, T2, T3>>
{
    public MRubyValue Serialize(ValueTuple<T1, T2, T3> value, MRubyState state, MRubyValueSerializerOptions options)
    {
        var array = state.NewArray(3);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T3>().Serialize(value.Item3, state, options));
        return array;
    }

    public ValueTuple<T1, T2, T3> Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return default;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ValueTuple<,,>", state);
        var array = value.As<RArray>();
        if (array.Length < 3)
        {
            throw new MRubySerializationException($"Array must have at least 3 elements for ValueTuple<,,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item3Value = state.Send(value, Names.OpAref, [2]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        var item3 = options.Resolver.GetFormatterWithVerify<T3>()
            .Deserialize(item3Value, state, options);
        return new ValueTuple<T1, T2, T3>(item1, item2, item3);
    }
}

class ValueTupleFormatter<T1, T2, T3, T4> : IMRubyValueFormatter<ValueTuple<T1, T2, T3, T4>>
{
    public MRubyValue Serialize(ValueTuple<T1, T2, T3, T4> value, MRubyState state, MRubyValueSerializerOptions options)
    {
        var array = state.NewArray(4);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T3>().Serialize(value.Item3, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T4>().Serialize(value.Item4, state, options));
        return array;
    }

    public ValueTuple<T1, T2, T3, T4> Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return default;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ValueTuple<,,,>", state);
        var array = value.As<RArray>();
        if (array.Length < 4)
        {
            throw new MRubySerializationException($"Array must have at least 4 elements for ValueTuple<,,,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item3Value = state.Send(value, Names.OpAref, [2]);
        var item4Value = state.Send(value, Names.OpAref, [3]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        var item3 = options.Resolver.GetFormatterWithVerify<T3>()
            .Deserialize(item3Value, state, options);
        var item4 = options.Resolver.GetFormatterWithVerify<T4>()
            .Deserialize(item4Value, state, options);
        return new ValueTuple<T1, T2, T3, T4>(item1, item2, item3, item4);
    }
}

class ValueTupleFormatter<T1, T2, T3, T4, T5> : IMRubyValueFormatter<ValueTuple<T1, T2, T3, T4, T5>>
{
    public MRubyValue Serialize(ValueTuple<T1, T2, T3, T4, T5> value, MRubyState state, MRubyValueSerializerOptions options)
    {
        var array = state.NewArray(5);
        array.Push(options.Resolver.GetFormatterWithVerify<T1>().Serialize(value.Item1, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T2>().Serialize(value.Item2, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T3>().Serialize(value.Item3, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T4>().Serialize(value.Item4, state, options));
        array.Push(options.Resolver.GetFormatterWithVerify<T5>().Serialize(value.Item5, state, options));
        return array;
    }

    public ValueTuple<T1, T2, T3, T4, T5> Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return default;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ValueTuple<,,,,>", state);
        var array = value.As<RArray>();
        if (array.Length < 5)
        {
            throw new MRubySerializationException($"Array must have at least 5 elements for ValueTuple<,,,,>, but has {array.Length}");
        }

        var item1Value = state.Send(value, Names.OpAref, [0]);
        var item2Value = state.Send(value, Names.OpAref, [1]);
        var item3Value = state.Send(value, Names.OpAref, [2]);
        var item4Value = state.Send(value, Names.OpAref, [3]);
        var item5Value = state.Send(value, Names.OpAref, [4]);
        var item1 = options.Resolver.GetFormatterWithVerify<T1>()
            .Deserialize(item1Value, state, options);
        var item2 = options.Resolver.GetFormatterWithVerify<T2>()
            .Deserialize(item2Value, state, options);
        var item3 = options.Resolver.GetFormatterWithVerify<T3>()
            .Deserialize(item3Value, state, options);
        var item4 = options.Resolver.GetFormatterWithVerify<T4>()
            .Deserialize(item4Value, state, options);
        var item5 = options.Resolver.GetFormatterWithVerify<T5>()
            .Deserialize(item5Value, state, options);
        return new ValueTuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
    }
}