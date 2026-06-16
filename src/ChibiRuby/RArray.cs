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
                return data[offset + index];
            }
            return MRubyValue.Nil;
        }
        set
        {
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
        data = new MRubyValue[capacity];
        dataOwned = true;
    }

    RArray(RArray shared)
        : this(shared, 0, shared.Length)
    {
    }

    RArray(RArray shared, int offset, int size) : base(MRubyVType.Array, shared.Class)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (size > shared.Length)
        {
            size = shared.Length;
        }
        Length = size;
        this.offset = offset;
        data = shared.data;
        dataOwned = false;
    }

    public override string ToString()
    {
        var list = AsSpan().ToArray().Select(x => x.ToString());
        return $"[{string.Join(", ", list)}]";
    }

    public RArray Dup() => new(this);

    public RArray SubSequence(int start, int length)
    {
        return new RArray(this, start, length);
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
        MakeModifiable(currentLength + 1, true);
        data[currentLength] = newItem;
    }

    public bool TryPop(out MRubyValue value)
    {
        if (Length <= 0)
        {
            value = default;
            return false;
        }

        value = data[offset + Length - 1];
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

        var result = new RArray(this)
        {
            Length = n
        };
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
        if (Length <= 0)
        {
            Length = other.Length;
            data = other.data;
            dataOwned = false;
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

        var value = data[offset + index];
        var src = AsSpan(index + 1);
        var dst = AsSpan(index);
        src.CopyTo(dst);
        Length--;
        return value;
    }

    public void CopyTo(RArray other)
    {
        other.MakeModifiable(Length);
        other.Length = Length;
        AsSpan().CopyTo(other.AsSpan());
    }

    public void ReplaceTo(RArray other)
    {
        other.Length = 0;
        CopyTo(other);
    }

    internal override RObject Clone()
    {
        var clone = new RArray(data.Length, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
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
        if (data.Length - offset < capacity)
        {
            var newLength = data.Length * 2;
            if (newLength - offset < capacity)
            {
                newLength = capacity;
            }

            if (dataOwned)
            {
                Array.Resize(ref data, newLength);
            }
            else
            {
                var newData = new MRubyValue[newLength];
                data.AsSpan(offset).CopyTo(newData);
                data = newData;
                offset = 0;
                dataOwned = true;
            }
        }
        else if (!dataOwned)
        {
            var newData = new MRubyValue[data.Length];
            data.AsSpan(offset).CopyTo(newData);
            data = newData;
            offset = 0;
            dataOwned = true;
        }

        if (expandLength)
        {
            Length = capacity;
        }
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
