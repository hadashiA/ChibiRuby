using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ChibiRuby.NIO;

/// <summary>Raised on malformed input; translated to <c>JSON::ParserError</c>.</summary>
internal sealed class MRubyJsonFormatException(string message, int position, int line, int column)
    : Exception($"{message} (line {line}, column {column}, pos {position})")
{
    public int Position { get; } = position;
}

internal enum JsonTokenKind
{
    EndOfInput,
    StartObject,
    StartArray,
    String,
    Number,
    True,
    False,
    Null,
    /// <summary>Non-standard <c>NaN</c> / <c>Infinity</c> / <c>-Infinity</c> literal (allow_nan).</summary>
    NonFiniteNumber,
}

/// <summary>Forward-only JSON tokenizer over a UTF-8 span with lazy escape decoding.</summary>
/// <remarks>Stays UTF-8 end to end (RString is UTF-8 internally): escapes decode directly to UTF-8 bytes with no UTF-16 round trip.</remarks>
internal ref struct Utf8JsonTokenizer(ReadOnlySpan<byte> input)
{
    readonly ReadOnlySpan<byte> input = input;
    int pos = 0;
    int valueStart = 0;
    int valueEnd = 0;
    bool valueHasEscape = false;

    public int Position => pos;

    /// <summary>Skips whitespace and classifies the next value without consuming it.</summary>
    public JsonTokenKind PeekTokenType()
    {
        SkipWhitespace();
        if (pos >= input.Length) return JsonTokenKind.EndOfInput;
        var b = input[pos];
        switch (b)
        {
            case (byte)'{': return JsonTokenKind.StartObject;
            case (byte)'[': return JsonTokenKind.StartArray;
            case (byte)'"': return JsonTokenKind.String;
            case (byte)'t': return JsonTokenKind.True;
            case (byte)'f': return JsonTokenKind.False;
            case (byte)'n': return JsonTokenKind.Null;
            case (byte)'N': return JsonTokenKind.NonFiniteNumber;          // NaN
            case (byte)'I': return JsonTokenKind.NonFiniteNumber;          // Infinity
            case (byte)'-':
                // Distinguish "-Infinity" from a negative number.
                return pos + 1 < input.Length && input[pos + 1] == (byte)'I'
                    ? JsonTokenKind.NonFiniteNumber
                    : JsonTokenKind.Number;
            default:
                if (b >= (byte)'0' && b <= (byte)'9') return JsonTokenKind.Number;
                ThrowFormatException($"Unexpected byte 0x{b:X2}");
                return default; // unreachable
        }
    }

    public void ReadStartObject()
    {
        SkipWhitespace();
        if (pos >= input.Length || input[pos] != (byte)'{')
            ThrowFormatException("Expected '{'");
        pos++;
    }

    public void ReadStartArray()
    {
        SkipWhitespace();
        if (pos >= input.Length || input[pos] != (byte)'[')
            ThrowFormatException("Expected '['");
        pos++;
    }

    /// <summary>Reads the next property name + colon, or consumes the closing brace and returns false.</summary>
    public bool TryReadPropertyName(out ReadOnlySpan<byte> rawName, out bool hasEscape)
    {
        SkipWhitespace();
        if (pos >= input.Length) ThrowFormatException("Unexpected end inside object");

        var b = input[pos];
        if (b == (byte)',')
        {
            pos++;
            SkipWhitespace();
            if (pos >= input.Length) ThrowFormatException("Unexpected end after ','");
            b = input[pos];
        }

        if (b == (byte)'}')
        {
            pos++;
            rawName = default;
            hasEscape = false;
            return false;
        }

        if (b != (byte)'"')
            ThrowFormatException($"Expected '\"' for property name, got 0x{b:X2}");

        ReadStringValue();
        rawName = input.Slice(valueStart, valueEnd - valueStart);
        hasEscape = valueHasEscape;

        SkipWhitespace();
        if (pos >= input.Length || input[pos] != (byte)':')
            ThrowFormatException("Expected ':' after property name");
        pos++;
        return true;
    }

    /// <summary>Consumes the closing bracket and returns true, or positions at the next element.</summary>
    public bool TryReadEndArray()
    {
        SkipWhitespace();
        if (pos >= input.Length) ThrowFormatException("Unexpected end inside array");

        var b = input[pos];
        if (b == (byte)',')
        {
            pos++;
            SkipWhitespace();
            if (pos >= input.Length) ThrowFormatException("Unexpected end after ','");
            b = input[pos];
        }

        if (b == (byte)']') { pos++; return true; }
        return false;
    }

    /// <summary>Returns the raw slice between quotes; caller decodes via <see cref="DecodeEscapedUtf8"/> when <paramref name="hasEscape"/>.</summary>
    public ReadOnlySpan<byte> ReadStringRaw(out bool hasEscape)
    {
        SkipWhitespace();
        if (pos >= input.Length || input[pos] != (byte)'"')
            ThrowFormatException("Expected string");
        ReadStringValue();
        hasEscape = valueHasEscape;
        return input.Slice(valueStart, valueEnd - valueStart);
    }

    public bool ReadBoolean()
    {
        SkipWhitespace();
        if (pos >= input.Length) ThrowFormatException("Expected boolean");
        if (input[pos] == (byte)'t') { ConsumeKeyword("true"u8); return true; }
        if (input[pos] == (byte)'f') { ConsumeKeyword("false"u8); return false; }
        ThrowFormatException($"Expected boolean, got 0x{input[pos]:X2}");
        return false; // unreachable
    }

    public void ReadNull() => ConsumeKeyword("null"u8);

    public double ReadNonFiniteNumber()
    {
        SkipWhitespace();
        if (pos >= input.Length) ThrowFormatException("Expected number");
        switch (input[pos])
        {
            case (byte)'N':
                ConsumeKeyword("NaN"u8);
                return double.NaN;
            case (byte)'I':
                ConsumeKeyword("Infinity"u8);
                return double.PositiveInfinity;
            default:
                ConsumeKeyword("-Infinity"u8);
                return double.NegativeInfinity;
        }
    }

    /// <summary>Reads a number as the raw digit slice; the caller picks Integer vs Float.</summary>
    public ReadOnlySpan<byte> ReadNumberRaw()
    {
        SkipWhitespace();
        if (pos >= input.Length) ThrowFormatException("Expected number");
        var first = input[pos];
        if (first != (byte)'-' && (first < (byte)'0' || first > (byte)'9'))
            ThrowFormatException($"Expected number, got 0x{first:X2}");

        var start = pos;
        if (first == (byte)'-') pos++;
        while (pos < input.Length && IsNumberByte(input[pos])) pos++;
        return input.Slice(start, pos - start);
    }

    /// <summary>Rejects trailing garbage after the top-level value.</summary>
    public void EnsureEndOfInput()
    {
        SkipWhitespace();
        if (pos < input.Length)
            ThrowFormatException("Unexpected token after JSON document");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ThrowFormatException(string message)
    {
        var (line, column) = ComputeLineColumn(pos);
        throw new MRubyJsonFormatException(message, pos, line, column);
    }

    (int Line, int Column) ComputeLineColumn(int position)
    {
        int line = 1, col = 1;
        var max = Math.Min(position, input.Length);
        for (var i = 0; i < max; i++)
        {
            if (input[i] == (byte)'\n') { line++; col = 1; }
            else col++;
        }
        return (line, col);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void SkipWhitespace()
    {
        while (pos < input.Length)
        {
            var b = input[pos];
            if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r') pos++;
            else break;
        }
    }

    void ReadStringValue()
    {
        pos++; // opening quote
        valueStart = pos;
        valueHasEscape = false;
        while (pos < input.Length)
        {
            var b = input[pos];
            if (b == (byte)'"')
            {
                valueEnd = pos;
                pos++;
                return;
            }
            if (b == (byte)'\\')
            {
                valueHasEscape = true;
                if (pos + 1 >= input.Length) ThrowFormatException("Truncated escape");
                pos += input[pos + 1] == (byte)'u' ? 6 : 2;
                continue;
            }
            pos++;
        }
        ThrowFormatException("Unterminated string");
    }

    void ConsumeKeyword(ReadOnlySpan<byte> keyword)
    {
        SkipWhitespace();
        if (pos + keyword.Length > input.Length ||
            !input.Slice(pos, keyword.Length).SequenceEqual(keyword))
        {
            ThrowFormatException($"Expected '{System.Text.Encoding.ASCII.GetString(keyword)}'");
        }
        pos += keyword.Length;
    }

    static bool IsNumberByte(byte b) =>
        (b >= (byte)'0' && b <= (byte)'9') ||
        b is (byte)'.' or (byte)'-' or (byte)'+' or (byte)'e' or (byte)'E';

    /// <summary>Decodes backslash escapes (incl. surrogate-pair <c>\uXXXX</c>) directly to UTF-8 bytes.</summary>
    public static byte[] DecodeEscapedUtf8(ReadOnlySpan<byte> bytes)
    {
        var buffer = new ArrayBufferWriter<byte>(bytes.Length);
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b != (byte)'\\')
            {
                var span = buffer.GetSpan(1);
                span[0] = b;
                buffer.Advance(1);
                i++;
                continue;
            }

            if (i + 1 >= bytes.Length) throw new MRubyJsonFormatException("Truncated escape", i, 0, 0);
            var e = bytes[i + 1];
            switch (e)
            {
                case (byte)'"': AppendByte(buffer, (byte)'"'); i += 2; break;
                case (byte)'\\': AppendByte(buffer, (byte)'\\'); i += 2; break;
                case (byte)'/': AppendByte(buffer, (byte)'/'); i += 2; break;
                case (byte)'b': AppendByte(buffer, (byte)'\b'); i += 2; break;
                case (byte)'f': AppendByte(buffer, (byte)'\f'); i += 2; break;
                case (byte)'n': AppendByte(buffer, (byte)'\n'); i += 2; break;
                case (byte)'r': AppendByte(buffer, (byte)'\r'); i += 2; break;
                case (byte)'t': AppendByte(buffer, (byte)'\t'); i += 2; break;
                case (byte)'u':
                {
                    if (i + 6 > bytes.Length) throw new MRubyJsonFormatException("Truncated \\u escape", i, 0, 0);
                    var cp = ParseHex4(bytes, i + 2);
                    i += 6;
                    if (cp is >= 0xD800 and <= 0xDBFF)
                    {
                        if (i + 6 > bytes.Length || bytes[i] != (byte)'\\' || bytes[i + 1] != (byte)'u')
                            throw new MRubyJsonFormatException("Unpaired surrogate in \\u escape", i, 0, 0);
                        var low = ParseHex4(bytes, i + 2);
                        if (low is < 0xDC00 or > 0xDFFF)
                            throw new MRubyJsonFormatException("Invalid low surrogate in \\u escape", i, 0, 0);
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                        i += 6;
                    }
                    AppendCodepointUtf8(buffer, cp);
                    break;
                }
                default:
                    throw new MRubyJsonFormatException("Unknown escape", i, 0, 0);
            }
        }
        return buffer.WrittenSpan.ToArray();
    }

    static void AppendByte(ArrayBufferWriter<byte> buffer, byte b)
    {
        var span = buffer.GetSpan(1);
        span[0] = b;
        buffer.Advance(1);
    }

    static int ParseHex4(ReadOnlySpan<byte> bytes, int offset)
    {
        return HexNibble(bytes[offset]) << 12
             | HexNibble(bytes[offset + 1]) << 8
             | HexNibble(bytes[offset + 2]) << 4
             | HexNibble(bytes[offset + 3]);
    }

    static int HexNibble(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9') return b - (byte)'0';
        if (b is >= (byte)'a' and <= (byte)'f') return 10 + (b - (byte)'a');
        if (b is >= (byte)'A' and <= (byte)'F') return 10 + (b - (byte)'A');
        throw new MRubyJsonFormatException("Bad hex digit in \\u escape", 0, 0, 0);
    }

    static void AppendCodepointUtf8(ArrayBufferWriter<byte> buffer, int cp)
    {
        if (cp < 0x80)
        {
            AppendByte(buffer, (byte)cp);
        }
        else if (cp < 0x800)
        {
            var span = buffer.GetSpan(2);
            span[0] = (byte)(0xC0 | (cp >> 6));
            span[1] = (byte)(0x80 | (cp & 0x3F));
            buffer.Advance(2);
        }
        else if (cp < 0x10000)
        {
            var span = buffer.GetSpan(3);
            span[0] = (byte)(0xE0 | (cp >> 12));
            span[1] = (byte)(0x80 | ((cp >> 6) & 0x3F));
            span[2] = (byte)(0x80 | (cp & 0x3F));
            buffer.Advance(3);
        }
        else
        {
            var span = buffer.GetSpan(4);
            span[0] = (byte)(0xF0 | (cp >> 18));
            span[1] = (byte)(0x80 | ((cp >> 12) & 0x3F));
            span[2] = (byte)(0x80 | ((cp >> 6) & 0x3F));
            span[3] = (byte)(0x80 | (cp & 0x3F));
            buffer.Advance(4);
        }
    }
}
