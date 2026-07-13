using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif

namespace ChibiRuby;

// A struct (embedded directly in RObject.InstanceVariables / RClass class-ivars) so an object with
// instance variables costs ONE heap allocation, not two. ≤4 ivars live in inline fields (no backing
// array); more promote to arrays. Because it is a struct it MUST be stored in a field and mutated in
// place (`obj.InstanceVariables.Set(...)`), and passed by `ref` when a callee mutates it (see CopyTo).
public struct VariableTable : IEnumerable<KeyValuePair<Symbol, MRubyValue>>
{
    const int InlineCapacity = 4;

    Symbol key0;
    Symbol key1;
    Symbol key2;
    Symbol key3;
    MRubyValue value0;
    MRubyValue value1;
    MRubyValue value2;
    MRubyValue value3;
    Symbol[] keys = [];
    MRubyValue[] values = [];
    int count;

    // Field initializers (keys/values = []) only run via an explicit parameterless ctor; a `default`
    // VariableTable would have null arrays. Every owner creates it with `new VariableTable()`.
    public VariableTable() { }

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Defined(Symbol id)
    {
        if (keys.Length != 0)
        {
            var keysLocal = keys;
            ref var keysRef = ref GetArrayDataReference(keysLocal);
            var l = count;
            for (var i = 0; l > i; i++)
            {
                if (Unsafe.Add(ref keysRef, i) == id) return true;
            }
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (GetInlineKey(i) == id) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Symbol id, out MRubyValue value)
    {
        if (keys.Length != 0)
        {
            var keysLocal = keys;
            var valsLocal = values;
            ref var keysRef = ref GetArrayDataReference(keysLocal);
            ref var valsRef = ref GetArrayDataReference(valsLocal);
            var l = count;
            for (var i = 0; i < l; i++)
            {
                if (Unsafe.Add(ref keysRef, i) == id)
                {
                    value = Unsafe.Add(ref valsRef, i);
                    return true;
                }
            }
            value = default;
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (GetInlineKey(i) == id)
            {
                value = GetInlineValue(i);
                return true;
            }
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Get(Symbol id)
    {
        if (keys.Length != 0)
        {
            var keysLocal = keys;
            var valsLocal = values;
            ref var keysRef = ref GetArrayDataReference(keysLocal);
            ref var valsRef = ref GetArrayDataReference(valsLocal);
            var l = count;
            for (var i = 0; i < l; i++)
            {
                if (Unsafe.Add(ref keysRef, i) == id)
                {
                    return Unsafe.Add(ref valsRef, i);
                }
            }
            return default;
        }

        for (var i = 0; i < count; i++)
        {
            if (GetInlineKey(i) == id)
            {
                return GetInlineValue(i);
            }
        }
        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Symbol id, MRubyValue value)
    {
        if (keys.Length != 0)
        {
            var keysLocal = keys;
            var valsLocal = values;
            ref var keysRef = ref GetArrayDataReference(keysLocal);
            ref var valsRef = ref GetArrayDataReference(valsLocal);
            var l = count;
            for (var i = 0; i < l; i++)
            {
                if (Unsafe.Add(ref keysRef, i) == id)
                {
                    Unsafe.Add(ref valsRef, i) = value;
                    return;
                }
            }
            if (count >= keys.Length) Grow();

            Unsafe.Add(ref GetArrayDataReference(keys), count) = id;
            Unsafe.Add(ref GetArrayDataReference(values), count) = value;
            count++;
            return;
        }

        for (var i = 0; i < count; i++)
        {
            if (GetInlineKey(i) == id)
            {
                SetInlineValue(i, value);
                return;
            }
        }

        if (count < InlineCapacity)
        {
            SetInline(count, id, value);
            count++;
            return;
        }

        Grow();
        Unsafe.Add(ref GetArrayDataReference(keys), count) = id;
        Unsafe.Add(ref GetArrayDataReference(values), count) = value;
        count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(Symbol id, out MRubyValue removedValue)
    {
        if (keys.Length != 0)
        {
            var keysLocal = keys;
            var valsLocal = values;
            ref var keysRef = ref GetArrayDataReference(keysLocal);
            ref var valsRef = ref GetArrayDataReference(valsLocal);
            var l = count;
            for (var i = 0; i < l; i++)
            {
                if (Unsafe.Add(ref keysRef, i) == id)
                {
                    removedValue = Unsafe.Add(ref valsRef, i);
                    count--;
                    for (var j = i; j < count; j++)
                    {
                        Unsafe.Add(ref keysRef, j) = Unsafe.Add(ref keysRef, j + 1);
                        Unsafe.Add(ref valsRef, j) = Unsafe.Add(ref valsRef, j + 1);
                    }
                    Unsafe.Add(ref keysRef, count) = default;
                    Unsafe.Add(ref valsRef, count) = default;
                    return true;
                }
            }
            removedValue = default;
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (GetInlineKey(i) != id) continue;

            removedValue = GetInlineValue(i);
            count--;
            for (var j = i; j < count; j++)
            {
                SetInline(j, GetInlineKey(j + 1), GetInlineValue(j + 1));
            }
            ClearInline(count);
            return true;
        }
        removedValue = default;
        return false;
    }

    public void Clear()
    {
        if (count > 0)
        {
            if (keys.Length != 0)
            {
                Array.Clear(keys, 0, count);
                Array.Clear(values, 0, count);
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    ClearInline(i);
                }
            }
            count = 0;
        }
    }

    // `other` is mutated, so it must be passed by ref (a struct copy's mutations would be lost).
    public void CopyTo(ref VariableTable other)
    {
        if (count == 0) return;
        if (other.keys.Length != 0 || other.count + count > InlineCapacity)
        {
            other.EnsureArrayCapacity(other.count + count);
            for (var i = 0; i < count; i++)
            {
                other.keys[other.count + i] = GetKey(i);
                other.values[other.count + i] = GetValue(i);
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                other.SetInline(other.count + i, GetKey(i), GetValue(i));
            }
        }
        other.count += count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Grow()
    {
        if (keys.Length == 0)
        {
            PromoteInlineToArray(InlineCapacity * 2);
            return;
        }

        var newSize = keys.Length * 2;
        Array.Resize(ref keys, newSize);
        Array.Resize(ref values, newSize);
    }

    void EnsureArrayCapacity(int capacity)
    {
        if (keys.Length == 0)
        {
            PromoteInlineToArray(Math.Max(InlineCapacity * 2, capacity));
            return;
        }

        if (keys.Length < capacity)
        {
            var newSize = Math.Max(keys.Length * 2, capacity);
            Array.Resize(ref keys, newSize);
            Array.Resize(ref values, newSize);
        }
    }

    void PromoteInlineToArray(int capacity)
    {
        keys = new Symbol[capacity];
        values = new MRubyValue[capacity];
        for (var i = 0; i < count; i++)
        {
            keys[i] = GetInlineKey(i);
            values[i] = GetInlineValue(i);
        }
        for (var i = 0; i < InlineCapacity; i++)
        {
            ClearInline(i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly Symbol GetKey(int index) => keys.Length != 0 ? keys[index] : GetInlineKey(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly MRubyValue GetValue(int index) => keys.Length != 0 ? values[index] : GetInlineValue(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly Symbol GetInlineKey(int index) => index switch
    {
        0 => key0,
        1 => key1,
        2 => key2,
        3 => key3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly MRubyValue GetInlineValue(int index) => index switch
    {
        0 => value0,
        1 => value1,
        2 => value2,
        3 => value3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void SetInlineValue(int index, MRubyValue value)
    {
        switch (index)
        {
            case 0:
                value0 = value;
                break;
            case 1:
                value1 = value;
                break;
            case 2:
                value2 = value;
                break;
            case 3:
                value3 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void SetInline(int index, Symbol key, MRubyValue value)
    {
        switch (index)
        {
            case 0:
                key0 = key;
                value0 = value;
                break;
            case 1:
                key1 = key;
                value1 = value;
                break;
            case 2:
                key2 = key;
                value2 = value;
                break;
            case 3:
                key3 = key;
                value3 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ClearInline(int index)
    {
        SetInline(index, default, default);
    }

    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<KeyValuePair<Symbol, MRubyValue>> IEnumerable<KeyValuePair<Symbol, MRubyValue>>.GetEnumerator() => GetEnumerator();
    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<KeyValuePair<Symbol, MRubyValue>>
    {
        readonly VariableTable table;
        int index;

        internal Enumerator(VariableTable table)
        {
            this.table = table;
            index = -1;
        }

        public KeyValuePair<Symbol, MRubyValue> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(table.GetKey(index), table.GetValue(index));
        }

        object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return ++index < table.count;
        }

        public void Reset() => index = -1;
        public void Dispose() { }
    }
}
