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

public class VariableTable : IEnumerable<KeyValuePair<Symbol, MRubyValue>>
{
    Symbol[] keys = [];
    MRubyValue[] values = [];
    int count;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Defined(Symbol id)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Symbol id, out MRubyValue value)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Get(Symbol id)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Symbol id, MRubyValue value)
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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(Symbol id, out MRubyValue removedValue)
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

    public void Clear()
    {
        if (count > 0)
        {
            Array.Clear(keys, 0, count);
            Array.Clear(values, 0, count);
            count = 0;
        }
    }

    public void CopyTo(VariableTable other)
    {
        if (count == 0) return;
        if (other.keys.Length < other.count + count)
        {
            var newSize = Math.Max(other.keys.Length == 0 ? 4 : other.keys.Length * 2, other.count + count);
            Array.Resize(ref other.keys, newSize);
            Array.Resize(ref other.values, newSize);
        }
        Array.Copy(keys, 0, other.keys, other.count, count);
        Array.Copy(values, 0, other.values, other.count, count);
        other.count += count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Grow()
    {
        var newSize = keys.Length == 0 ? 4 : keys.Length * 2;
        Array.Resize(ref keys, newSize);
        Array.Resize(ref values, newSize);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<Symbol, MRubyValue>> IEnumerable<KeyValuePair<Symbol, MRubyValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
            get => new(table.keys[index], table.values[index]);
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
