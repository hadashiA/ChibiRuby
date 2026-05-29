using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ChibiRuby.Internals;

namespace ChibiRuby.StdLib;

/// <summary>
/// Mutable sequence of bytes, usually interpreted as UTF-8 text. Indexing
/// returns substrings or single characters; many methods come in pairs of
/// pure (<c>upcase</c>) and in-place (<c>upcase!</c>) variants. Includes
/// <c>Comparable</c>. String literals are frozen by some host configurations
/// but mutable by default.
/// </summary>
[RubyClass("String")]
static class StringMembers
{
    /// <summary>
    /// Initializes a new <c>String</c>, optionally copying the contents of the given string.
    /// </summary>
    /// <example>
    /// <code>
    /// String.new            # => ""
    /// String.new("hello")   # => "hello"
    /// </code>
    /// </example>
    [RubyDef("(?String) -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        if (state.TryGetArgumentAt(0, out var arg))
        {
            if (arg.Object is RString other)
            {
                var str = self.As<RString>();
                other.CopyTo(str);
            }
            else
            {
                state.Raise(Names.TypeError, $"{state.StringifyAny(arg)} cannot be converted to String");
            }
        }
        return self;
    }

    /// <summary>
    /// Replaces the contents of <c>self</c> with a copy of the given string. Called by <c>dup</c> and <c>clone</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc".dup   # => "abc"
    /// </code>
    /// </example>
    [RubyDef("(String) -> self")]
    public static MRubyValue InitializeCopy(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var other = state.GetArgumentAsStringAt(0);
        other.CopyTo(str);
        return self;
    }

    /// <summary>
    /// Returns the <c>Symbol</c> corresponding to <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "foo".intern    # => :foo
    /// </code>
    /// </example>
    [RubyDef("() -> Symbol")]
    public static MRubyValue Intern(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        return state.Intern(str);
    }

    /// <summary>
    /// Replaces the contents of <c>self</c> with the contents of the given string and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s.replace("world")   # => "world"
    /// </code>
    /// </example>
    [RubyDef("(String) -> self")]
    public static MRubyValue Replace(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var other = state.GetArgumentAsStringAt(0);
        other.CopyTo(str);
        return self;
    }

    /// <summary>
    /// Returns a quoted, escaped representation of <c>self</c> suitable for debugging output.
    /// </summary>
    /// <example>
    /// <code>
    /// "hi\n".inspect    # => "\"hi\\n\""
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var output = ArrayPool<byte>.Shared.Rent(str.Length * 2 + 2);

        int written;
        while (!NamingRule.TryEscape(str.AsSpan(), true, output, out written))
        {
            ArrayPool<byte>.Shared.Return(output);
            output = ArrayPool<byte>.Shared.Rent(output.Length * 2);
        }
        ArrayPool<byte>.Shared.Return(output);

        return state.NewString(output.AsSpan(0, written));
    }

    /// <summary>
    /// Equality <c>==</c>. Returns <c>true</c> when the argument is a <c>String</c> with the same byte content.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc" == "abc"   # => true
    /// "abc" == "abd"   # => false
    /// "abc" == :abc    # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (other.Object is RString otherString)
        {
            return self.As<RString>().Equals(otherString);
        }
        return MRubyValue.False;
    }

    /// <summary>
    /// Comparison <c>&lt;=&gt;</c>. Returns -1, 0, or 1 by comparing byte content with the given string, or <c>nil</c> for non-strings.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc" &lt;=&gt; "abd"    # => -1
    /// "abc" &lt;=&gt; "abc"    # => 0
    /// "abc" &lt;=&gt; 42       # => nil
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue OpCmp(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (other.Object is RString otherStr)
        {
            var str = self.As<RString>();
            return str.CompareTo(otherStr);
        }
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Element reference <c>[]</c>. Returns the substring at the given index, range, or <c>(start, length)</c>, or <c>nil</c> when out of range.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello"[0]      # => "h"
    /// "hello"[1, 3]   # => "ell"
    /// "hello"[1..3]   # => "ell"
    /// </code>
    /// </example>
    [RubyDef("(Integer | Range[Integer]) -> String? | (Integer, Integer) -> String?")]
    public static MRubyValue OpAref(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();

        var indexValue = state.GetArgumentAt(0);
        var rangeLength = default(int?);
        if (state.TryGetArgumentAt(1, out var arg1))
        {
            rangeLength = (int)state.AsInteger(arg1);
        }

        var result = str.GetPartial(state, indexValue, rangeLength);
        return result != null ? new MRubyValue(result) : MRubyValue.Nil;
    }

    /// <summary>
    /// Element assignment <c>[]=</c>. Replaces the substring at the given index, range, or <c>(start, length)</c> with the given string.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s[0] = "H"        # s == "Hello"
    /// s[1, 3] = "i!"    # s == "Hi!o"
    /// </code>
    /// </example>
    [RubyDef("(Integer, String) -> String | (Integer, Integer, String) -> String | (Range[Integer], String) -> String")]
    public static MRubyValue OpAset(MRubyState state, MRubyValue self)
    {
        MRubyValue index;
        RString? value;
        int? rangeLength = null;
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 2:
                index = state.GetArgumentAt(0);
                value = state.GetArgumentAsStringAt(1);
                break;
            case 3:
                index = state.GetArgumentAt(0);
                rangeLength = (int)state.GetArgumentAsIntegerAt(1);
                value = state.GetArgumentAsStringAt(2);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 2, 3);
                return MRubyValue.Nil;
        }

        var str = self.As<RString>();
        str.SetPartial(state, index, rangeLength, value);
        return value;
    }

    /// <summary>
    /// Returns the <c>Symbol</c> corresponding to <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "foo".to_sym    # => :foo
    /// </code>
    /// </example>
    [RubyDef("() -> Symbol")]
    public static MRubyValue ToSym(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        return state.Intern(str.AsSpan());
    }

    /// <summary>
    /// Returns <c>self</c> (or a duplicate of <c>self</c> when called on a plain <c>String</c>).
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".to_s     # => "hello"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        if (state.ClassOf(self) == state.StringClass)
        {
            return self.As<RString>().Dup();
        }
        return self;
    }

    /// <summary>
    /// Parses the leading integer in <c>self</c> using the given radix (2, 8, 10, or 16; default 10). Returns 0 when no number can be parsed.
    /// </summary>
    /// <example>
    /// <code>
    /// "42abc".to_i      # => 42
    /// "ff".to_i(16)     # => 255
    /// "1010".to_i(2)    # => 10
    /// "abc".to_i        # => 0
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Integer")]
    public static MRubyValue ToI(MRubyState state, MRubyValue self)
    {
        var source = self.As<RString>().AsSpan();

        var format = 'g';
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            var basis = state.AsInteger(arg0);
            switch (basis)
            {
                case 2:
                    format = 'b';
                    break;
                case 8:
                    format = 'o';
                    break;
                case 16:
                    format = 'x';
                    break;
                case 10:
                    format = 'g';
                    break;
                default:
                    state.Raise(Names.ArgumentError, $"invalid radix {basis}");
                    format = default;
                    break;
            }
        }

        bool result;
        long value;
        if (source.Length > 64)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
            AsciiCode.PrepareNumber(source, buffer);
            result = format == 'b'
                ? AsciiCode.TryParseBinary(buffer, out value)
                : Utf8Parser.TryParse(buffer, out value, out var consumed, format);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        else
        {
            Span<byte> buffer = stackalloc byte[source.Length];
            AsciiCode.PrepareNumber(source, buffer);
            result = format == 'b'
                ? AsciiCode.TryParseBinary(buffer, out value)
                : Utf8Parser.TryParse(buffer, out value, out var consumed, format);
        }
        return result ? value : 0;
    }

    /// <summary>
    /// Parses the leading floating-point number in <c>self</c> and returns it. Returns <c>0.0</c> when no number can be parsed.
    /// </summary>
    /// <example>
    /// <code>
    /// "3.14".to_f      # => 3.14
    /// "1e2".to_f       # => 100.0
    /// "abc".to_f       # => 0.0
    /// </code>
    /// </example>
    [RubyDef("() -> Float")]
    public static MRubyValue ToF(MRubyState state, MRubyValue self)
    {
        var source = self.As<RString>().AsSpan();

        bool result;
        double value;
        if (source.Length > 64)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
            AsciiCode.PrepareNumber(source, buffer);
            ArrayPool<byte>.Shared.Return(buffer);
            result = Utf8Parser.TryParse(buffer, out value, out var consumed, 'g');
        }
        else
        {
            Span<byte> buffer = stackalloc byte[source.Length];
            AsciiCode.PrepareNumber(source, buffer);
            result = Utf8Parser.TryParse(buffer, out value, out var consumed, 'g');
        }

        return result ? value : 0;
    }

    /// <summary>
    /// Returns the number of characters (UTF-8 code points) in <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".size     # => 5
    /// "".size          # => 0
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        return Encoding.UTF8.GetCharCount(str.AsSpan());
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> has zero length, <c>false</c> otherwise.
    /// </summary>
    /// <example>
    /// <code>
    /// "".empty?       # => true
    /// "x".empty?      # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue Empty(MRubyState state, MRubyValue self)
    {
        return self.As<RString>().Length <= 0;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> contains the given substring.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".include?("ell")   # => true
    /// "hello".include?("xyz")   # => false
    /// </code>
    /// </example>
    [RubyDef("(String) -> bool")]
    public static MRubyValue Include(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var v = state.GetArgumentAsStringAt(0);
        var i = str.AsSpan().IndexOf(v.AsSpan());
        return i >= 0;
    }

    /// <summary>
    /// Returns the index of the first occurrence of the given substring (optionally starting at the given offset), or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".index("l")        # => 2
    /// "hello".index("l", 3)     # => 3
    /// "hello".index("x")        # => nil
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> Integer?")]
    public static MRubyValue Index(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var argc = state.GetArgumentCount();

        RString target = default!;
        var pos = 0;
        switch (argc)
        {
            case 1:
                target = state.GetArgumentAsStringAt(0);
                break;
            case 2:
                target = state.GetArgumentAsStringAt(0);
                pos = (int)state.GetArgumentAsIntegerAt(1);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                break;
        }
        var result = str.IndexOf(target, pos);
        return result < 0 ? MRubyValue.Nil : new MRubyValue(result);
    }

    /// <summary>
    /// Returns the index of the last occurrence of the given substring (optionally ending at the given offset), or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".rindex("l")       # => 3
    /// "hello".rindex("x")       # => nil
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> Integer?")]
    public static MRubyValue RIndex(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var argc = state.GetArgumentCount();

        RString target = default!;
        var pos = str.Length;
        switch (argc)
        {
            case 1:
                target = state.GetArgumentAsStringAt(0);
                break;
            case 2:
                target = state.GetArgumentAsStringAt(0);
                pos = (int)state.GetArgumentAsIntegerAt(1);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                break;
        }
        var result = str.LstIndexOf(target, pos);
        return result < 0 ? MRubyValue.Nil : result;
    }

    /// <summary>
    /// Repetition <c>*</c>. Returns a new string built by concatenating <c>self</c> the given number of times.
    /// </summary>
    /// <example>
    /// <code>
    /// "ab" * 3      # => "ababab"
    /// "x" * 0       # => ""
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> String")]

    public static MRubyValue Times(MRubyState state, MRubyValue self)
    {
        var n = state.GetArgumentAsIntegerAt(0);
        if (n < 0)
        {
            state.Raise(Names.ArgumentError, "negative argument"u8);
        }

        var str = self.As<RString>();
        var newLength = str.Length * n;
        var buffer = new byte[newLength];
        var result = state.NewStringOwned(buffer);

        var src = str.AsSpan();
        var dst = buffer.AsSpan();
        for (var i = 0; i < n; i++)
        {
            src.CopyTo(dst);
            dst = dst[src.Length..];
        }
        return result;
    }

    /// <summary>
    /// Returns a copy of <c>self</c> with the first character converted to uppercase and the rest to lowercase.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".capitalize    # => "Hello"
    /// "HELLO".capitalize    # => "Hello"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]

    public static MRubyValue Capitalize(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Capitalize();
        return result;
    }

    /// <summary>
    /// Capitalizes <c>self</c> in place. Returns <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s.capitalize!   # => nil
    /// s               # => "Hello"
    /// </code>
    /// </example>
    [RubyDef("() -> self?")]

    public static MRubyValue CapitalizeBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);

        str.Capitalize();
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns a copy of <c>self</c> with the trailing record separator (default <c>"\n"</c> or <c>"\r\n"</c>) removed.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello\n".chomp        # => "hello"
    /// "hello\r\n".chomp      # => "hello"
    /// "hello".chomp("lo")    # => "hel"
    /// </code>
    /// </example>
    [RubyDef("(?String) -> String")]
    public static MRubyValue Chomp(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var result = str.Dup();
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            state.EnsureValueType(arg0, MRubyVType.String);
            var paragraph = arg0.As<RString>();
            result.Chomp(paragraph.AsSpan());
        }
        else
        {
            result.Chomp();
        }
        return result;
    }

    /// <summary>
    /// Removes the trailing record separator from <c>self</c> in place. Returns <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello\n"
    /// s.chomp!     # => nil
    /// s            # => "hello"
    /// </code>
    /// </example>
    [RubyDef("(?String) -> self?")]
    public static MRubyValue ChompBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);

        if (state.TryGetArgumentAt(0, out var arg0))
        {
            state.EnsureValueType(arg0, MRubyVType.String);
            var paragraph = arg0.As<RString>();
            str.Chomp(paragraph.AsSpan());
        }
        else
        {
            str.Chomp();
        }
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns a copy of <c>self</c> with the last character removed (<c>"\r\n"</c> counts as one character).
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".chop       # => "hell"
    /// "hi\r\n".chop      # => "hi"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Chop(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Chop();
        return result;
    }

    /// <summary>
    /// Removes the last character of <c>self</c> in place. Returns <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s.chop!     # => nil
    /// s           # => "hell"
    /// </code>
    /// </example>
    [RubyDef("() -> self?")]
    public static MRubyValue ChopBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        str.Chop();
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns a copy of <c>self</c> with all uppercase ASCII letters converted to lowercase.
    /// </summary>
    /// <example>
    /// <code>
    /// "Hello".downcase     # => "hello"
    /// "ABC".downcase       # => "abc"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Downcase(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Downcase();
        return result;
    }

    /// <summary>
    /// Downcases <c>self</c> in place. Returns <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "Hello"
    /// s.downcase!    # => nil
    /// s              # => "hello"
    /// </code>
    /// </example>
    [RubyDef("() -> self?")]
    public static MRubyValue DowncaseBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        str.Downcase();
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns a copy of <c>self</c> with all lowercase ASCII letters converted to uppercase.
    /// </summary>
    /// <example>
    /// <code>
    /// "Hello".upcase     # => "HELLO"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Upcase(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Upcase();
        return result;
    }

    /// <summary>
    /// Upcases <c>self</c> in place. Returns <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "Hello"
    /// s.upcase!     # => nil
    /// s             # => "HELLO"
    /// </code>
    /// </example>
    [RubyDef("() -> self?")]
    public static MRubyValue UpcaseBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        str.Upcase();
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns a new string with the characters of <c>self</c> reversed.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".reverse    # => "olleh"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Reverse(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var buf = Utf8Helper.Reverse(str.AsSpan());
        return state.NewStringOwned(buf);
    }

    /// <summary>
    /// Reverses the characters of <c>self</c> in place and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s.reverse!    # => "olleh"
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue ReverseBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);

        var buf = Utf8Helper.Reverse(str.AsSpan());
        str.MakeModifiable(str.Length);
        buf.CopyTo(str.AsSpan());
        return self;
    }

    /// <summary>
    /// Splits <c>self</c> into an array of substrings using the given delimiter (string or <c>Regexp</c>). With no delimiter splits on whitespace.
    /// </summary>
    /// <example>
    /// <code>
    /// "a,b,c".split(",")        # => ["a", "b", "c"]
    /// "a b  c".split            # => ["a", "b", "c"]
    /// "a,b,c".split(",", 2)     # => ["a", "b,c"]
    /// </code>
    /// </example>
    [RubyDef("(?(String | Regexp), ?Integer) -> Array[String]")]
    public static MRubyValue Split(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var argc = state.GetArgumentCount();
        var limit = -1; // Default: no limit (use -1 for existing methods)
        var regexpLimit = 0; // Default for regexp: remove trailing empty strings

        // Get limit from second argument if present
        if (argc >= 2)
        {
            limit = (int)state.GetArgumentAsIntegerAt(1);
            regexpLimit = limit;
        }

        // Check for Regexp argument first
        if (argc >= 1)
        {
            var arg0 = state.GetArgumentAt(0);
            if (RegexpMembers.TryGetRegexpData(arg0, out var regexpData))
            {
                return SplitByRegexp(state, str, regexpData, regexpLimit);
            }
        }

        var splitType = RStringSplitType.String;
        var separator = default(RString?);

        switch (argc)
        {
            case 0:
                splitType = RStringSplitType.Whitespaces;
                break;
            case 1:
            {
                var arg0 = state.GetArgumentAt(0);
                if (!arg0.IsNil)
                {
                    state.EnsureValueType(arg0, MRubyVType.String);
                    separator = arg0.As<RString>();
                }
                break;
            }
            case 2:
            {
                var arg0 = state.GetArgumentAt(0);
                if (!arg0.IsNil)
                {
                    state.EnsureValueType(arg0, MRubyVType.String);
                    separator = arg0.As<RString>();
                }
                break;
            }
            default:
                state.RaiseArgumentNumberError(argc, 0, 2);
                break;
        }

        if (separator == null || separator.Length == 1 && separator.AsSpan()[0] == (byte)' ')
        {
            splitType = RStringSplitType.Whitespaces;
        }

        var result = state.NewArray();
        switch (splitType)
        {
            case RStringSplitType.Whitespaces:
            {
                str.SplitByWhitespacesTo(result, limit);
                break;
            }
            case RStringSplitType.String:
            {
                str.SplitBytSeparatorTo(result, separator!, limit);
                break;
            }
        }
        return result;
    }

    static MRubyValue SplitByRegexp(MRubyState state, RString str, MRubyRegexpData regexpData, int limit)
    {
        var input = str.ToString();
        var result = state.NewArray();
        var regex = regexpData.Regex;

        if (input.Length == 0)
        {
            return result;
        }

        // Special handling for empty pattern - split into individual characters
        if (regexpData.Pattern.Length == 0)
        {
            var maxParts = limit > 0 ? limit : input.Length;
            for (var i = 0; i < input.Length && result.Length < maxParts - 1; i++)
            {
                result.Push(state.NewString(input.Substring(i, 1)));
            }
            if (result.Length < maxParts && result.Length < input.Length)
            {
                result.Push(state.NewString(input.Substring(result.Length)));
            }
            return result;
        }

        var matches = regex.Matches(input);

        if (matches.Count == 0)
        {
            result.Push(str.Dup());
            return result;
        }

        var lastEnd = 0;
        var count = 0;

        foreach (Match match in matches)
        {
            if (limit > 0 && count >= limit - 1) break;

            // Add the substring before this match
            var before = input.Substring(lastEnd, match.Index - lastEnd);
            result.Push(state.NewString(before));
            count++;

            // Add captured groups if any
            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    result.Push(state.NewString(match.Groups[i].Value));
                }
            }

            lastEnd = match.Index + match.Length;
        }

        // Add the remaining substring
        if (lastEnd <= input.Length)
        {
            var remaining = input.Substring(lastEnd);
            result.Push(state.NewString(remaining));
        }

        // If limit == 0 (default), remove trailing empty strings
        // If limit < 0, keep all trailing empties
        if (limit == 0)
        {
            while (result.Length > 0)
            {
                var last = result[result.Length - 1];
                if (last.Object is RString lastStr && lastStr.Length == 0)
                {
                    result.DeleteAt(result.Length - 1);
                }
                else
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the number of bytes in <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".bytesize     # => 5
    /// "日本".bytesize      # => 6
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue ByteCount(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        return str.AsSpan().Length;
    }

    /// <summary>
    /// Returns an array of the bytes that make up <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc".bytes      # => [97, 98, 99]
    /// </code>
    /// </example>
    [RubyDef("() -> Array[Integer]")]
    public static MRubyValue Bytes(MRubyState state, MRubyValue self)
    {
        var span = self.As<RString>().AsSpan();
        var array = state.NewArray(span.Length);
        foreach (var x in span)
        {
            array.Push(x);
        }
        return array;
    }

    /// <summary>
    /// Returns the byte offset of the first occurrence of the given substring (optionally starting at the given byte offset), or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".byteindex("l")        # => 2
    /// "hello".byteindex("l", 3)     # => 3
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> Integer?")]
    public static MRubyValue ByteIndex(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();

        var target = state.GetArgumentAsStringAt(0);
        var pos = 0;
        if (state.TryGetArgumentAt(1, out var arg1))
        {
            pos = (int)state.AsInteger(arg1);
        }

        var index = str.ByteIndexOf(target, pos);
        return index < 0 ? MRubyValue.Nil : index;
    }

    /// <summary>
    /// Returns the substring at the given byte index, byte range, or <c>(start, length)</c> in bytes. Returns <c>nil</c> when out of range.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".byteslice(1)         # => "e"
    /// "hello".byteslice(1, 3)      # => "ell"
    /// "hello".byteslice(1..3)      # => "ell"
    /// </code>
    /// </example>
    [RubyDef("(Integer, ?Integer) -> String?")]
    public static MRubyValue BytesSlice(MRubyState state, MRubyValue self)
    {
        int start;
        int length;
        var empty = true;

        var str = self.As<RString>();

        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 1:
                var arg0 = state.GetArgumentAt(0);
                if (arg0.Object is RRange range)
                {
                    var rangeResult = range.Calculate(str.Length, true, out start, out length);
                    if (rangeResult != RangeCalculateResult.Ok)
                    {
                        return MRubyValue.Nil;
                    }
                }
                else
                {
                    start = (int)state.AsInteger(arg0);
                    length = 1;
                    empty = false;
                }
                break;
            case 2:
                start = (int)state.GetArgumentAsIntegerAt(0);
                length = (int)state.GetArgumentAsIntegerAt(1);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                return MRubyValue.Nil;
        }

        var result = str.SubByteSequence(start, length);
        if (!empty && (result == null || result.Length == 0))
        {
            return MRubyValue.Nil;
        }
        return result != null ? result : MRubyValue.Nil;
    }

    /// <summary>
    /// Replaces the bytes at the given byte index, byte range, or <c>(start, length)</c> in <c>self</c> with the given string. Returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s.bytesplice(1, 3, "i")    # s == "hio"
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> String")]
    public static MRubyValue ByteSplice(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();

        var sourceIndex = 0;
        var sourceLength = 0;
        RString value = default!;
        var valueIndex = 0;
        var valueLength = 0;

        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 2:
                var range = state.GetArgumentAsRangeAt(0);
                value = state.GetArgumentAsStringAt(1);
                valueLength = value.Length;
                if (range.Calculate(str.Length, false, out sourceIndex, out sourceLength) != RangeCalculateResult.Ok)
                {
                    goto default;
                }
                break;
            case 3:
                var arg0 = state.GetArgumentAt(0);
                if (arg0.IsInteger)
                {
                    sourceIndex = (int)state.AsInteger(arg0);
                    var arg1 = state.GetArgumentAsIntegerAt(1);
                    // check overflow
                    if (sourceIndex > str.Length - arg1)
                    {
                        sourceLength = str.Length - sourceIndex;
                    }
                    else
                    {
                        sourceLength = (int)arg1;
                    }
                    value = state.GetArgumentAsStringAt(2);
                    valueLength = value.Length;
                }
                else
                {
                    state.EnsureValueType(arg0, MRubyVType.Range);
                    var range1 = arg0.As<RRange>();
                    value = state.GetArgumentAsStringAt(1);
                    var range2 = state.GetArgumentAsRangeAt(2);

                    if (range1.Calculate(str.Length, false, out sourceIndex, out sourceLength) != RangeCalculateResult.Ok)
                    {
                        goto default;
                    }
                    if (range2.Calculate(value.Length, false, out valueIndex, out valueLength) != RangeCalculateResult.Ok)
                    {
                        goto default;
                    }
                }
                break;
            case 5:
                sourceIndex = (int)state.GetArgumentAsIntegerAt(0);
                sourceLength = (int)state.GetArgumentAsIntegerAt(1);
                value = state.GetArgumentAsStringAt(2);
                valueIndex = (int)state.GetArgumentAsIntegerAt(3);
                valueLength = (int)state.GetArgumentAsIntegerAt(4);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 2, 5);
                break;
        }

        if (sourceIndex < 0)
        {
            sourceIndex += str.Length;
        }
        if (valueIndex < 0)
        {
            valueIndex += value.Length;
        }
        if (str.Length < sourceIndex ||
            sourceIndex < 0 ||
            value.Length < valueIndex ||
            valueIndex < 0)
        {
            state.Raise(Names.IndexError, "index out of string"u8);
        }
        if (sourceLength < 0 || valueLength < 0)
        {
            state.Raise(Names.IndexError, "negative length"u8);
        }

        if (str.Length < sourceIndex + sourceLength)
        {
            sourceLength = str.Length - sourceIndex;
        }
        if (value.Length < valueIndex + valueLength)
        {
            valueLength = value.Length - valueIndex;
        }

        if (sourceLength >= valueLength)
        {
            var currentLength = str.Length;
            var newLength = currentLength - (sourceLength - valueLength);
            str.MakeModifiable(newLength, true);
            value.AsSpan(valueIndex, valueLength).CopyTo(str.AsSpan(sourceIndex));
            if (sourceLength > valueLength)
            {
                str.AsSpan(sourceIndex + sourceLength, currentLength - (sourceIndex + sourceLength)).CopyTo(
                    str.AsSpan(sourceIndex + valueLength));
            }
        }
        else
        {
            var currentLength = str.Length;
            str.MakeModifiable(currentLength + valueLength - sourceLength, true);
            str.AsSpan(sourceIndex + sourceLength, currentLength - (sourceIndex + sourceLength)).CopyTo(
                str.AsSpan(sourceIndex + valueLength));
            value.AsSpan(valueIndex, valueLength).CopyTo(
                str.AsSpan(sourceIndex));
        }
        return self;
    }

    /// <summary>
    /// Returns the byte at the given byte index, or <c>nil</c> when out of range. Negative indices count from the end.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc".getbyte(0)    # => 97
    /// "abc".getbyte(-1)   # => 99
    /// "abc".getbyte(10)   # => nil
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer?")]
    public static MRubyValue GetByte(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var pos = (int)state.GetArgumentAsIntegerAt(0);
        if (pos < 0)
        {
            pos += str.Length;
        }
        if (pos < 0 || str.Length <= pos)
        {
            return MRubyValue.Nil;
        }

        return str.AsSpan()[pos];
    }

    /// <summary>
    /// Sets the byte at the given byte index to the low 8 bits of the given integer. Returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "abc"
    /// s.setbyte(0, 65)    # s == "Abc"
    /// </code>
    /// </example>
    [RubyDef("(Integer, Integer) -> Integer")]
    public static MRubyValue SetByte(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();

        var pos = (int)state.GetArgumentAsIntegerAt(0);
        var value = (int)state.GetArgumentAsIntegerAt(1);
        if (pos < -str.Length || str.Length <= pos)
        {
            state.Raise(Names.IndexError, $"index {pos} out of string");
        }
        if (pos < 0)
        {
            pos += str.Length;
        }
        str.AsSpan()[pos] = (byte)(value & 0xff);
        return self;
    }

    /// <summary>Internal helper used by <c>String#sub</c> to expand backreferences in a replacement pattern.</summary>
    [RubyDef("(String, MatchData) -> String")]
    public static MRubyValue InternalSubReplace(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var pattern = state.GetArgumentAsStringAt(0);
        var match = state.GetArgumentAsStringAt(1);
        var found = state.GetArgumentAsIntegerAt(2);

        var p = pattern.AsSpan();
        var m = pattern.AsSpan();

        var result = state.NewString(0);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (p[i] != '\\' || i + 1 >= pattern.Length)
            {
                result.Concat(p[i]);
                continue;
            }

            // escaped
            i++;

            switch (p[i])
            {
                case (byte)'\\':
                    result.Concat((byte)'\\');
                    break;
                case (byte)'`':
                    result.Concat(str.AsSpan(0, (int)found));
                    break;
                case (byte)'&':
                case (byte)'0':
                    result.Concat(match);
                    break;
                case (byte)'\'':
                    var pos = (int)found + match.Length;
                    if (str.Length > pos)
                    {
                        result.Concat(str.AsSpan(pos));
                    }
                    break;
                case >= (byte)'1' and <= (byte)'9':
                    // ignore sub-group match (no Regexp supported)
                    break;
                default:
                    result.Concat(p.Slice(i - 1, 2));
                    break;
            }
        }
        return result;
    }

}

