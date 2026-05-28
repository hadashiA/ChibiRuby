using System;
using System.Runtime.CompilerServices;
using System.Text;
using ChibiRuby.Internals;

namespace ChibiRuby;

enum RStringRangeType
{
    /// <summary>
    /// `beg` and `len` are byte unit in `0 ... str.bytesize`
    /// </summary>
    ByteRangeCorrected = 1,

    /// <summary>
    /// `beg` and `len` are char unit in any range
    /// </summary>
    CharRange = 2,

    /// <summary>
    /// `beg` and `len` are char unit in `0 ... str.size`
    /// </summary>
    CharRangeCorrected = 3,

    /// <summary>
    /// `beg` is out of range
    /// </summary>
    OutOfRange = -1
}

enum RStringSplitType
{
    Whitespaces,
    String
}

public class RString : RObject, IEquatable<RString>
#if NET6_0_OR_GREATER
    , ISpanFormattable, IUtf8SpanFormattable
#endif
{
    public int Length { get; private set; }

    byte[] buffer;
    int offset;
    bool bufferOwned;

    public static RString Owned(byte[] value, RClass stringClass)
    {
        return new RString(value, 0, value.Length, stringClass);
    }

    public static RString Owned(byte[] value, int offset, int length, RClass stringClass)
    {
        return new RString(value, offset, length, stringClass);
    }

    public static RString operator+(RString a, RString b)
    {
        var buffer = new byte[a.Length + b.Length];
        a.AsSpan().CopyTo(buffer);
        b.AsSpan().CopyTo(buffer.AsSpan(a.Length));
        return Owned(buffer, a.Class);
    }

    internal RString(int capacity, RClass stringClass)
        : base(MRubyVType.String, stringClass)
    {
        buffer = new byte[capacity];
        Length = 0;
        bufferOwned = true;
    }

    internal RString(ReadOnlySpan<byte> utf8, RClass stringClass)
        : base(MRubyVType.String, stringClass)
    {
        buffer = new byte[utf8.Length];
        Length = utf8.Length;
        bufferOwned = true;
        utf8.CopyTo(buffer);
    }

    RString(RString shared) : base(MRubyVType.String, shared.Class)
    {
        buffer = shared.buffer;
        Length = shared.Length;
        offset = shared.offset;
        bufferOwned = false;
    }

    RString(byte[] buffer, int offset, int length, RClass stringClass) : base(MRubyVType.String, stringClass)
    {
        this.buffer = buffer;
        this.offset = offset;
        Length = length;
        bufferOwned = true;
        MarkAsFrozen();
    }

    public static implicit operator Span<byte>(RString str) => str.AsSpan();
    public static implicit operator ReadOnlySpan<byte>(RString str) => str.AsSpan();

    public static bool operator ==(RString? left, RString? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(RString? left, RString? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(buffer, offset, Length);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan() => buffer.AsSpan(offset, Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan(int start) => buffer.AsSpan(offset + start, Length - start);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan(int start, int length) => buffer.AsSpan(offset + start, length);

    public RString Dup() => new(this);

    internal override RObject Clone()
    {
        var clone = new RString(buffer.Length, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public RString? SubString(int utf8Start, int utf8Length)
    {
        var span = AsSpan();
        var charCount = Encoding.UTF8.GetCharCount(span);
        if (TryConvertRange(charCount, ref utf8Start, ref utf8Length))
        {
            var start = Utf8Helper.FindByteIndexAt(span, utf8Start);
            var end = Utf8Helper.FindByteIndexAt(span[start..], utf8Length) + start;

            var slice = span.Slice(start, end - start);
            return new RString(slice, Class);
        }
        return null;
    }

    public RString? SubByteSequence(int bytesStart, int bytesLength)
    {
        var span = AsSpan();
        if (TryConvertRange(span.Length, ref bytesStart, ref bytesLength))
        {
            var slice = span.Slice(bytesStart, bytesLength);
            return new RString(slice, Class);
        }
        return null;
    }

    public RString? GetPartial(MRubyState state, MRubyValue indexValue, int? rangeLength = null)
    {
        switch (CalculateStringRange(state, indexValue, rangeLength, out var calculatedOffset, out var calculatedLength))
        {
            case RStringRangeType.CharRangeCorrected:
                var span = AsSpan();
                var bytesOffset = Utf8Helper.FindByteIndexAt(span, calculatedOffset);
                var bytesLength = Utf8Helper.FindByteIndexAt(span, calculatedLength);
                return new RString(span.Slice(bytesOffset, bytesLength), Class);

            case RStringRangeType.CharRange:
                return SubString(calculatedOffset, calculatedLength);

            case RStringRangeType.ByteRangeCorrected:
                if (indexValue.Object is RString targetStr)
                {
                    return targetStr.Dup();
                }
                return SubString(calculatedOffset, calculatedLength);
        }
        return null;
    }

    public void SetPartial(MRubyState state, MRubyValue indexValue, int? rangeLength, RString? value)
    {
        var span = AsSpan();

        switch (CalculateStringRange(state, indexValue, rangeLength, out var calculatedOffset, out var calculatedLength))
        {
            case RStringRangeType.CharRange:
                if (calculatedLength < 0)
                {
                    state.Raise(Names.IndexError, "nagative length"u8);
                }
                var charCount = Encoding.UTF8.GetCharCount(span);
                if (calculatedOffset < 0)
                {
                    calculatedOffset += charCount;
                }
                if (calculatedOffset < 0 || calculatedOffset > charCount)
                {
                    state.Raise(Names.IndexError, $"index {state.Stringify(indexValue)} out of string");
                }
                calculatedOffset = Utf8Helper.FindByteIndexAt(span, calculatedOffset);
                calculatedLength = Utf8Helper.FindByteIndexAt(span, calculatedLength);
                break;
            case RStringRangeType.CharRangeCorrected:
                calculatedOffset = Utf8Helper.FindByteIndexAt(span, calculatedOffset);
                calculatedLength = Utf8Helper.FindByteIndexAt(span, calculatedLength);
                break;
            case RStringRangeType.ByteRangeCorrected:
                break;
            case RStringRangeType.OutOfRange:
                state.Raise(Names.IndexError, "string not matched"u8);
                break;
        }

        var pos = calculatedOffset;
        var end = calculatedOffset + calculatedLength;
        if (end > Length) end = Length;

        if (pos < 0 || pos > Length)
        {
            state.Raise(Names.IndexError, $"index {pos} out of string");
        }

        var currentLength = Length;
        var newLength = (value?.Length ?? 0) + (Length - (end - pos));
        MakeModifiable(newLength, true);

        // move latter half
        if (newLength != currentLength)
        {
            AsSpan(end, currentLength - end)
                .CopyTo(AsSpan(newLength - (currentLength - end)));
        }

        // insert value
        if (value != null)
        {
            value.AsSpan().CopyTo(AsSpan(pos));
        }
    }

    public void Concat(byte ch)
    {
        var currentLength = Length;
        var newLength = currentLength + 1;
        MakeModifiable(newLength, true);
        AsSpan()[currentLength] = ch;
    }

    public void Concat(RString other)
    {
        Concat(other.AsSpan());
    }

    public void Concat(ReadOnlySpan<byte> utf8)
    {
        var currentLength = Length;
        var newLength = currentLength + utf8.Length;
        MakeModifiable(newLength, true);
        utf8.CopyTo(AsSpan(currentLength));
    }

    public void Upcase()
    {
        if (Length <= 0) return;
        MakeModifiable(Length);
        var span = AsSpan();
        AsciiCode.ToUpper(span);
    }

    public void Downcase()
    {
        if (Length <= 0) return;
        MakeModifiable(Length);
        var span = AsSpan();
        AsciiCode.ToLower(span);
    }

    public void Capitalize()
    {
        if (Length <= 0) return;
        MakeModifiable(Length);

        var firstChar = buffer[offset];
        if (AsciiCode.IsLower(firstChar))
        {
            buffer[offset] = AsciiCode.ToUpper(firstChar);
            AsciiCode.ToLower(buffer.AsSpan(offset + 1));
        }
        else
        {
            AsciiCode.ToLower(buffer.AsSpan());
        }
    }

    public void Chomp()
    {
        var span = AsSpan();
        if (span.Length > 0)
        {
            switch (span[^1])
            {
                case (byte)'\n':
                    if (span.Length > 1 && span[^2] == '\r')
                    {
                        Length -= 2;
                    }
                    else
                    {
                        Length--;
                    }
                    break;
                case (byte)'\r':
                    Length--;
                    break;
            }
        }
    }

    public void Chomp(ReadOnlySpan<byte> paragraph)
    {
        var span = AsSpan();
        var index = span.LastIndexOf(paragraph);
        if (index >= 0 && span.Length - index == paragraph.Length)
        {
            Length -= paragraph.Length;
        }
    }

    public void Chop()
    {
        var span = AsSpan();
        if (span.Length > 0)
        {
            var lastChar = span[^1];
            switch (lastChar)
            {
                case (byte)'\n':
                    if (span.Length > 1 && span[^2] == '\r')
                    {
                        Length -= 2;
                    }
                    else
                    {
                        Length--;
                    }
                    break;
                case var _ when AsciiCode.IsAscii(lastChar):
                    Length--;
                    break;
                default: // parse as utf8
                    var index = span.Length - 1;
                    while (index > 0 && !Utf8Helper.IsFirstUtf8Sequence(span[index]))
                    {
                        index--;
                    }
                    var lastUtf8Sequence = span[index];
                    Length -= Utf8Helper.GetUtf8SequenceLength(lastUtf8Sequence);
                    break;
            }
        }
    }

    public void CopyTo(RString other)
    {
        other.MakeModifiable(Length);
        other.Length = Length;
        AsSpan().CopyTo(other.AsSpan());
    }

    // TODO:
    public int CompareTo(RString other)
    {
        var a = Encoding.UTF8.GetString(AsSpan());
        var b = Encoding.UTF8.GetString(other.AsSpan());
        return string.CompareOrdinal(a, b) switch
        {
            > 0 => 1,
            < 0 => -1,
            _ => 0
        };
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        FormattableString formattable =
            $"{nameof(buffer)}: {buffer}, {nameof(Length)}: {Length}, {nameof(bufferOwned)}: {bufferOwned}";
        return formattable.ToString(formatProvider);
    }

#if NET6_0_OR_GREATER
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        return Encoding.UTF8.TryGetChars(AsSpan(), destination, out charsWritten);
    }

    public bool TryFormat(
        Span<byte> destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        var span = AsSpan();
        if (destination.Length < span.Length)
        {
            bytesWritten = default;
            return false;
        }
        span.CopyTo(destination);
        bytesWritten = span.Length;
        return true;
    }
#endif

    public int ByteIndexOf(ReadOnlySpan<byte> target, int pos = 0)
    {
        if (pos < 0)
        {
            pos += target.Length;
            if (pos < 0)
            {
                return -1;
            }
        }

        if (Length - pos < target.Length) return -1;
        if (target.Length <= 0) return pos;

        if (pos > 0)
        {
            var result = AsSpan(pos).IndexOf(target);
            return result >= 0 ? result + pos : result;
        }
        return AsSpan().IndexOf(target);
    }

    public int IndexOf(ReadOnlySpan<byte> target, int utf8Pos = 0)
    {
        var span = AsSpan();
        var charCount = Encoding.UTF8.GetCharCount(span);

        if (utf8Pos < 0)
        {
            utf8Pos += charCount;
            if (utf8Pos < 0)
            {
                return -1;
            }
        }

        var targetCharCount = Encoding.UTF8.GetCharCount(target);
        if (charCount - utf8Pos < targetCharCount) return -1;
        if (targetCharCount <= 0) return utf8Pos;

        var i = 0;
        var c = 0;
        while (i < span.Length)
        {
            if (c >= utf8Pos && span[i..].StartsWith(target))
            {
                return c;
            }
            var seqLength = Utf8Helper.GetUtf8SequenceLength(span[i]);
            if (seqLength > 1)
            {
                if (i + seqLength > span.Length ||
                    Utf8Helper.IsFirstUtf8Sequence(span[i + 1]))
                {
                    // invalid
                    seqLength = 1;
                }
            }
            i += seqLength;
            c++;
        }

        return -1;
    }

    public int LstIndexOf(ReadOnlySpan<byte> target, int utf8Pos = 0)
    {
        var span = AsSpan();
        var charCount = Encoding.UTF8.GetCharCount(span);

        if (utf8Pos < 0)
        {
            utf8Pos += charCount;
            if (utf8Pos < 0)
            {
                return -1;
            }
        }

        var targetCharCount = Encoding.UTF8.GetCharCount(target);

        if (charCount < targetCharCount) return -1;
        if (charCount - utf8Pos < targetCharCount)
        {
            utf8Pos = charCount - targetCharCount;
        }

        if (target.Length <= 0) return utf8Pos;

        var pos = Utf8Helper.FindByteIndexAt(span, utf8Pos);
        var i = pos;
        if (i >= span.Length) i = span.Length - 1;

        while (i >= 0)
        {
            while (i > 0 && !Utf8Helper.IsFirstUtf8Sequence(span[i]))
            {
                i--;
            }
            if (i <= pos && span[i..].StartsWith(target))
            {
                return utf8Pos;
            }

            i--;
            utf8Pos--;
        }
        return -1;
    }

    public void SplitByWhitespacesTo(RArray result, int limit = -1)
    {
        var span = AsSpan();
        var i = 0;

        var skip = true;
        var elementStart = 0;
        var elementEnd = 0;
        while (i < span.Length)
        {
            var ch = span[i++];
            if (skip)
            {
                if (AsciiCode.IsWhiteSpace(ch))
                {
                    elementStart = i;
                }
                else
                {
                    elementEnd = i;
                    skip = false;
                    if (limit >= 0 && limit <= i) break;
                }
            }
            else if (AsciiCode.IsWhiteSpace(ch))
            {
                var slice = span.Slice(elementStart, elementEnd - elementStart);
                var element = new RString(slice, Class);

                result.Push(new MRubyValue(element));
                skip = true;
                elementStart = i;
            }
            else
            {
                elementEnd = i;
            }
        }

        if (span.Length > 0 && elementStart < span.Length && (limit < 0 || limit > result.Length))
        {
            var remaining = SubByteSequence(elementStart, span.Length - elementStart)!;
            result.Push(new MRubyValue(remaining));
        }
    }

    public void SplitBytSeparatorTo(RArray result, RString separator, int limit = -1)
    {
        var span = AsSpan();
        var separatorSpan = separator.AsSpan();

        var i = 0;
        while (i < span.Length)
        {
            if (separatorSpan.Length > 0)
            {
                var elementEnd = span[i..].IndexOf(separatorSpan);
                if (elementEnd < 0) break;

                var slice = span.Slice(i, elementEnd);
                result.Push(new RString(slice, Class));
                i += elementEnd + separatorSpan.Length;
            }
            else
            {
                var l = Utf8Helper.GetUtf8SequenceLength(span[i]);
                var slice = span.Slice(i, l);
                result.Push(new RString(slice, Class));
                i += l;
            }
        }

        if (span.Length > 0 && i < span.Length && (limit < 0 || limit > result.Length))
        {
            var remaining = SubByteSequence(i, span.Length - i)!;
            result.Push(remaining);
        }
    }

    internal static uint GetHashCode(ReadOnlySpan<byte> span)
    {
        const uint OffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;
        var hash = OffsetBasis;
        foreach (var b in span)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }
    public override int GetHashCode()
    {
        return unchecked((int)GetHashCode(AsSpan()));
    }

    public bool Equals(RString? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RString)obj);
    }

    static bool TryConvertRange(int maxLength, ref int start, ref int length)
    {
        if (maxLength < start || length < 0) return false;
        if (start < 0)
        {
            start += maxLength;
            if (start < 0) return false;
        }
        if (length > maxLength - start)
        {
            length = maxLength - start;
        }
        if (length <= 0)
        {
            length = 0;
        }
        return true;
    }

    internal void MakeModifiable(int capacity, bool expandLength = false)
    {
        if (buffer.Length - offset < capacity)
        {
            var newLength = buffer.Length * 2;
            if (newLength - offset < capacity)
            {
                newLength = capacity;
            }

            if (bufferOwned)
            {
                Array.Resize(ref buffer, newLength);
            }
            else
            {
                var newBuffer = new byte[newLength];
                buffer.AsSpan(offset).CopyTo(newBuffer);
                buffer = newBuffer;
                offset = 0;
                bufferOwned = true;
            }
        }
        else if (!bufferOwned)
        {
            buffer = AsSpan().ToArray();
            bufferOwned = true;
        }

        if (expandLength)
        {
            Length = capacity;
        }
    }

    RStringRangeType CalculateStringRange(
        MRubyState state,
        MRubyValue index,
        int? indexLength,
        out int calculatedOffset,
        out int calculatedLength)
    {
        if (indexLength.HasValue)
        {
            calculatedOffset = (int)state.AsInteger(index);
            calculatedLength = indexLength.Value;
            return RStringRangeType.CharRange;
        }

        if (index.IsInteger)
        {
            calculatedOffset = (int)index.IntegerValue;
            calculatedLength = 1;
            return RStringRangeType.CharRange;
        }

        var utf8 = AsSpan();

        switch (index.Object)
        {
            case RString targetStr:
                calculatedOffset = utf8.IndexOf(targetStr);
                calculatedLength = targetStr.Length;
                return calculatedOffset < 0
                    ? RStringRangeType.OutOfRange
                    : RStringRangeType.ByteRangeCorrected;
            case RRange range:
                var utf8Length = Encoding.UTF8.GetCharCount(utf8);
                if (range.Calculate(utf8Length, true, out calculatedOffset, out calculatedLength) == RangeCalculateResult.Ok)
                {
                    return RStringRangeType.CharRangeCorrected;
                }
                break;
            default:
                calculatedOffset = (int)state.AsInteger(index);
                calculatedLength = 1;
                return RStringRangeType.CharRange;
        }

        calculatedOffset = default;
        calculatedLength = default;
        return RStringRangeType.OutOfRange;
    }
}
