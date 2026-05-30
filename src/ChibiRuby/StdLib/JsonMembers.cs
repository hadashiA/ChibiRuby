using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ChibiRuby.StdLib;

/// <summary>
/// <c>JSON</c> module. Mirrors the API of Ruby's bundled <c>json</c>
/// stdlib (<c>JSON.parse</c> / <c>JSON.generate</c> / <c>JSON.pretty_generate</c>
/// / <c>JSON.dump</c> / <c>JSON.load</c>), backed by <c>System.Text.Json</c>'s
/// low-level <see cref="Utf8JsonReader"/> / <see cref="Utf8JsonWriter"/>.
/// <para>
/// Type mapping on <b>parse</b>:
/// <c>null</c>→<c>nil</c>, <c>true</c>/<c>false</c>→bool, integer fitting
/// <c>Int64</c>→<c>Integer</c> (overflow falls back to <c>Float</c>, matching
/// no-DoS-on-bignum behaviour), other number→<c>Float</c>, string→<c>String</c>,
/// array→<c>Array</c>, object→<c>Hash</c> (String keys by default,
/// Symbol keys when <c>symbolize_names: true</c>).
/// </para>
/// <para>
/// Type mapping on <b>generate</b>: same direction, with Symbols emitted as
/// strings. For any other type, <c>obj.to_json</c> is dispatched via
/// <see cref="MRubyState.Send"/>; the returned String is spliced into the
/// output verbatim. Standard builtins (<c>Hash</c>/<c>Array</c>/<c>String</c>/
/// <c>Integer</c>/<c>Float</c>/<c>TrueClass</c>/<c>FalseClass</c>/
/// <c>NilClass</c>/<c>Symbol</c>) gain a native <c>#to_json</c> defined by
/// this module.
/// </para>
/// <para>
/// Errors: <c>JSON::ParserError</c> on malformed input or
/// <c>max_nesting</c> overflow on parse; <c>JSON::GeneratorError</c> on
/// non-finite floats (when <c>allow_nan: false</c>), unsupported value
/// types, or <c>max_nesting</c> overflow on generate.
/// </para>
/// </summary>
[RubyModule("JSON")]
static class JsonMembers
{
    /// <summary>
    /// <c>JSON.parse(source, symbolize_names: false, max_nesting: 100,
    /// allow_nan: false)</c> — decode a JSON document to Ruby values.
    /// Raises <c>JSON::ParserError</c> on malformed input.
    /// </summary>
    [RubyDef("(String, **untyped) -> untyped")]
    public static MRubyValue Parse(MRubyState mrb, MRubyValue self)
    {
        var sourceArg = mrb.GetArgumentAsStringAt(0);
        var symbolizeNames = ReadBoolKwarg(mrb, "symbolize_names"u8, false);
        var maxNesting = ReadIntKwarg(mrb, "max_nesting"u8, DefaultMaxNesting);
        var allowNan = ReadBoolKwarg(mrb, "allow_nan"u8, false);

        var options = new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            // Utf8JsonReader's max depth caps below ours when small; pass a
            // big enough value so our explicit counter is the authoritative
            // gate (so we can raise JSON::NestingError ourselves rather than
            // a generic JsonException).
            MaxDepth = Math.Max(64, maxNesting + 16),
        };

        var reader = new Utf8JsonReader(sourceArg.AsSpan(), options);
        try
        {
            if (!reader.Read())
            {
                RaiseParserError(mrb, "unexpected end of input"u8);
            }
            var value = ReadValue(mrb, ref reader, symbolizeNames, allowNan, maxNesting, depth: 0);
            // Reject trailing tokens after the top-level value; flori/json
            // raises on `'{"a":1}garbage'`.
            if (reader.Read())
            {
                RaiseParserError(mrb, "unexpected token after JSON document"u8);
            }
            return value;
        }
        catch (JsonException ex)
        {
            RaiseParserError(mrb, ex.Message);
            return MRubyValue.Nil; // unreachable
        }
    }

    /// <summary>
    /// <c>JSON.generate(obj, max_nesting: 100, allow_nan: false,
    /// indent: nil)</c> — encode a Ruby value as JSON. With a non-nil
    /// <c>indent:</c> the output is pretty-printed (two-space indent;
    /// custom indent strings are coerced to default for v1).
    /// </summary>
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

    /// <summary>
    /// <c>JSON.pretty_generate(obj, max_nesting: 100, allow_nan: false)</c>
    /// — convenience for <c>generate(obj, indent: "  ")</c>. The output is
    /// human-readable with two-space indentation.
    /// </summary>
    [RubyDef("(untyped, **untyped) -> String")]
    public static MRubyValue PrettyGenerate(MRubyState mrb, MRubyValue self)
    {
        var obj = mrb.GetArgumentAt(0);
        var maxNesting = ReadIntKwarg(mrb, "max_nesting"u8, DefaultMaxNesting);
        var allowNan = ReadBoolKwarg(mrb, "allow_nan"u8, false);
        return DoGenerate(mrb, obj, indent: true, allowNan, maxNesting);
    }

    /// <summary>
    /// <c>JSON.dump(obj)</c> — alias for <c>generate</c>. The IO-writing
    /// form (<c>JSON.dump(obj, io)</c>) is not supported in v1; write
    /// <c>JSON.generate(obj)</c> to an IO manually instead.
    /// </summary>
    [RubyDef("(untyped) -> String")]
    public static MRubyValue Dump(MRubyState mrb, MRubyValue self)
    {
        var obj = mrb.GetArgumentAt(0);
        return DoGenerate(mrb, obj, indent: false, allowNan: false, DefaultMaxNesting);
    }

    /// <summary>
    /// <c>JSON.load(source)</c> — alias for <c>parse</c>. The MRI extension
    /// that instantiates arbitrary classes via <c>create_additions:</c> is
    /// intentionally not supported (deserialization-RCE class of bug).
    /// </summary>
    [RubyDef("(String) -> untyped")]
    public static MRubyValue Load(MRubyState mrb, MRubyValue self) => Parse(mrb, self);

    // ── to_json instance methods on builtin types ──────────────────────
    // These are defined here (not in HashMembers/ArrayMembers/etc.) so the
    // whole JSON surface is colocated. Wired up to the right classes by
    // ChibiRubyState.DefineJson(). Not [RubyDef]-tagged because the
    // current RBS generator only emits methods whose declaring C# class
    // matches the Ruby class being documented; this file augments multiple
    // Ruby classes from one C# class, which the generator can't model.

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
        ref Utf8JsonReader reader,
        bool symbolizeNames,
        bool allowNan,
        int maxNesting,
        int depth)
    {
        if (depth > maxNesting)
        {
            RaiseNestingError(mrb, depth);
        }

        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return MRubyValue.Nil;

            case JsonTokenType.True:
                return MRubyValue.True;

            case JsonTokenType.False:
                return MRubyValue.False;

            case JsonTokenType.String:
            {
                var bytes = reader.HasValueSequence
                    ? reader.ValueSequence.ToArray()
                    : reader.ValueSpan.ToArray();
                // ValueSpan holds the encoded form; for strings without
                // escapes it equals the unescaped UTF-8 already. For escaped
                // strings, Utf8JsonReader.GetString() decodes correctly.
                return new MRubyValue(mrb.NewString(reader.GetString() ?? ""));
            }

            case JsonTokenType.Number:
            {
                // Prefer Int64 to keep Ruby Integer-ness; fall back to Float
                // on overflow or non-integer numbers. This silently widens
                // bignums to Float — acceptable for v1, documented in the
                // module summary.
                if (reader.TryGetInt64(out var i64))
                {
                    return new MRubyValue(i64);
                }
                var f = reader.GetDouble();
                if (!allowNan && (double.IsNaN(f) || double.IsInfinity(f)))
                {
                    // Reader shouldn't produce these from strict-JSON input,
                    // but guard anyway.
                    RaiseParserError(mrb, "non-finite number in JSON"u8);
                }
                return new MRubyValue(f);
            }

            case JsonTokenType.StartArray:
            {
                var arr = mrb.NewArray(0);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var element = ReadValue(mrb, ref reader, symbolizeNames, allowNan, maxNesting, depth + 1);
                    arr.Push(element);
                }
                return new MRubyValue(arr);
            }

            case JsonTokenType.StartObject:
            {
                var hash = mrb.NewHash();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        RaiseParserError(mrb, "expected property name"u8);
                    }
                    var keyString = reader.GetString() ?? "";
                    MRubyValue key = symbolizeNames
                        ? new MRubyValue(mrb.Intern(keyString))
                        : new MRubyValue(mrb.NewString(keyString));

                    if (!reader.Read())
                    {
                        RaiseParserError(mrb, "expected value after property name"u8);
                    }
                    var value = ReadValue(mrb, ref reader, symbolizeNames, allowNan, maxNesting, depth + 1);
                    hash[key] = value;
                }
                return new MRubyValue(hash);
            }

            default:
                RaiseParserError(mrb, $"unexpected JSON token: {reader.TokenType}");
                return MRubyValue.Nil; // unreachable
        }
    }

    // ── generation internals ───────────────────────────────────────────

    static MRubyValue DoGenerate(MRubyState mrb, MRubyValue value, bool indent, bool allowNan, int maxNesting)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions
        {
            Indented = indent,
            // We do our own escaping decisions; this stays at default
            // (Strict, which rejects invalid UTF-8 → JsonException).
            SkipValidation = false,
        };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            WriteValue(mrb, writer, value, allowNan, maxNesting, depth: 0);
            writer.Flush();
        }
        return new MRubyValue(mrb.NewStringOwned(buffer.WrittenSpan.ToArray()));
    }

    static void WriteValue(
        MRubyState mrb,
        Utf8JsonWriter writer,
        MRubyValue value,
        bool allowNan,
        int maxNesting,
        int depth)
    {
        if (depth > maxNesting)
        {
            RaiseGeneratorNestingError(mrb, depth);
        }

        switch (value.VType)
        {
            case MRubyVType.Nil:
                writer.WriteNullValue();
                return;

            case MRubyVType.True:
                writer.WriteBooleanValue(true);
                return;

            case MRubyVType.False:
                writer.WriteBooleanValue(false);
                return;

            case MRubyVType.Integer:
                writer.WriteNumberValue(value.IntegerValue);
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
                    // Non-standard tokens, matching flori/json's allow_nan
                    // behaviour. WriteRawValue lets us bypass Utf8JsonWriter's
                    // built-in rejection of NaN/Infinity.
                    var token = double.IsNaN(f) ? "NaN"
                        : double.IsPositiveInfinity(f) ? "Infinity"
                        : "-Infinity";
                    writer.WriteRawValue(token, skipInputValidation: true);
                    return;
                }
                writer.WriteNumberValue(f);
                return;
            }

            case MRubyVType.Symbol:
            {
                var name = mrb.NameOf(value.SymbolValue).AsSpan();
                writer.WriteStringValue(name);
                return;
            }
        }

        // Reference types
        switch (value.Object)
        {
            case RString s:
                writer.WriteStringValue(s.AsSpan());
                return;

            case RArray arr:
            {
                writer.WriteStartArray();
                for (var i = 0; i < arr.Length; i++)
                {
                    WriteValue(mrb, writer, arr[i], allowNan, maxNesting, depth + 1);
                }
                writer.WriteEndArray();
                return;
            }

            case RHash hash:
            {
                writer.WriteStartObject();
                foreach (var entry in hash)
                {
                    WritePropertyName(mrb, writer, entry.Key);
                    WriteValue(mrb, writer, entry.Value, allowNan, maxNesting, depth + 1);
                }
                writer.WriteEndObject();
                return;
            }
        }

        // Fallback: ask the object to render itself via #to_json. We splice
        // the returned String into the output as raw tokens so user-defined
        // `to_json` implementations can produce any valid JSON fragment
        // (object, array, number, …). If `to_json` returns something other
        // than a String, GeneratorError.
        var toJsonSym = mrb.Intern("to_json"u8);
        var rendered = mrb.Send(value, toJsonSym);
        if (rendered.Object is not RString renderedString)
        {
            RaiseGeneratorError(mrb, $"#{mrb.ClassNameOf(value)}#to_json did not return a String");
            return;
        }
        writer.WriteRawValue(renderedString.AsSpan(), skipInputValidation: true);
    }

    static void WritePropertyName(MRubyState mrb, Utf8JsonWriter writer, MRubyValue key)
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

    static void RaiseParserError(MRubyState mrb, ReadOnlySpan<byte> message)
    {
        mrb.Raise(GetParserErrorClass(mrb), message);
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

    static void RaiseGeneratorNestingError(MRubyState mrb, int depth)
    {
        // CRuby raises NestingError on both directions; we mirror that even
        // though NestingError < ParserError (NestingError covers depth on
        // either side semantically).
        mrb.Raise(GetNestingErrorClass(mrb), mrb.NewString($"nesting too deep: {depth}"));
    }
}
