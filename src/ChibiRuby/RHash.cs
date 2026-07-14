using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChibiRuby;

public sealed class RHash : RObject, IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>
{
    public int Length => keys.Count;
    public ReadOnlySpan<MRubyValue> Keys => CollectionsMarshal.AsSpan(keys);
    public ReadOnlySpan<MRubyValue> Values => CollectionsMarshal.AsSpan(values);

    public MRubyValue? DefaultValue { get; set; }
    public RProc? DefaultProc { get; set; }

    readonly List<MRubyValue> keys;
    readonly List<MRubyValue> values;
    Dictionary<MRubyValue, int> indexTable;

    IEqualityComparer<MRubyValue> keyComparer;
    readonly IEqualityComparer<MRubyValue> valueComparer;

    public bool ComparedByIdentity => keyComparer is MRubyValueHashKeyEqualityComparer { ByIdentity: true };

    /// <summary>
    /// Switch key semantics to identity (Hash#compare_by_identity): objects compare by
    /// reference, immediates by value, ignoring user-defined hash/eql?. Existing entries
    /// are re-bucketed; identity is strictly finer than eql?-equality so no entries merge.
    /// </summary>
    internal void CompareByIdentity(MRubyValueHashKeyEqualityComparer identityComparer)
    {
        if (ComparedByIdentity) return;
        keyComparer = identityComparer;
        indexTable = new Dictionary<MRubyValue, int>(keys.Count, keyComparer);
        for (var i = 0; i < keys.Count; i++)
        {
            indexTable[keys[i]] = i;
        }
    }

    internal RHash(
        int capacity,
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer,
        RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.keyComparer = keyComparer;
        this.valueComparer = valueComparer;
        keys = new List<MRubyValue>(capacity);
        values = new List<MRubyValue>(capacity);
        indexTable = new Dictionary<MRubyValue, int>(capacity, keyComparer);
    }

    RHash(
        List<MRubyValue> keys,
        List<MRubyValue> values,
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer,
        RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.keys = keys;
        this.values = values;
        this.keyComparer = keyComparer;
        this.valueComparer = valueComparer;
        indexTable = new Dictionary<MRubyValue, int>(keys.Count, keyComparer);
        for (var i = 0; i < keys.Count; i++)
        {
            indexTable[keys[i]] = i;
        }
    }

    public MRubyValue this[MRubyValue key]
    {
        get
        {
            if (TryGetIndexOfKey(key, out var index))
            {
                return values[index];
            }
            return MRubyValue.Nil;
        }
        set
        {
            if (TryGetIndexGuarded(key, out var index))
            {
                keys[index] = key;
                values[index] = value;
            }
            else
            {
                indexTable.Add(key, keys.Count);
                keys.Add(key);
                values.Add(value);
            }
        }
    }

    public MRubyValue GetValueOrDefault(MRubyValue key, MRubyState state)
    {
        if (TryGetIndexOfKey(key, out var index))
        {
            return values[index];
        }
        if (DefaultProc is { } proc)
        {
            return state.Send(proc, Names.Call, this, key);
        }
        return DefaultValue ?? MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MRubyValue key, MRubyValue value)
    {
        if (ContainsKey(key))
        {
            throw new InvalidOperationException("Duplicate key");
        }
        indexTable.Add(key, keys.Count);
        keys.Add(key);
        values.Add(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(MRubyValue key) => TryGetIndexGuarded(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsValue(MRubyValue value)
    {
        foreach (var t in Values)
        {
            if (valueComparer.Equals(value, t)) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(MRubyValue key, out MRubyValue value)
    {
        if (TryGetIndexOfKey(key, out var index))
        {
            value = values[index];
            return true;
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDelete(MRubyValue key, out MRubyValue value)
    {
        if (TryGetIndexOfKey(key, out var index))
        {
            value = values[index];
            indexTable.Remove(key);
            keys.RemoveAt(index);
            values.RemoveAt(index);
            for (var i = index; i < keys.Count; i++)
            {
                indexTable[keys[i]] = i;
            }
            return true;
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        keys.Clear();
        values.Clear();
        indexTable.Clear();
    }

    public void ReplaceTo(RHash other)
    {
        other.keys.Clear();
        other.values.Clear();
        other.indexTable.Clear();
        for (var i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            other.keys.Add(k);
            other.values.Add(values[i]);
            other.indexTable.Add(k, i);
        }

        other.DefaultValue = DefaultValue;
        other.DefaultProc = DefaultProc;
    }

    public bool TryShift(out MRubyValue headKey, out MRubyValue headValue)
    {
        if (keys.Count > 0)
        {
            headKey = keys[0];
            headValue = values[0];
            TryDelete(headKey, out _);
            return true;
        }
        headValue = default;
        headKey = default;
        return false;
    }

    public RHash Dup() => new(keys, values, keyComparer, valueComparer, Class);

    internal override RObject Clone()
    {
        var clone = new RHash(Length, keyComparer, valueComparer, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public void Merge(RHash other)
    {
        if (this == other) return;
        if (other.Length == 0) return;

        for (var i = 0; i < other.keys.Count; i++)
        {
            this[other.keys[i]] = other.values[i];
        }
    }

    public void Rehash()
    {
        indexTable.Clear();
        var writePos = 0;

        for (var readPos = 0; readPos < keys.Count; readPos++)
        {
            var key = keys[readPos];
            if (TryGetIndexGuarded(key, out var existingIndex))
            {
                values[existingIndex] = values[readPos];
            }
            else
            {
                if (writePos != readPos)
                {
                    keys[writePos] = keys[readPos];
                    values[writePos] = values[readPos];
                }
                indexTable[key] = writePos;
                writePos++;
            }
        }

        // A user-defined #hash may have mutated this hash (e.g. cleared it) during the
        // probes above; clamp so the trim never underflows.
        var keyTrimStart = Math.Min(writePos, keys.Count);
        var valueTrimStart = Math.Min(writePos, values.Count);
        keys.RemoveRange(keyTrimStart, keys.Count - keyTrimStart);
        values.RemoveRange(valueTrimStart, values.Count - valueTrimStart);
    }

    public struct Enumerator(RHash source) : IEnumerator<KeyValuePair<MRubyValue, MRubyValue>>
    {
        public KeyValuePair<MRubyValue, MRubyValue> Current { get; private set; }
        object IEnumerator.Current => Current;

        int index = -1;

        public bool MoveNext()
        {
            if (++index < source.keys.Count)
            {
                Current = new KeyValuePair<MRubyValue, MRubyValue>(source.keys[index], source.values[index]);
                return true;
            }
            return false;
        }

        public void Reset()
        {
            index = -1;
            Current = default;
        }

        public void Dispose()
        {
            index = -2;
            Current = default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryGetIndexOfKey(MRubyValue key, out int index)
    {
        if (TryGetIndexGuarded(key, out index))
        {
            return true;
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// indexTable probe that survives a user-defined <c>#hash</c>/<c>#eql?</c> mutating
    /// this hash mid-probe (mruby leaves the result unspecified but must not crash).
    /// The Dictionary itself stays consistent after such a mutation — only the in-flight
    /// probe's guard throws — so a single retry against the new state is sound.
    /// </summary>
    /// <summary>
    /// indexTable probe that survives a user-defined <c>#hash</c>/<c>#eql?</c> mutating
    /// this hash mid-probe (mruby raises "hash modified" there; the result is unspecified
    /// but must not crash). The Dictionary itself stays consistent after such a mutation —
    /// only the in-flight probe's guard throws — so a single retry against the new state
    /// is sound.
    /// </summary>
    bool TryGetIndexGuarded(MRubyValue key, out int index)
    {
        try
        {
            return indexTable.TryGetValue(key, out index);
        }
        catch (InvalidOperationException)
        {
            return indexTable.TryGetValue(key, out index);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<MRubyValue, MRubyValue>> IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>.GetEnumerator() =>
        GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}