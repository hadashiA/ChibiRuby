using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif

namespace ChibiRuby;

public sealed class RArray : RObject, IEnumerable<MRubyValue>
{
    public static int MaxLength => 0X7FFFFFC7;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private set;
    }

    public MRubyValue this[int index]
    {
        get
        {
            if (index < 0)
            {
                index += Length;
            }
            if ((uint)index < (uint)Length)
            {
                return Unsafe.Add(ref GetArrayDataReference(data), offset + index);
            }
            return MRubyValue.Nil;
        }
        set
        {
            if (index < 0)
            {
                index += Length;
            }
            MakeModifiable(index + 1, index >= Length);
            Unsafe.Add(ref GetArrayDataReference(data), offset + index) = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index, MRubyValue value)
    {
        if (index < 0)
        {
            index += Length;
        }
        var length = Length;
        if ((uint)index < (uint)length)
        {
            if (!dataOwned)
            {
                MakeModifiable(length);
            }
            Unsafe.Add(ref GetArrayDataReference(data), offset + index) = value;
            return;
        }

        MakeModifiable(index + 1, index >= length);
        Unsafe.Add(ref GetArrayDataReference(data), offset + index) = value;
    }

    MRubyValue[] data;
    int offset;
    bool dataOwned;

    public Span<MRubyValue> AsSpan() => data.AsSpan(offset, Length);

    public Span<MRubyValue> AsSpan(int start, int count) =>
        data.AsSpan(offset + start, count);

    public Span<MRubyValue> AsSpan(int start) =>
        data.AsSpan(offset + start, Length - start);

    internal RArray(ReadOnlySpan<MRubyValue> values, RClass arrayClass)
        : base(MRubyVType.Array, arrayClass)
    {
        Length = values.Length;
        offset = 0;
        data = values.ToArray();
        dataOwned = true;
    }

    internal RArray(int capacity, RClass arrayClass) : base(MRubyVType.Array, arrayClass)
    {
        Length = 0;
        offset = 0;
        data = capacity == 0 ? [] : new MRubyValue[capacity];
        dataOwned = true;
    }

    RArray(RArray shared)
        : this(shared, 0, shared.Length, shared.Class)
    {
    }

    RArray(RArray shared, int start, int size, RClass klass) : base(MRubyVType.Array, klass)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (size > shared.Length - start)
        {
            size = shared.Length - start;
        }
        Length = size;
        offset = shared.offset + start;
        data = shared.data;
        dataOwned = false;
        shared.dataOwned = false;
    }

    public override string ToString()
    {
        var list = AsSpan().ToArray().Select(x => x.ToString());
        return $"[{string.Join(", ", list)}]";
    }

    public RArray Dup() => new(this);

    public RArray SubSequence(int start, int length)
    {
        return CopySubSequence(start, length, Class);
    }

    internal RArray CopySubSequence(int start, int length, RClass arrayClass)
    {
        NormalizeSubSequence(ref start, ref length);
        if (length <= 0)
        {
            return new RArray(0, arrayClass);
        }
        var result = new RArray(length, arrayClass)
        {
            Length = length
        };
        Array.Copy(data, offset + start, result.data, 0, length);
        return result;
    }

    internal RArray SharedSubSequence(int start, int length, RClass arrayClass)
    {
        NormalizeSubSequence(ref start, ref length);
        if (length <= 0)
        {
            return new RArray(0, arrayClass);
        }
        return new RArray(this, start, length, arrayClass);
    }

    void NormalizeSubSequence(ref int start, ref int length)
    {
        if (start < 0)
        {
            length += start;
            start = 0;
        }
        if (start > Length)
        {
            start = Length;
        }
        if (length > Length - start)
        {
            length = Length - start;
        }
    }

    public void Clear()
    {
        if (dataOwned)
        {
            AsSpan().Clear();
            Length = 0;
        }
        else
        {
            MakeModifiable(0, true);
        }
    }

    public void Push(MRubyValue newItem)
    {
        var currentLength = Length;
        if (dataOwned && data.Length - offset > currentLength)
        {
            Length = currentLength + 1;
            Unsafe.Add(ref GetArrayDataReference(data), offset + currentLength) = newItem;
            return;
        }

        MakeModifiable(currentLength + 1, true);
        Unsafe.Add(ref GetArrayDataReference(data), offset + currentLength) = newItem;
    }

    public bool TryPop(out MRubyValue value)
    {
        if (Length <= 0)
        {
            value = default;
            return false;
        }

        value = Unsafe.Add(ref GetArrayDataReference(data), offset + Length - 1);
        MakeModifiable(Length - 1, true);
        return true;
    }

    public MRubyValue Shift()
    {
        if (Length <= 0) return MRubyValue.Nil;
        var result = this[0];
        offset++;
        Length--;
        return result;
    }

    public RArray Shift(int n)
    {
        if (Length <= 0 || n <= 0) return new RArray(0, Class);
        if (n > Length) n = Length;

        var result = SharedSubSequence(0, n, Class);
        offset += n;
        Length -= n;
        return result;
    }

    public void Unshift(ReadOnlySpan<MRubyValue> newItems)
    {
        if (newItems.Length <= 0) return;

        var currentLength = Length;
        MakeModifiable(Length + newItems.Length, true);
        var span = AsSpan();
        AsSpan(0,currentLength).CopyTo(AsSpan(newItems.Length));
        newItems.CopyTo(span);
    }

    public void Concat(RArray other)
    {
        if (other.Length <= 0)
        {
            return;
        }

        if (Length <= 0)
        {
            Length = other.Length;
            data = other.data;
            offset = other.offset;
            dataOwned = false;
            other.dataOwned = false;
            return;
        }

        var currentLength = Length;
        var newLength = currentLength + other.Length;
        var source = other.AsSpan();
        MakeModifiable(newLength, true);
        source.CopyTo(AsSpan(currentLength));
    }

    public MRubyValue DeleteAt(int index)
    {
        if (index < 0) index += Length;
        if (index < 0 || index >= Length) return MRubyValue.Nil;

        var value = Unsafe.Add(ref GetArrayDataReference(data), offset + index);
        MakeModifiable(Length);
        var src = AsSpan(index + 1);
        var dst = AsSpan(index);
        src.CopyTo(dst);
        Length--;
        return value;
    }

    public void CopyTo(RArray other)
    {
        if (ReferenceEquals(this, other))
        {
            return;
        }

        other.MakeModifiable(Length, true);
        AsSpan().CopyTo(other.AsSpan());
    }

    public void ReplaceTo(RArray other)
    {
        CopyTo(other);
    }

    internal override RObject Clone()
    {
        var clone = new RArray(data.Length, Class);
        InstanceVariables.CopyTo(ref clone.InstanceVariables);
        return clone;
    }

    internal void PushRange(ReadOnlySpan<MRubyValue>newItems)
    {
        var start = Length;
        MakeModifiable(start + newItems.Length, true);
        newItems.CopyTo(AsSpan(start));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MakeModifiable(int capacity, bool expandLength = false)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (dataOwned)
        {
            if (offset == 0)
            {
                if (data.Length < capacity)
                {
                    Array.Resize(ref data, CalculateCapacity(data.Length, capacity));
                }
            }
            else if (data.Length - offset < capacity)
            {
                Compact(capacity, expandLength);
            }
        }
        else
        {
            Compact(capacity, expandLength);
        }

        if (expandLength)
        {
            Length = capacity;
        }
    }

    void Compact(int capacity, bool expandLength)
    {
        var targetLength = expandLength ? capacity : Length;
        var copyLength = Math.Min(Length, targetLength);
        var newData = new MRubyValue[CalculateCapacity(copyLength, capacity)];
        if (copyLength > 0)
        {
            data.AsSpan(offset, copyLength).CopyTo(newData);
        }
        data = newData;
        offset = 0;
        dataOwned = true;
    }

    static int CalculateCapacity(int currentCapacity, int requiredCapacity)
    {
        var newCapacity = currentCapacity * 2;
        if (newCapacity < requiredCapacity)
        {
            newCapacity = requiredCapacity;
        }
        return newCapacity;
    }

    public struct Enumerator(RArray source) : IEnumerator<MRubyValue>
    {
        public MRubyValue Current { get; private set; }
        object IEnumerator.Current => Current;

        int index = -1;

        public bool MoveNext()
        {
            if (index + 1 < source.Length)
            {
                index++;
                Current = source[index];
                return true;
            }
            return false;
        }

        public void Reset()
        {
            index = -1;
            Current = default;
        }

        public void Dispose() { }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<MRubyValue> IEnumerable<MRubyValue>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
