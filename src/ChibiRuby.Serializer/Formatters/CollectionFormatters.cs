using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ChibiRuby.Serializer;

public class ArrayFormatter<T> : IMRubyValueFormatter<T[]?>
{
    public MRubyValue Serialize(T[]? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Length);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public T[]? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array);

        var array = value.As<RArray>();
        var result = new T[array.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            var element = options.Resolver.GetFormatterWithVerify<T>()
                .Deserialize(elementValue, state, options);
            result[i] = element;
        }
        return result;
    }
}

public sealed class TwoDimensionalArrayFormatter<T> : IMRubyValueFormatter<T[,]?>
{
    public MRubyValue Serialize(T[,]? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var outerArray = state.NewArray(value.GetLength(0));
        for (var i = 0; i < value.GetLength(0); i++)
        {
            var innerArray = state.NewArray(value.GetLength(1));
            for (var j = 0; j < value.GetLength(1); j++)
            {
                var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                    .Serialize(value[i, j], state, options);
                innerArray.Push(elementValue);
            }
            outerArray.Push(innerArray);
        }
        return outerArray;
    }

    public T[,]? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "T[,]", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var outerArray = value.As<RArray>();

        if (outerArray.Length == 0) return new T[0, 0];

        var firstInner = state.Send(value, Names.OpAref, [0]);
        MRubySerializationException.ThrowIfTypeMismatch(firstInner, MRubyVType.Array, "T[,] inner", state);
        var innerLength = firstInner.As<RArray>().Length;

        var result = new T[outerArray.Length, innerLength];
        for (var i = 0; i < outerArray.Length; i++)
        {
            var innerValue = state.Send(value, Names.OpAref, [i]);
            MRubySerializationException.ThrowIfTypeMismatch(innerValue, MRubyVType.Array, "T[,] inner", state);

            for (var j = 0; j < innerLength; j++)
            {
                var elementValue = state.Send(innerValue, Names.OpAref, [j]);
                result[i, j] = formatter.Deserialize(elementValue, state, options);
            }
        }
        return result;
    }
}

public sealed class ThreeDimensionalArrayFormatter<T> : IMRubyValueFormatter<T[,,]?>
{
    public MRubyValue Serialize(T[,,]? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var outerArray = state.NewArray(value.GetLength(0));
        for (var i = 0; i < value.GetLength(0); i++)
        {
            var middleArray = state.NewArray(value.GetLength(1));
            for (var j = 0; j < value.GetLength(1); j++)
            {
                var innerArray = state.NewArray(value.GetLength(2));
                for (var k = 0; k < value.GetLength(2); k++)
                {
                    var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                        .Serialize(value[i, j, k], state, options);
                    innerArray.Push(elementValue);
                }
                middleArray.Push(innerArray);
            }
            outerArray.Push(middleArray);
        }
        return outerArray;
    }

    public T[,,]? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "T[,,]", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var outerArray = value.As<RArray>();

        if (outerArray.Length == 0) return new T[0, 0, 0];

        var firstMiddle = state.Send(value, Names.OpAref, [0]);
        MRubySerializationException.ThrowIfTypeMismatch(firstMiddle, MRubyVType.Array, "T[,,] middle", state);
        var middleArray = firstMiddle.As<RArray>();

        if (middleArray.Length == 0) return new T[outerArray.Length, 0, 0];

        var firstInner = state.Send(firstMiddle, Names.OpAref, [0]);
        MRubySerializationException.ThrowIfTypeMismatch(firstInner, MRubyVType.Array, "T[,,] inner", state);
        var innerLength = firstInner.As<RArray>().Length;

        var result = new T[outerArray.Length, middleArray.Length, innerLength];
        for (var i = 0; i < outerArray.Length; i++)
        {
            var middleValue = state.Send(value, Names.OpAref, [i]);
            for (var j = 0; j < middleArray.Length; j++)
            {
                var innerValue = state.Send(middleValue, Names.OpAref, [j]);
                for (var k = 0; k < innerLength; k++)
                {
                    var elementValue = state.Send(innerValue, Names.OpAref, [k]);
                    result[i, j, k] = formatter.Deserialize(elementValue, state, options);
                }
            }
        }
        return result;
    }
}

public sealed class FourDimensionalArrayFormatter<T> : IMRubyValueFormatter<T[,,,]?>
{
    public MRubyValue Serialize(T[,,,]? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var outerArray = state.NewArray(value.GetLength(0));
        for (var i = 0; i < value.GetLength(0); i++)
        {
            var array1 = state.NewArray(value.GetLength(1));
            for (var j = 0; j < value.GetLength(1); j++)
            {
                var array2 = state.NewArray(value.GetLength(2));
                for (var k = 0; k < value.GetLength(2); k++)
                {
                    var array3 = state.NewArray(value.GetLength(3));
                    for (var l = 0; l < value.GetLength(3); l++)
                    {
                        var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                            .Serialize(value[i, j, k, l], state, options);
                        array3.Push(elementValue);
                    }
                    array2.Push(array3);
                }
                array1.Push(array2);
            }
            outerArray.Push(array1);
        }
        return outerArray;
    }

    public T[,,,]? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "T[,,,]", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var outerArray = value.As<RArray>();

        if (outerArray.Length == 0) return new T[0, 0, 0, 0];

        var first1 = state.Send(value, Names.OpAref, [0]);
        MRubySerializationException.ThrowIfTypeMismatch(first1, MRubyVType.Array, "T[,,,] dim1", state);
        var array1 = first1.As<RArray>();

        if (array1.Length == 0) return new T[outerArray.Length, 0, 0, 0];

        var first2 = state.Send(first1, Names.OpAref, [0]);
        MRubySerializationException.ThrowIfTypeMismatch(first2, MRubyVType.Array, "T[,,,] dim2", state);
        var array2 = first2.As<RArray>();

        if (array2.Length == 0) return new T[outerArray.Length, array1.Length, 0, 0];

        var first3 = state.Send(first2, Names.OpAref, [0]);
        MRubySerializationException.ThrowIfTypeMismatch(first3, MRubyVType.Array, "T[,,,] dim3", state);
        var array3 = first3.As<RArray>();

        var result = new T[outerArray.Length, array1.Length, array2.Length, array3.Length];
        for (var i = 0; i < outerArray.Length; i++)
        {
            var value1 = state.Send(value, Names.OpAref, [i]);
            for (var j = 0; j < array1.Length; j++)
            {
                var value2 = state.Send(value1, Names.OpAref, [j]);
                for (var k = 0; k < array2.Length; k++)
                {
                    var value3 = state.Send(value2, Names.OpAref, [k]);
                    for (var l = 0; l < array3.Length; l++)
                    {
                        var elementValue = state.Send(value3, Names.OpAref, [l]);
                        result[i, j, k, l] = formatter.Deserialize(elementValue, state, options);
                    }
                }
            }
        }
        return result;
    }
}

public class ListFormatter<T> : IMRubyValueFormatter<List<T>?>
{
    public MRubyValue Serialize(List<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public List<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "List<>", state);

        var array = value.As<RArray>();
        var result = new List<T>(array.Length);
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            var element = options.Resolver.GetFormatterWithVerify<T>()
                .Deserialize(elementValue, state, options);
            result.Add(element);
        }
        return result;
    }
}

public class DictionaryFormatter<TKey, TValue> : IMRubyValueFormatter<Dictionary<TKey, TValue>?> where TKey : notnull
{
    public MRubyValue Serialize(Dictionary<TKey, TValue>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var hash = state.NewHash(value.Count);
        foreach (var x in value)
        {
            var k = options.Resolver.GetFormatterWithVerify<TKey>()
                .Serialize(x.Key, state, options);
            var v = options.Resolver.GetFormatterWithVerify<TValue>()
                .Serialize(x.Value, state, options);
            hash.Add(k, v);
        }
        return hash;
    }

    public Dictionary<TKey, TValue>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Hash);

        var hash = value.As<RHash>();
        var dict = new Dictionary<TKey, TValue?>(hash.Length);
        foreach (var x in hash)
        {
            var k = options.Resolver.GetFormatterWithVerify<TKey>()
                .Deserialize(x.Key, state, options);
            var v = options.Resolver.GetFormatterWithVerify<TValue>()
                .Deserialize(x.Value, state, options);
            dict.Add(k, v);
        }
        return dict!;
    }
}

class SortedDictionaryFormatter<TKey, TValue> : IMRubyValueFormatter<SortedDictionary<TKey, TValue>?> where TKey : notnull
{
    public MRubyValue Serialize(SortedDictionary<TKey, TValue>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var hash = state.NewHash(value.Count);
        foreach (var x in value)
        {
            var k = options.Resolver.GetFormatterWithVerify<TKey>()
                .Serialize(x.Key, state, options);
            var v = options.Resolver.GetFormatterWithVerify<TValue>()
                .Serialize(x.Value, state, options);
            hash.Add(k, v);
        }
        return hash;
    }

    public SortedDictionary<TKey, TValue>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Hash, "SortedDictionary<>", state);

        var hash = value.As<RHash>();
        var dict = new SortedDictionary<TKey, TValue?>();
        foreach (var x in hash)
        {
            var key = options.Resolver.GetFormatterWithVerify<TKey>()
                .Deserialize(x.Key, state, options);
            var val = options.Resolver.GetFormatterWithVerify<TValue>()
                .Deserialize(x.Value, state, options);
            dict.Add(key, val);
        }
        return dict!;
    }
}

class ConcurrentDictionaryFormatter<TKey, TValue> : IMRubyValueFormatter<ConcurrentDictionary<TKey, TValue>?> where TKey : notnull
{
    public MRubyValue Serialize(ConcurrentDictionary<TKey, TValue>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var hash = state.NewHash(value.Count);
        foreach (var x in value)
        {
            var k = options.Resolver.GetFormatterWithVerify<TKey>()
                .Serialize(x.Key, state, options);
            var v = options.Resolver.GetFormatterWithVerify<TValue>()
                .Serialize(x.Value, state, options);
            hash.Add(k, v);
        }
        return hash;
    }

    public ConcurrentDictionary<TKey, TValue>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Hash, "ConcurrentDictionary<>", state);

        var hash = value.As<RHash>();
        var dict = new ConcurrentDictionary<TKey, TValue?>();
        foreach (var x in hash)
        {
            var key = options.Resolver.GetFormatterWithVerify<TKey>()
                .Deserialize(x.Key, state, options);
            var val = options.Resolver.GetFormatterWithVerify<TValue>()
                .Deserialize(x.Value, state, options);
            dict.TryAdd(key, val);
        }
        return dict!;
    }
}

public class InterfaceDictionaryFormatter<TKey, TValue> : IMRubyValueFormatter<IDictionary<TKey, TValue>?> where TKey : notnull
{
    public MRubyValue Serialize(IDictionary<TKey, TValue>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var hash = state.NewHash(value.Count);
        foreach (var x in value)
        {
            var k = options.Resolver.GetFormatterWithVerify<TKey>()
                .Serialize(x.Key, state, options);
            var v = options.Resolver.GetFormatterWithVerify<TValue>()
                .Serialize(x.Value, state, options);
            hash.Add(k, v);
        }
        return hash;
    }

    public IDictionary<TKey, TValue>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Hash, "IDictionary<>", state);

        var hash = value.As<RHash>();
        var dict = new Dictionary<TKey, TValue?>(hash.Length);
        foreach (var x in hash)
        {
            var key = options.Resolver.GetFormatterWithVerify<TKey>()
                .Deserialize(x.Key, state, options);
            var val = options.Resolver.GetFormatterWithVerify<TValue>()
                .Deserialize(x.Value, state, options);
            dict.Add(key, val);
        }
        return dict!;
    }
}

public class InterfaceReadOnlyDictionaryFormatter<TKey, TValue> : IMRubyValueFormatter<IReadOnlyDictionary<TKey, TValue>?> where TKey : notnull
{
    public MRubyValue Serialize(IReadOnlyDictionary<TKey, TValue>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var hash = state.NewHash(value.Count);
        foreach (var x in value)
        {
            var k = options.Resolver.GetFormatterWithVerify<TKey>()
                .Serialize(x.Key, state, options);
            var v = options.Resolver.GetFormatterWithVerify<TValue>()
                .Serialize(x.Value, state, options);
            hash.Add(k, v);
        }
        return hash;
    }

    public IReadOnlyDictionary<TKey, TValue>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Hash, "IReadOnlyDictionary<>", state);

        var hash = value.As<RHash>();
        var dict = new Dictionary<TKey, TValue?>(hash.Length);
        foreach (var x in hash)
        {
            var key = options.Resolver.GetFormatterWithVerify<TKey>()
                .Deserialize(x.Key, state, options);
            var val = options.Resolver.GetFormatterWithVerify<TValue>()
                .Deserialize(x.Value, state, options);
            dict.Add(key, val);
        }
        return dict!;
    }
}

class InterfaceEnumerableFormatter<T> : IMRubyValueFormatter<IEnumerable<T>?>
{
    public MRubyValue Serialize(IEnumerable<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var list = value as IList<T> ?? new List<T>(value);
        var array = state.NewArray(list.Count);
        foreach (var x in list)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public IEnumerable<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "IEnumerable<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new T[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result[i] = formatter.Deserialize(elementValue, state, options);
        }
        return result;
    }
}

class InterfaceCollectionFormatter<T> : IMRubyValueFormatter<ICollection<T>?>
{
    public MRubyValue Serialize(ICollection<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public ICollection<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ICollection<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new T[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result[i] = formatter.Deserialize(elementValue, state, options);
        }
        return result;
    }
}

class InterfaceReadOnlyCollectionFormatter<T> : IMRubyValueFormatter<IReadOnlyCollection<T>?>
{
    public MRubyValue Serialize(IReadOnlyCollection<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var list = value as IList<T> ?? new List<T>(value);
        var array = state.NewArray(list.Count);
        foreach (var x in list)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public IReadOnlyCollection<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "IReadOnlyCollection<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new T[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result[i] = formatter.Deserialize(elementValue, state, options);
        }
        return result;
    }
}

class InterfaceListFormatter<T> : IMRubyValueFormatter<IList<T>?>
{
    public MRubyValue Serialize(IList<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public IList<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "IList<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new T[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result[i] = formatter.Deserialize(elementValue, state, options);
        }
        return result;
    }
}

class InterfaceReadOnlyListFormatter<T> : IMRubyValueFormatter<IReadOnlyList<T>?>
{
    public MRubyValue Serialize(IReadOnlyList<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public IReadOnlyList<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "IReadOnlyList<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new T[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result[i] = formatter.Deserialize(elementValue, state, options);
        }
        return result;
    }
}

class HashSetFormatter<T> : IMRubyValueFormatter<HashSet<T>?>
{
    public MRubyValue Serialize(HashSet<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public HashSet<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "HashSet<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new HashSet<T>();
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Add(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class SortedSetFormatter<T> : IMRubyValueFormatter<SortedSet<T>?>
{
    public MRubyValue Serialize(SortedSet<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public SortedSet<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "SortedSet<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new SortedSet<T>();
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Add(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class InterfaceSetFormatter<T> : IMRubyValueFormatter<ISet<T>?>
{
    public MRubyValue Serialize(ISet<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public ISet<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ISet<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new HashSet<T>();
        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Add(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class StackFormatter<T> : IMRubyValueFormatter<Stack<T>?>
{
    public MRubyValue Serialize(Stack<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        // Stack enumerates in LIFO order, we need to reverse it
        var items = value.ToArray();
        Array.Reverse(items);
        foreach (var x in items)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public Stack<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Stack<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var list = new List<T>(array.Length);

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            list.Add(formatter.Deserialize(elementValue, state, options));
        }

        var stack = new Stack<T>(array.Length);
        for (var i = list.Count - 1; i >= 0; i--)
        {
            stack.Push(list[i]);
        }
        return stack;
    }
}

class QueueFormatter<T> : IMRubyValueFormatter<Queue<T>?>
{
    public MRubyValue Serialize(Queue<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public Queue<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Queue<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var queue = new Queue<T>(array.Length);

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            queue.Enqueue(formatter.Deserialize(elementValue, state, options));
        }
        return queue;
    }
}

class LinkedListFormatter<T> : IMRubyValueFormatter<LinkedList<T>?>
{
    public MRubyValue Serialize(LinkedList<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public LinkedList<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "LinkedList<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new LinkedList<T>();

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.AddLast(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class CollectionFormatter<T> : IMRubyValueFormatter<Collection<T>?>
{
    public MRubyValue Serialize(Collection<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public Collection<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "Collection<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new Collection<T>();

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Add(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class ReadOnlyCollectionFormatter<T> : IMRubyValueFormatter<ReadOnlyCollection<T>?>
{
    public MRubyValue Serialize(ReadOnlyCollection<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public ReadOnlyCollection<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ReadOnlyCollection<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var list = new List<T>(array.Length);

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            list.Add(formatter.Deserialize(elementValue, state, options));
        }
        return new ReadOnlyCollection<T>(list);
    }
}

class BlockingCollectionFormatter<T> : IMRubyValueFormatter<BlockingCollection<T>?>
{
    public MRubyValue Serialize(BlockingCollection<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public BlockingCollection<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "BlockingCollection<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new BlockingCollection<T>();

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Add(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class ConcurrentQueueFormatter<T> : IMRubyValueFormatter<ConcurrentQueue<T>?>
{
    public MRubyValue Serialize(ConcurrentQueue<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public ConcurrentQueue<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ConcurrentQueue<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new ConcurrentQueue<T>();

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Enqueue(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}

class ConcurrentStackFormatter<T> : IMRubyValueFormatter<ConcurrentStack<T>?>
{
    public MRubyValue Serialize(ConcurrentStack<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        // ConcurrentStack enumerates in LIFO order, we need to reverse it
        var items = value.ToArray();
        Array.Reverse(items);

        var array = state.NewArray(items.Length);
        foreach (var x in items)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public ConcurrentStack<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ConcurrentStack<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var list = new List<T>(array.Length);

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            list.Add(formatter.Deserialize(elementValue, state, options));
        }

        var stack = new ConcurrentStack<T>();
        for (var i = list.Count - 1; i >= 0; i--)
        {
            stack.Push(list[i]);
        }
        return stack;
    }
}

class ConcurrentBagFormatter<T> : IMRubyValueFormatter<ConcurrentBag<T>?>
{
    public MRubyValue Serialize(ConcurrentBag<T>? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var array = state.NewArray(value.Count);
        foreach (var x in value)
        {
            var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(x, state, options);
            array.Push(elementValue);
        }
        return array;
    }

    public ConcurrentBag<T>? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array, "ConcurrentBag<>", state);

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var array = value.As<RArray>();
        var result = new ConcurrentBag<T>();

        for (var i = 0; i < array.Length; i++)
        {
            var elementValue = state.Send(value, Names.OpAref, [i]);
            result.Add(formatter.Deserialize(elementValue, state, options));
        }
        return result;
    }
}