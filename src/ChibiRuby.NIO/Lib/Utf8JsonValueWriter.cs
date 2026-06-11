using System;
using System.Buffers;
using System.Buffers.Text;

namespace ChibiRuby.NIO;

/// <summary>JSON writer over <see cref="ArrayBufferWriter{T}"/>; comma placement is handled by a single separator flag.</summary>
/// <remarks>Non-ASCII UTF-8 passes through verbatim — only quotes, backslash, and control chars are escaped.</remarks>
internal struct Utf8JsonValueWriter(ArrayBufferWriter<byte> buffer, bool indented)
{
    readonly ArrayBufferWriter<byte> buffer = buffer;
    readonly bool indented = indented;
    int depth = 0;
    bool needsSeparator = false;

    public void WriteStartObject()
    {
        BeginValue();
        AppendByte((byte)'{');
        depth++;
        needsSeparator = false;
    }

    public void WriteEndObject()
    {
        depth--;
        // needsSeparator is false right after the opening brace, so `{}` stays collapsed.
        if (indented && needsSeparator) NewlineIndent();
        AppendByte((byte)'}');
        needsSeparator = true;
    }

    public void WriteStartArray()
    {
        BeginValue();
        AppendByte((byte)'[');
        depth++;
        needsSeparator = false;
    }

    public void WriteEndArray()
    {
        depth--;
        if (indented && needsSeparator) NewlineIndent();
        AppendByte((byte)']');
        needsSeparator = true;
    }

    public void WritePropertyName(ReadOnlySpan<byte> nameUtf8)
    {
        BeginValue();
        WriteStringBytes(nameUtf8);
        AppendByte((byte)':');
        if (indented) AppendByte((byte)' ');
        needsSeparator = false;
    }

    public void WriteString(ReadOnlySpan<byte> utf8)
    {
        BeginValue();
        WriteStringBytes(utf8);
        needsSeparator = true;
    }

    public void WriteNull()
    {
        BeginValue();
        Append("null"u8);
        needsSeparator = true;
    }

    public void WriteBoolean(bool value)
    {
        BeginValue();
        Append(value ? "true"u8 : "false"u8);
        needsSeparator = true;
    }

    public void WriteInt64(long value)
    {
        BeginValue();
        Span<byte> tmp = stackalloc byte[24];
        Utf8Formatter.TryFormat(value, tmp, out var written);
        Append(tmp.Slice(0, written));
        needsSeparator = true;
    }

    public void WriteDouble(double value)
    {
        BeginValue();
        Span<byte> tmp = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, tmp, out var written);
        Append(tmp.Slice(0, written));
        needsSeparator = true;
    }

    /// <summary>Splices pre-rendered JSON verbatim, without validation.</summary>
    public void WriteRaw(ReadOnlySpan<byte> rawJson)
    {
        BeginValue();
        Append(rawJson);
        needsSeparator = true;
    }

    // ── internals ───────────────────────────────────────────────────────

    void BeginValue()
    {
        if (needsSeparator)
        {
            AppendByte((byte)',');
            if (indented) NewlineIndent();
        }
        else if (indented && depth > 0)
        {
            NewlineIndent();
        }
    }

    void NewlineIndent()
    {
        AppendByte((byte)'\n');
        for (var i = 0; i < depth; i++)
        {
            Append("  "u8);
        }
    }

    void WriteStringBytes(ReadOnlySpan<byte> utf8)
    {
        AppendByte((byte)'"');
        var safeStart = 0;
        for (var i = 0; i < utf8.Length; i++)
        {
            var b = utf8[i];
            if (b != (byte)'"' && b != (byte)'\\' && b >= 0x20) continue;

            if (i > safeStart) Append(utf8.Slice(safeStart, i - safeStart));
            switch (b)
            {
                case (byte)'"': Append("\\\""u8); break;
                case (byte)'\\': Append("\\\\"u8); break;
                case (byte)'\b': Append("\\b"u8); break;
                case (byte)'\f': Append("\\f"u8); break;
                case (byte)'\n': Append("\\n"u8); break;
                case (byte)'\r': Append("\\r"u8); break;
                case (byte)'\t': Append("\\t"u8); break;
                default:
                {
                    Span<byte> esc = stackalloc byte[6];
                    "\\u00"u8.CopyTo(esc);
                    esc[4] = HexDigit(b >> 4);
                    esc[5] = HexDigit(b & 0xF);
                    Append(esc);
                    break;
                }
            }
            safeStart = i + 1;
        }
        if (utf8.Length > safeStart) Append(utf8.Slice(safeStart));
        AppendByte((byte)'"');
    }

    static byte HexDigit(int v) => (byte)(v < 10 ? '0' + v : 'a' + (v - 10));

    void AppendByte(byte b)
    {
        var span = buffer.GetSpan(1);
        span[0] = b;
        buffer.Advance(1);
    }

    void Append(ReadOnlySpan<byte> bytes)
    {
        var span = buffer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        buffer.Advance(bytes.Length);
    }
}
