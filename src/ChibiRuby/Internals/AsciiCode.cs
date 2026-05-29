using System;
using System.Runtime.CompilerServices;

namespace ChibiRuby.Internals;

static class Utf8Helper
{
    static readonly int[] Utf8SequenceLengthTable =
    [
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 4, 1
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetUtf8SequenceLength(byte lead)
    {
        return Utf8SequenceLengthTable[lead >> 3];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFirstUtf8Sequence(byte lead)
    {
        return (lead & 0b1100_0000) != 0b1000_0000;
    }

    public static int FindByteIndexAt(ReadOnlySpan<byte> utf8, int utf8Pos)
    {
        if (utf8Pos <= 0) return 0;

        var index = 0;
        var charCount = 0;
        while (index < utf8.Length && charCount < utf8Pos)
        {
            var seqLen = GetUtf8SequenceLength(utf8[index]);
            if (seqLen > 1)
            {
                if (index + seqLen > utf8.Length ||
                    IsFirstUtf8Sequence(utf8[index + 1]))
                {
                    // invalid
                    seqLen = 1;
                }
            }

            index += seqLen;
            charCount++;
        }
        return index;
    }

    public static byte[] Reverse(ReadOnlySpan<byte> span)
    {
        var readPos = span.Length - 1;
        var writePos = 0;
        var output = new byte[span.Length];

        while (readPos >= 0)
        {
            var start = readPos;
            while (start > 0 && !IsFirstUtf8Sequence(span[start]))
            {
                start--;
            }

            var length = readPos - start + 1;
            if (length == 1)
            {
                output[writePos] = span[readPos];
            }
            else
            {
                span.Slice(start, length).CopyTo(output.AsSpan(writePos));
            }

            writePos += length;
            readPos -= length;
        }
        return output;
    }
}

static class AsciiCode
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(byte c) => (byte)((c | 0x20) - (byte)'0') < 10;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAlphabet(byte c) => (byte)((c | 0x20) - (byte)'a') < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsHexAlphabet(byte c) => (byte)((c | 0x20) - (byte)'a') < 6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAscii(byte c) => c <= 0x7f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUpper(byte c) => (byte)(c - (byte)'A') < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLower(byte c) => (byte)(c - (byte)'a') < 26;

    public static bool IsIdentifier(byte c) => IsAlphabet(c) ||
                                               IsDigit(c) ||
                                               c == (byte)'_' ||
                                               !IsAscii(c);

    public static bool IsIdentifier(ReadOnlySpan<byte> span)
    {
        foreach (var x in span)
        {
            if (!IsIdentifier(x)) return false;
        }

        return true;
    }

    public static bool IsPrint(byte c) => (byte)(c - 0x20) < 0x5f;

    public static bool IsLineBreak(byte c) => c is (byte)'\n' or (byte)'\r';

    public static bool IsWhiteSpace(byte c) => c == (byte)' ' ||
                                               (c - (byte)'\t' < 5);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToUpper(byte b) => IsLower(b) ? (byte)(b - 0x20) : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToLower(byte b) => IsUpper(b) ? (byte)(b + 0x20) : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToUpper(Span<byte> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = ToUpper(span[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToLower(Span<byte> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = ToLower(span[i]);
        }
    }

    public static void PrepareNumber(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var i = 0;
        var j = 0;

        while (i < source.Length && IsWhiteSpace(source[i]))
        {
            i++;
        }

        if (i >= source.Length ||
            (!IsDigit(source[i]) &&
             !IsHexAlphabet(source[i]) &&
             source[i] != '+' && source[i] != '-' && source[i] != '.'))
        {
            return;
        }

        var allowUnderscore = false;
        while (i < source.Length)
        {
            var ch = source[i++];
            if (IsDigit(ch))
            {
                destination[j++] = ch;
                allowUnderscore = true;
            }
            else if (ch == '+' ||
                     ch == '-' ||
                     ch == '.' ||
                     IsHexAlphabet(ch))
            {
                destination[j++] = ch;
                allowUnderscore = false;
            }
            else if (allowUnderscore && ch == '_')
            {
                allowUnderscore = false;
            }
            else
            {
                break;
            }
        }
    }

    public static bool TryParseBinary(ReadOnlySpan<byte> bytes, out long result)
    {
        result = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            result <<= 1;
            var b = bytes[i];
            if (b == (byte)'1')
            {
                result |= 1;
            }
            else if (b != (byte)'0')
            {
                return i > 0;
            }
        }
        return true;
    }
}