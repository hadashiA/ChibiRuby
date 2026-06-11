using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace ChibiRuby.StdLib;

/// <summary>Ruby <c>JSON</c> module (stdlib-compatible API) backed by <see cref="Utf8JsonTokenizer"/> / <see cref="Utf8JsonValueWriter"/>.</summary>
/// <remarks>
/// Numbers fitting Int64 parse as Integer, otherwise Float. Encoding dispatches
/// <c>obj.to_json</c> for non-builtin values and splices the result verbatim.
/// </remarks>
[RubyModule("JSON")]
static class JsonMembers
{
    /// <summary><c>JSON.parse(source, symbolize_names: false, max_nesting: 100, allow_nan: false)</c></summary>
    [RubyDef("(String, **untyped) -> untyped")]
    public static MRubyValue Parse(MRubyState mrb, MRubyValue self)
    {
        var sourceArg = mrb.GetArgumentAsStringAt(0);
        var symbolizeNames = ReadBoolKwarg(mrb, "symbolize_names"u8, false);
        var maxNesting = ReadIntKwarg(mrb, "max_nesting"u8, DefaultMaxNesting);
        var allowNan = ReadBoolKwarg(mrb, "allow_nan"u8, false);

        try
        {
            var tokenizer = new Utf8JsonTokenizer(sourceArg.AsSpan());
            var value = ReadValue(mrb, ref tokenizer, symbolizeNames, allowNan, maxNesting, depth: 0);
            tokenizer.EnsureEndOfInput();
            return value;
        }
        catch (MRubyJsonFormatException ex)
        {
            RaiseParserError(mrb, ex.Message);
            return MRubyValue.Nil; // unreachable
        }
    }

    /// <summary><c>JSON.generate(obj, max_nesting: 100, allow_nan: false, indent: nil)</c></summary>
    [RubyDef("(untyped, **untyped) -> String")]
    public static MRubyValue Generate(MRubyState mrb, MRubyValue self)
    {
        var obj = mrb.GetArgumentAt(0);
        var maxNesting = ReadIntKwarg(mrb, "max_nesting"u8, DefaultMaxNesting);
        var allowNan = ReadBoolKwarg(mrb, "allow_nan"u8, false);
        var indent = mrb.TryGetKeywordArgument(mrb.Intern("indent"u8), out var indentArg)
            && !indentArg.IsNil;

        return DoGenerate(mrb, obj, indent, allowNan, maxNesting);
    }

    /// <summary><c>JSON.generate</c> with two-space indentation.</summary>
    [RubyDef("(untyped, **untyped) -> String")]
    public static MRubyValue PrettyGenerate(MRubyState mrb, MRubyValue self)
    {
        var obj = mrb.GetArgumentAt(0);
        var maxNesting = ReadIntKwarg(mrb, "max_nesting"u8, DefaultMaxNesting);
        var allowNan = ReadBoolKwarg(mrb, "allow_nan"u8, false);
        return DoGenerate(mrb, obj, indent: true, allowNan, maxNesting);
    }

    /// <summary>Alias for <c>generate</c> (the IO-writing form is not supported).</summary>
    [RubyDef("(untyped) -> String")]
    public static MRubyValue Dump(MRubyState mrb, MRubyValue self)
    {
        var obj = mrb.GetArgumentAt(0);
        return DoGenerate(mrb, obj, indent: false, allowNan: false, DefaultMaxNesting);
    }

    /// <summary>Alias for <c>parse</c> (<c>create_additions:</c> is intentionally unsupported).</summary>
    [RubyDef("(String) -> untyped")]
    public static MRubyValue Load(MRubyState mrb, MRubyValue self) => Parse(mrb, self);

    // #to_json for builtin types, wired by DefineJson(). No [RubyDef]: the RBS
    // generator can't model one C# class augmenting multiple Ruby classes.

    public static MRubyValue HashToJson(MRubyState mrb, MRubyValue self) =>
        DoGenerate(mrb, self, indent: false, allowNan: false, DefaultMaxNesting);

    public static MRubyValue ArrayToJson(MRubyState mrb, MRubyValue self) =>
        DoGenerate(mrb, self, indent: false, allowNan: false, DefaultMaxNesting);

    public static MRubyValue StringToJson(MRubyState mrb, MRubyValue self) =>
        DoGenerate(mrb, self, indent: false, allowNan: false, DefaultMaxNesting);

    public static MRubyValue IntegerToJson(MRubyState mrb, MRubyValue self) =>
        DoGenerate(mrb, self, indent: false, allowNan: false, DefaultMaxNesting);

    public static MRubyValue FloatToJson(MRubyState mrb, MRubyValue self) =>
        DoGenerate(mrb, self, indent: false, allowNan: true, DefaultMaxNesting);

    public static MRubyValue TrueToJson(MRubyState mrb, MRubyValue self) =>
        new(mrb.NewString("true"u8));

    public static MRubyValue FalseToJson(MRubyState mrb, MRubyValue self) =>
        new(mrb.NewString("false"u8));

    public static MRubyValue NilToJson(MRubyState mrb, MRubyValue self) =>
        new(mrb.NewString("null"u8));

    public static MRubyValue SymbolToJson(MRubyState mrb, MRubyValue self) =>
        DoGenerate(mrb, self, indent: false, allowNan: false, DefaultMaxNesting);

    // ── parsing internals ──────────────────────────────────────────────

    const int DefaultMaxNesting = 100;

    static MRubyValue ReadValue(
        MRubyState mrb,
        ref Utf8JsonTokenizer tokenizer,
        bool symbolizeNames,
        bool allowNan,
        int maxNesting,
        int depth)
    {
        if (depth > maxNesting)
        {
            RaiseNestingError(mrb, depth);
        }

        switch (tokenizer.PeekTokenType())
        {
            case JsonTokenKind.Null:
                tokenizer.ReadNull();
                return MRubyValue.Nil;

            case JsonTokenKind.True:
            case JsonTokenKind.False:
                return tokenizer.ReadBoolean() ? MRubyValue.True : MRubyValue.False;

            case JsonTokenKind.String:
            {
                var raw = tokenizer.ReadStringRaw(out var hasEscape);
                return hasEscape
                    ? new MRubyValue(mrb.NewStringOwned(Utf8JsonTokenizer.DecodeEscapedUtf8(raw)))
                    : new MRubyValue(mrb.NewString(raw));
            }

            case JsonTokenKind.Number:
            {
                var raw = tokenizer.ReadNumberRaw();
                // Int64 first, Float on overflow/fraction. The full-consume check
                // rejects malformed runs like "1.2.3".
                if (Utf8Parser.TryParse(raw, out long i64, out var consumedL) && consumedL == raw.Length)
                {
                    return new MRubyValue(i64);
                }
                if (Utf8Parser.TryParse(raw, out double f, out var consumedD) && consumedD == raw.Length)
                {
                    return new MRubyValue(f);
                }
                tokenizer.ThrowFormatException($"Invalid number: {Encoding.UTF8.GetString(raw)}");
                return MRubyValue.Nil; // unreachable
            }

            case JsonTokenKind.NonFiniteNumber:
            {
                if (!allowNan)
                {
                    tokenizer.ThrowFormatException("NaN/Infinity not allowed (pass allow_nan: true to accept)");
                }
                return new MRubyValue(tokenizer.ReadNonFiniteNumber());
            }

            case JsonTokenKind.StartArray:
            {
                tokenizer.ReadStartArray();
                var arr = mrb.NewArray(0);
                while (!tokenizer.TryReadEndArray())
                {
                    var element = ReadValue(mrb, ref tokenizer, symbolizeNames, allowNan, maxNesting, depth + 1);
                    arr.Push(element);
                }
                return new MRubyValue(arr);
            }

            case JsonTokenKind.StartObject:
            {
                tokenizer.ReadStartObject();
                var hash = mrb.NewHash();
                while (tokenizer.TryReadPropertyName(out var rawName, out var nameHasEscape))
                {
                    MRubyValue key;
                    if (nameHasEscape)
                    {
                        var decoded = Utf8JsonTokenizer.DecodeEscapedUtf8(rawName);
                        key = symbolizeNames
                            ? new MRubyValue(mrb.Intern(decoded))
                            : new MRubyValue(mrb.NewStringOwned(decoded));
                    }
                    else
                    {
                        key = symbolizeNames
                            ? new MRubyValue(mrb.Intern(rawName))
                            : new MRubyValue(mrb.NewString(rawName));
                    }
                    var value = ReadValue(mrb, ref tokenizer, symbolizeNames, allowNan, maxNesting, depth + 1);
                    hash[key] = value;
                }
                return new MRubyValue(hash);
            }

            case JsonTokenKind.EndOfInput:
            default:
                tokenizer.ThrowFormatException("Unexpected end of input");
                return MRubyValue.Nil; // unreachable
        }
    }

    // ── generation internals ───────────────────────────────────────────

    static MRubyValue DoGenerate(MRubyState mrb, MRubyValue value, bool indent, bool allowNan, int maxNesting)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonValueWriter(buffer, indent);
        WriteValue(mrb, ref writer, value, allowNan, maxNesting, depth: 0);
        return new MRubyValue(mrb.NewStringOwned(buffer.WrittenSpan.ToArray()));
    }

    static void WriteValue(
        MRubyState mrb,
        ref Utf8JsonValueWriter writer,
        MRubyValue value,
        bool allowNan,
        int maxNesting,
        int depth)
    {
        if (depth > maxNesting)
        {
            RaiseNestingError(mrb, depth);
        }

        switch (value.VType)
        {
            case MRubyVType.Nil:
                writer.WriteNull();
                return;

            case MRubyVType.True:
                writer.WriteBoolean(true);
                return;

            case MRubyVType.False:
                writer.WriteBoolean(false);
                return;

            case MRubyVType.Integer:
                writer.WriteInt64(value.IntegerValue);
                return;

            case MRubyVType.Float:
            {
                var f = value.FloatValue;
                if (double.IsNaN(f) || double.IsInfinity(f))
                {
                    if (!allowNan)
                    {
                        RaiseGeneratorError(mrb, $"non-finite Float in object to be serialized: {f}");
                    }
                    writer.WriteRaw(double.IsNaN(f) ? "NaN"u8
                        : double.IsPositiveInfinity(f) ? "Infinity"u8
                        : "-Infinity"u8);
                    return;
                }
                writer.WriteDouble(f);
                return;
            }

            case MRubyVType.Symbol:
                writer.WriteString(mrb.NameOf(value.SymbolValue).AsSpan());
                return;
        }

        // Reference types
        switch (value.Object)
        {
            case RString s:
                writer.WriteString(s.AsSpan());
                return;

            case RArray arr:
            {
                writer.WriteStartArray();
                for (var i = 0; i < arr.Length; i++)
                {
                    WriteValue(mrb, ref writer, arr[i], allowNan, maxNesting, depth + 1);
                }
                writer.WriteEndArray();
                return;
            }

            case RHash hash:
            {
                writer.WriteStartObject();
                foreach (var entry in hash)
                {
                    WritePropertyName(mrb, ref writer, entry.Key);
                    WriteValue(mrb, ref writer, entry.Value, allowNan, maxNesting, depth + 1);
                }
                writer.WriteEndObject();
                return;
            }
        }

        // Fallback: splice the object's own #to_json output verbatim.
        var rendered = mrb.Send(value, mrb.Intern("to_json"u8));
        if (rendered.Object is not RString renderedString)
        {
            RaiseGeneratorError(mrb, $"#{mrb.ClassNameOf(value)}#to_json did not return a String");
            return;
        }
        writer.WriteRaw(renderedString.AsSpan());
    }

    static void WritePropertyName(MRubyState mrb, ref Utf8JsonValueWriter writer, MRubyValue key)
    {
        if (key.Object is RString s)
        {
            writer.WritePropertyName(s.AsSpan());
            return;
        }
        if (key.VType == MRubyVType.Symbol)
        {
            writer.WritePropertyName(mrb.NameOf(key.SymbolValue).AsSpan());
            return;
        }
        RaiseGeneratorError(mrb, "JSON object keys must be String or Symbol"u8);
    }

    // ── option-reading helpers ─────────────────────────────────────────

    static bool ReadBoolKwarg(MRubyState mrb, ReadOnlySpan<byte> name, bool fallback)
    {
        if (mrb.TryGetKeywordArgument(mrb.Intern(name), out var value))
        {
            return value.Truthy;
        }
        return fallback;
    }

    static int ReadIntKwarg(MRubyState mrb, ReadOnlySpan<byte> name, int fallback)
    {
        if (!mrb.TryGetKeywordArgument(mrb.Intern(name), out var value))
        {
            return fallback;
        }
        if (value.IsNil) return fallback;
        if (!value.IsInteger)
        {
            mrb.Raise(Names.TypeError, $"{Encoding.UTF8.GetString(name)}: must be Integer");
        }
        var i = value.IntegerValue;
        if (i < 0 || i > int.MaxValue)
        {
            mrb.Raise(Names.ArgumentError, $"{Encoding.UTF8.GetString(name)}: out of range");
        }
        return (int)i;
    }

    // ── error raising ──────────────────────────────────────────────────

    internal static RClass GetParserErrorClass(MRubyState mrb) =>
        GetJsonErrorClass(mrb, "ParserError"u8);

    internal static RClass GetGeneratorErrorClass(MRubyState mrb) =>
        GetJsonErrorClass(mrb, "GeneratorError"u8);

    internal static RClass GetNestingErrorClass(MRubyState mrb) =>
        GetJsonErrorClass(mrb, "NestingError"u8);

    static RClass GetJsonErrorClass(MRubyState mrb, ReadOnlySpan<byte> name)
    {
        var jsonModule = mrb.GetConst(mrb.Intern("JSON"u8)).As<RClass>();
        return mrb.GetConst(mrb.Intern(name), jsonModule).As<RClass>();
    }

    static void RaiseParserError(MRubyState mrb, string message)
    {
        mrb.Raise(GetParserErrorClass(mrb), mrb.NewString(message ?? ""));
    }

    static void RaiseGeneratorError(MRubyState mrb, ReadOnlySpan<byte> message)
    {
        mrb.Raise(GetGeneratorErrorClass(mrb), message);
    }

    static void RaiseGeneratorError(MRubyState mrb, string message)
    {
        mrb.Raise(GetGeneratorErrorClass(mrb), mrb.NewString(message ?? ""));
    }

    static void RaiseNestingError(MRubyState mrb, int depth)
    {
        mrb.Raise(GetNestingErrorClass(mrb), mrb.NewString($"nesting too deep: {depth}"));
    }
}
