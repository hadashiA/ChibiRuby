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

    // Vectorized scan: Symbol is a single uint, so the key array can be searched
    // with the SIMD IndexOf over uints instead of a scalar loop.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int IndexOfKey(Symbol id) =>
        System.Runtime.InteropServices.MemoryMarshal
            .Cast<Symbol, uint>(keys.AsSpan(0, count))
            .IndexOf(id.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Defined(Symbol id) => IndexOfKey(id) >= 0;

    // Slot-verified accessors used by the interpreter's inline caches. `slot` is an
    // untrusted guess: it hits only when the entry at that index is exactly `id`,
    // so a stale or foreign slot value can never read/write the wrong variable.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetAt(int slot, Symbol id, out MRubyValue value)
    {
        if ((uint)slot < (uint)count &&
            Unsafe.Add(ref GetArrayDataReference(keys), slot) == id)
        {
            value = Unsafe.Add(ref GetArrayDataReference(values), slot);
            return true;
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TrySetAt(int slot, Symbol id, MRubyValue value)
    {
        if ((uint)slot < (uint)count &&
            Unsafe.Add(ref GetArrayDataReference(keys), slot) == id)
        {
            Unsafe.Add(ref GetArrayDataReference(values), slot) = value;
            return true;
        }
        return false;
    }

    /// <summary>Get that also reports the slot the symbol was found at (-1 when missing).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetWithSlot(Symbol id, out MRubyValue value)
    {
        var i = IndexOfKey(id);
        value = i >= 0 ? Unsafe.Add(ref GetArrayDataReference(values), i) : default;
        return i;
    }

    /// <summary>Set that also reports the slot the value was stored at.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int SetWithSlot(Symbol id, MRubyValue value)
    {
        var i = IndexOfKey(id);
        if (i >= 0)
        {
            Unsafe.Add(ref GetArrayDataReference(values), i) = value;
            return i;
        }
        if (count >= keys.Length) Grow();

        Unsafe.Add(ref GetArrayDataReference(keys), count) = id;
        Unsafe.Add(ref GetArrayDataReference(values), count) = value;
        return count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Symbol id, out MRubyValue value)
    {
        var i = IndexOfKey(id);
        if (i >= 0)
        {
            value = Unsafe.Add(ref GetArrayDataReference(values), i);
            return true;
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Get(Symbol id)
    {
        var i = IndexOfKey(id);
        return i >= 0 ? Unsafe.Add(ref GetArrayDataReference(values), i) : default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Symbol id, MRubyValue value) => SetWithSlot(id, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(Symbol id, out MRubyValue removedValue)
    {
        var i = IndexOfKey(id);
        if (i >= 0)
        {
            ref var keysRef = ref GetArrayDataReference(keys);
            ref var valsRef = ref GetArrayDataReference(values);
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
