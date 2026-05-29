using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ChibiRuby.StdLib;

class MRubyRegexpData(string pattern, int rubyOptions = 0) : IEquatable<MRubyRegexpData>
{
    public const int RubyIgnoreCase = 1;
    public const int RubyExtended = 2;
    public const int RubyMultiline = 4;

    public Regex Regex { get; } = new(pattern, ConvertToRegexOptions(rubyOptions));
    public string Pattern => pattern;
    public int RubyOptions => rubyOptions;

    public bool Equals(MRubyRegexpData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Pattern == other.Pattern && RubyOptions == other.RubyOptions;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((MRubyRegexpData)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Pattern, RubyOptions);
    }

    /// <summary>
    /// Converts Ruby options to .NET RegexOptions.
    /// Ruby: IGNORECASE=1, EXTENDED=2, MULTILINE=4 (dot matches newline)
    /// .NET: Multiline means ^/$ match line boundaries (Ruby's default)
    ///       Singleline means . matches newline (Ruby's MULTILINE)
    /// </summary>
    static RegexOptions ConvertToRegexOptions(int rubyOptions)
    {
        // Always enable Multiline so ^/$ match at line boundaries (Ruby default)
        var options = RegexOptions.Multiline;

        if ((rubyOptions & RubyIgnoreCase) != 0)
        {
            options |= RegexOptions.IgnoreCase;
        }
        if ((rubyOptions & RubyExtended) != 0)
        {
            options |= RegexOptions.IgnorePatternWhitespace;
        }
        if ((rubyOptions & RubyMultiline) != 0)
        {
            // Ruby's MULTILINE = .NET's Singleline (dot matches newline)
            options |= RegexOptions.Singleline;
        }

        return options;
    }
}

/// <summary>
/// Regular expression literal -- written as <c>/pattern/flags</c>. Matching
/// against a <c>String</c> via <c>=~</c> or <c>match</c> returns a
/// <c>MatchData</c> (or <c>nil</c> on no match). In ChibiRuby, the pattern is
/// translated to and executed by .NET's <see cref="System.Text.RegularExpressions.Regex"/>,
/// so some Ruby-specific syntax may differ.
/// </summary>
[RubyClass("Regexp")]
static class RegexpMembers
{
    public static RData CreateRDataFromRegexp(MRubyState mrb, MRubyRegexpData regexpData)
    {
        return new RData(mrb.GetConst(mrb.Intern("Regexp"u8)).As<RClass>(), regexpData);
    }

    public static bool TryGetRegexpData(MRubyValue value, out MRubyRegexpData data)
    {
        if (value.Object is RData { Data: MRubyRegexpData regexpData })
        {
            data = regexpData;
            return true;
        }
        data = default!;
        return false;
    }

    public static MRubyRegexpData GetRegexpData(MRubyState mrb, MRubyValue value)
    {
        if (TryGetRegexpData(value, out var data))
        {
            return data;
        }
        mrb.Raise(Names.TypeError, "expected Regexp"u8);
        return default!; // unreachable
    }

    /// <summary>
    /// Updates regex global variables ($~, $&amp;, $`, $', $+, $1-$9) after a match.
    /// </summary>
    public static void UpdateRegexpGlobalVariables(MRubyState mrb, MRubyMatchData? matchData)
    {
        var gvMatch = mrb.Intern("$~"u8);
        var gvMatchedString = mrb.Intern("$&"u8);
        var gvPreMatch = mrb.Intern("$`"u8);
        var gvPostMatch = mrb.Intern("$'"u8);
        var gvLastCapture = mrb.Intern("$+"u8);

        if (matchData == null)
        {
            // Clear all global variables
            mrb.SetGlobalVariable(gvMatch, MRubyValue.Nil);
            mrb.SetGlobalVariable(gvMatchedString, MRubyValue.Nil);
            mrb.SetGlobalVariable(gvPreMatch, MRubyValue.Nil);
            mrb.SetGlobalVariable(gvPostMatch, MRubyValue.Nil);
            mrb.SetGlobalVariable(gvLastCapture, MRubyValue.Nil);
            for (var i = 1; i <= 9; i++)
            {
                mrb.SetGlobalVariable(mrb.Intern($"${i}"), MRubyValue.Nil);
            }
            return;
        }

        var match = matchData.Match;
        var input = matchData.OriginalString;

        // $~ = MatchData object
        var matchDataRData = MatchDataMembers.CreateRDataFromMatchData(mrb, matchData);
        mrb.SetGlobalVariable(gvMatch, matchDataRData);

        // $& = matched string
        mrb.SetGlobalVariable(gvMatchedString, mrb.NewString(match.Value));

        // $` = pre_match
        var preMatchValue = mrb.NewString(input.Substring(0, match.Index));
        mrb.SetGlobalVariable(gvPreMatch, preMatchValue);

        // $' = post_match
        mrb.SetGlobalVariable(gvPostMatch, mrb.NewString(input.Substring(match.Index + match.Length)));

        // $+ = last successful capture (last non-empty group)
        MRubyValue lastCapture = MRubyValue.Nil;
        for (var i = match.Groups.Count - 1; i >= 1; i--)
        {
            var g = match.Groups[i];
            if (g.Success)
            {
                lastCapture = mrb.NewString(g.Value);
                break;
            }
        }
        mrb.SetGlobalVariable(gvLastCapture, lastCapture);

        // $1-$9 capture groups
        for (var i = 1; i <= 9; i++)
        {
            var sym = mrb.Intern($"${i}");
            if (i < match.Groups.Count && match.Groups[i].Success)
            {
                mrb.SetGlobalVariable(sym, mrb.NewString(match.Groups[i].Value));
            }
            else
            {
                mrb.SetGlobalVariable(sym, MRubyValue.Nil);
            }
        }
    }

    /// <summary>
    /// Constructs a new <c>Regexp</c> from the given pattern string and option flags. When the first argument is itself a <c>Regexp</c>, returns a copy.
    /// </summary>
    /// <example>
    /// <code>
    /// r = Regexp.new("hello", Regexp::IGNORECASE)
    /// r.match?("Hello")   # => true
    /// </code>
    /// </example>
    [RubyDef("(String, ?(Integer | bool)) -> Regexp")]
    public static MRubyValue New(MRubyState mrb, MRubyValue self)
    {
        var patternValue = mrb.GetArgumentAt(0);
        string pattern;

        if (TryGetRegexpData(patternValue, out var existingRegexp))
        {
            // If first arg is a Regexp, return a copy (ignore second arg)
            return CreateRDataFromRegexp(mrb, new MRubyRegexpData(existingRegexp.Pattern, existingRegexp.RubyOptions));
        }

        if (patternValue.Object is RString patternStr)
        {
            pattern = patternStr.ToString();
        }
        else
        {
            mrb.Raise(Names.TypeError, "no implicit conversion into String"u8);
            return MRubyValue.Nil;
        }

        var rubyOptions = 0;
        if (mrb.TryGetArgumentAt(1, out var optionsValue))
        {
            if (optionsValue.IsInteger)
            {
                rubyOptions = (int)optionsValue.IntegerValue;
            }
            else if (optionsValue.Truthy)
            {
                rubyOptions = MRubyRegexpData.RubyIgnoreCase;
            }
        }

        try
        {
            var regexpData = new MRubyRegexpData(pattern, rubyOptions);
            return CreateRDataFromRegexp(mrb, regexpData);
        }
        catch (ArgumentException ex)
        {
            mrb.Raise(Names.RegexpError, $"{ex.Message}");
            return MRubyValue.Nil;
        }
    }

    /// <summary>
    /// Alias for <c>Regexp.new</c>. Compiles the pattern string into a <c>Regexp</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Regexp.compile("\\d+").match?("abc 42")   # => true
    /// </code>
    /// </example>
    [RubyDef("(String, ?(Integer | bool)) -> Regexp")]
    public static MRubyValue Compile(MRubyState mrb, MRubyValue self)
    {
        return New(mrb, self);
    }

    /// <summary>
    /// Returns a copy of the given string with regular-expression metacharacters escaped, so that the result matches the original literally.
    /// </summary>
    /// <example>
    /// <code>
    /// Regexp.escape("a.b*c")   # => "a\\.b\\*c"
    /// </code>
    /// </example>
    [RubyDef("(String) -> String")]
    public static MRubyValue Escape(MRubyState mrb, MRubyValue self)
    {
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ToString();
        var escaped = EscapeForRegexp(input);
        return mrb.NewString(escaped);
    }

    static string EscapeForRegexp(string input)
    {
        var sb = new StringBuilder(input.Length * 2);
        foreach (var c in input)
        {
            switch (c)
            {
                case '.':
                case '*':
                case '+':
                case '?':
                case '^':
                case '$':
                case '{':
                case '}':
                case '[':
                case ']':
                case '(':
                case ')':
                case '|':
                case '\\':
                    sb.Append('\\');
                    sb.Append(c);
                    break;
                case ' ':
                    sb.Append("\\ ");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Alias for <c>Regexp.escape</c>. Returns a string with regex metacharacters escaped.
    /// </summary>
    /// <example>
    /// <code>
    /// Regexp.quote("1+1")   # => "1\\+1"
    /// </code>
    /// </example>
    [RubyDef("(String) -> String")]
    public static MRubyValue Quote(MRubyState mrb, MRubyValue self)
    {
        return Escape(mrb, self);
    }

    /// <summary>
    /// Returns a <c>Regexp</c> that matches any of the given patterns, joined with alternation. Strings are escaped automatically; <c>Regexp</c> arguments preserve their options.
    /// </summary>
    /// <example>
    /// <code>
    /// r = Regexp.union("foo", "bar")
    /// r.match?("bar")   # => true
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> Regexp")]
    public static MRubyValue Union(MRubyState mrb, MRubyValue self)
    {
        var argc = mrb.GetArgumentCount();
        var patterns = new List<string>();

        // Handle single array argument
        if (argc == 1)
        {
            var arg = mrb.GetArgumentAt(0);
            if (arg.Object is RArray array)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    patterns.Add(ExtractPattern(mrb, array[i]));
                }
            }
            else
            {
                patterns.Add(ExtractPattern(mrb, arg));
            }
        }
        else
        {
            for (var i = 0; i < argc; i++)
            {
                patterns.Add(ExtractPattern(mrb, mrb.GetArgumentAt(i)));
            }
        }

        if (patterns.Count == 0)
        {
            return CreateRDataFromRegexp(mrb, new MRubyRegexpData("(?!)"));
        }

        var unionPattern = string.Join("|", patterns);
        try
        {
            return CreateRDataFromRegexp(mrb, new MRubyRegexpData(unionPattern));
        }
        catch (ArgumentException ex)
        {
            mrb.Raise(Names.RegexpError, $"{ex.Message}");
            return MRubyValue.Nil;
        }
    }

    static string ExtractPattern(MRubyState mrb, MRubyValue value)
    {
        if (TryGetRegexpData(value, out var regexpData))
        {
            // Include inline modifiers to preserve options
            var modifiers = "";
            if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0)
            {
                modifiers += "i";
            }
            if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) != 0)
            {
                modifiers += "x";
            }
            if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) != 0)
            {
                modifiers += "s"; // .NET's singleline = Ruby's multiline
            }

            if (modifiers.Length > 0)
            {
                return $"(?{modifiers}:{regexpData.Pattern})";
            }
            return $"(?:{regexpData.Pattern})";
        }
        if (value.Object is RString str)
        {
            return EscapeForRegexp(str.ToString());
        }
        mrb.Raise(Names.TypeError, "no implicit conversion into String"u8);
        return "";
    }

    /// <summary>
    /// Returns the argument if it is a <c>Regexp</c>, otherwise <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Regexp.try_convert(/x/)   # => /x/
    /// Regexp.try_convert("x")   # => nil
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> Regexp?")]
    public static MRubyValue TryConvert(MRubyState mrb, MRubyValue self)
    {
        var arg = mrb.GetArgumentAt(0);
        if (TryGetRegexpData(arg, out _))
        {
            return arg;
        }
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns the <c>MatchData</c> from the last successful pattern match in the current scope, or the nth capture when an index is given.
    /// </summary>
    /// <example>
    /// <code>
    /// /(\w+)/ =~ "hello"
    /// Regexp.last_match[0]   # => "hello"
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> untyped")]
    public static MRubyValue LastMatch(MRubyState mrb, MRubyValue self)
    {
        var matchValue = mrb.GetGlobalVariable(mrb.Intern("$~"u8));
        if (matchValue.IsNil)
        {
            return MRubyValue.Nil;
        }

        if (!mrb.TryGetArgumentAt(0, out var indexArg))
        {
            return matchValue;
        }

        // Regexp.last_match(n) returns the nth capture
        var n = (int)mrb.AsInteger(indexArg);
        return MatchDataMembers.OpAref(mrb, matchValue);
    }

    /// <summary>
    /// Matches <c>self</c> against the given string starting at the optional character position. Returns a <c>MatchData</c> object on success, or <c>nil</c> on failure.
    /// </summary>
    /// <example>
    /// <code>
    /// /(\w+)/.match("hello world")[0]   # => "hello"
    /// /xyz/.match("hello")              # => nil
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> MatchData?")]
    public static MRubyValue Match(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ToString();

        var pos = 0;
        if (mrb.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)mrb.AsInteger(posValue);
        }

        // Convert character position to actual position in string
        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            UpdateRegexpGlobalVariables(mrb, null);
            return MRubyValue.Nil;
        }

        var match = regexpData.Regex.Match(input, pos);
        if (!match.Success)
        {
            UpdateRegexpGlobalVariables(mrb, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        UpdateRegexpGlobalVariables(mrb, matchData);
        return MatchDataMembers.CreateRDataFromMatchData(mrb, matchData);
    }

    /// <summary>
    /// Returns <c>true</c> if the pattern matches the given string. Does not allocate a <c>MatchData</c> or update match-related global variables.
    /// </summary>
    /// <example>
    /// <code>
    /// /\d+/.match?("abc 42")   # => true
    /// /xyz/.match?("abc")      # => false
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> bool")]
    public static MRubyValue QMatch(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ToString();

        var pos = 0;
        if (mrb.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)mrb.AsInteger(posValue);
        }

        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            return MRubyValue.False;
        }

        var match = regexpData.Regex.Match(input, pos);
        return match.Success ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>
    /// Matches the pattern against the given string and returns the character index of the first match, or <c>nil</c> when there is no match.
    /// </summary>
    /// <example>
    /// <code>
    /// /world/ =~ "hello world"   # => 6
    /// /xyz/   =~ "hello"         # => nil
    /// </code>
    /// </example>
    [RubyDef("(String?) -> Integer?")]
    public static MRubyValue OpMatch(MRubyState mrb, MRubyValue self)
    {
        var arg = mrb.GetArgumentAt(0);
        if (arg.IsNil)
        {
            return MRubyValue.Nil;
        }

        var regexpData = GetRegexpData(mrb, self);
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ToString();

        var match = regexpData.Regex.Match(input);
        if (!match.Success)
        {
            UpdateRegexpGlobalVariables(mrb, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        UpdateRegexpGlobalVariables(mrb, matchData);

        // Return character index (not byte index)
        return match.Index;
    }

    /// <summary>
    /// Case-equality operator. Returns <c>true</c> if the pattern matches the argument. Used by <c>case</c>/<c>when</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// case "hello"
    /// when /^h/ then "starts with h"
    /// end                            # => "starts with h"
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eqq(MRubyState mrb, MRubyValue self)
    {
        var arg = mrb.GetArgumentAt(0);
        if (arg.IsNil)
        {
            return MRubyValue.False;
        }

        RString str;
        if (arg.Object is RString s)
        {
            str = s;
        }
        else
        {
            // Try to convert to string
            var converted = mrb.Send(arg, Names.ToS);
            if (converted.Object is not RString convertedStr)
            {
                return MRubyValue.False;
            }
            str = convertedStr;
        }

        var regexpData = GetRegexpData(mrb, self);
        var input = str.ToString();
        var match = regexpData.Regex.Match(input);

        if (match.Success)
        {
            var matchData = new MRubyMatchData(match, regexpData, input);
            UpdateRegexpGlobalVariables(mrb, matchData);
            return MRubyValue.True;
        }

        UpdateRegexpGlobalVariables(mrb, null);
        return MRubyValue.False;
    }

    /// <summary>
    /// Returns the original pattern string of <c>self</c>, without surrounding slashes or option flags.
    /// </summary>
    /// <example>
    /// <code>
    /// /hello/i.source   # => "hello"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Source(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        return mrb.NewString(regexpData.Pattern);
    }

    /// <summary>
    /// Returns the set of options flags used to create <c>self</c> as an integer bitmask (IGNORECASE=1, EXTENDED=2, MULTILINE=4).
    /// </summary>
    /// <example>
    /// <code>
    /// /hello/i.options   # => 1
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Options(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        return regexpData.RubyOptions;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> was compiled with the case-insensitive option.
    /// </summary>
    /// <example>
    /// <code>
    /// /hello/i.casefold?   # => true
    /// /hello/.casefold?    # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue QCasefold(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        return (regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0;
    }

    /// <summary>
    /// Returns a string in the "(?opts-opts:pattern)" form that, when compiled again, reproduces the same pattern and options.
    /// </summary>
    /// <example>
    /// <code>
    /// /hello/i.to_s   # => "(?i-mx:hello)"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        var sb = new StringBuilder();
        sb.Append("(?");

        // Add option flags
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) != 0)
        {
            sb.Append('m');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0)
        {
            sb.Append('i');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) != 0)
        {
            sb.Append('x');
        }

        // Add disabled options
        sb.Append('-');
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) == 0)
        {
            sb.Append('m');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) == 0)
        {
            sb.Append('i');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) == 0)
        {
            sb.Append('x');
        }

        sb.Append(':');
        sb.Append(regexpData.Pattern);
        sb.Append(')');
        return mrb.NewString(sb.ToString());
    }

    /// <summary>
    /// Returns a literal-style representation of <c>self</c>, like <c>"/pattern/flags"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// /hello/i.inspect   # => "/hello/i"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        var sb = new StringBuilder();
        sb.Append('/');
        sb.Append(regexpData.Pattern);
        sb.Append('/');

        if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0)
        {
            sb.Append('i');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) != 0)
        {
            sb.Append('m');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) != 0)
        {
            sb.Append('x');
        }
        return mrb.NewString(sb.ToString());
    }

    /// <summary>
    /// Returns <c>true</c> when the argument is a <c>Regexp</c> with the same pattern and options as <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// /hello/i == /hello/i   # => true
    /// /hello/i == /hello/    # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState mrb, MRubyValue self)
    {
        var other = mrb.GetArgumentAt(0);
        if (!TryGetRegexpData(other, out var otherData))
        {
            return MRubyValue.False;
        }
        var selfData = GetRegexpData(mrb, self);
        return selfData.Equals(otherData);
    }

    /// <summary>
    /// Returns <c>true</c> when the argument is an equal <c>Regexp</c>. Equivalent to <c>==</c> for <c>Regexp</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// /a/.eql?(/a/)   # => true
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue QEql(MRubyState mrb, MRubyValue self)
    {
        return OpEq(mrb, self);
    }

    /// <summary>
    /// Returns a hash code computed from the pattern and options of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// /a/.hash.class   # => Integer
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Hash(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        return regexpData.GetHashCode();
    }

    /// <summary>
    /// Returns a hash mapping each named capture group in <c>self</c> to an array containing its group index.
    /// </summary>
    /// <example>
    /// <code>
    /// /(?&lt;year&gt;\d{4})/.named_captures   # => {"year" => [1]}
    /// </code>
    /// </example>
    [RubyDef("() -> Hash[String, Array[Integer]]")]
    public static MRubyValue NamedCaptures(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        var hash = mrb.NewHash(0);

        var groupNames = regexpData.Regex.GetGroupNames();
        foreach (var name in groupNames)
        {
            // Skip numeric group names
            if (int.TryParse(name, out _)) continue;

            var groupNumber = regexpData.Regex.GroupNumberFromName(name);
            var indices = mrb.NewArray(1);
            indices.Push(groupNumber);
            hash[mrb.NewString(name)] = indices;
        }

        return hash;
    }

    /// <summary>
    /// Returns the list of named capture group names defined in <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// /(?&lt;y&gt;\d+)-(?&lt;m&gt;\d+)/.names   # => ["y", "m"]
    /// </code>
    /// </example>
    [RubyDef("() -> Array[String]")]
    public static MRubyValue NamesMethod(MRubyState mrb, MRubyValue self)
    {
        var regexpData = GetRegexpData(mrb, self);
        var groupNames = regexpData.Regex.GetGroupNames();

        var names = new List<string>();
        foreach (var name in groupNames)
        {
            // Skip numeric group names
            if (!int.TryParse(name, out _))
            {
                names.Add(name);
            }
        }

        var array = mrb.NewArray(names.Count);
        foreach (var name in names)
        {
            array.Push(mrb.NewString(name));
        }
        return array;
    }
}

/// <summary>
/// Regexp-related methods for String class.
/// </summary>
static class StringRegexpMembers
{
    /// <summary>
    /// Matches <c>self</c> against the given <c>Regexp</c> and returns the index of the first match, or <c>nil</c> if there is no match.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello world" =~ /world/   # => 6
    /// "hello" =~ /xyz/           # => nil
    /// </code>
    /// </example>
    [RubyDef("(String?) -> Integer?")]
    public static MRubyValue OpMatch(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        if (arg.IsNil)
        {
            return MRubyValue.Nil;
        }

        if (!RegexpMembers.TryGetRegexpData(arg, out var regexpData))
        {
            // Try calling =~ on the other object
            return state.Send(arg, state.Intern("=~"u8), self);
        }

        var input = str.ToString();
        var match = regexpData.Regex.Match(input);

        if (!match.Success)
        {
            RegexpMembers.UpdateRegexpGlobalVariables(state, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
        return match.Index;
    }

    /// <summary>
    /// Matches <c>self</c> against the given <c>Regexp</c> or pattern string starting at the optional character position. Returns <c>MatchData</c> or <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello world".match(/(\w+)/)[1]   # => "hello"
    /// "abc".match(/x/)                  # => nil
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> MatchData?")]
    public static MRubyValue Match(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        MRubyRegexpData regexpData;
        if (RegexpMembers.TryGetRegexpData(arg, out var data))
        {
            regexpData = data;
        }
        else if (arg.Object is RString patternStr)
        {
            try
            {
                regexpData = new MRubyRegexpData(patternStr.ToString());
            }
            catch (ArgumentException ex)
            {
                state.Raise(Names.RegexpError, $"{ex.Message}");
                return MRubyValue.Nil;
            }
        }
        else
        {
            state.Raise(Names.TypeError, "wrong argument type"u8);
            return MRubyValue.Nil;
        }

        var input = str.ToString();
        var pos = 0;
        if (state.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)state.AsInteger(posValue);
        }

        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            RegexpMembers.UpdateRegexpGlobalVariables(state, null);
            return MRubyValue.Nil;
        }

        var match = regexpData.Regex.Match(input, pos);
        if (!match.Success)
        {
            RegexpMembers.UpdateRegexpGlobalVariables(state, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
        return MatchDataMembers.CreateRDataFromMatchData(state, matchData);
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> matches the given <c>Regexp</c> or pattern string. Does not allocate <c>MatchData</c> or update match globals.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc 42".match?(/\d+/)   # => true
    /// "abc".match?(/\d+/)      # => false
    /// </code>
    /// </example>
    [RubyDef("(String, ?Integer) -> bool")]
    public static MRubyValue QMatch(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        MRubyRegexpData regexpData;
        if (RegexpMembers.TryGetRegexpData(arg, out var data))
        {
            regexpData = data;
        }
        else if (arg.Object is RString patternStr)
        {
            try
            {
                regexpData = new MRubyRegexpData(patternStr.ToString());
            }
            catch (ArgumentException ex)
            {
                state.Raise(Names.RegexpError, $"{ex.Message}");
                return MRubyValue.False;
            }
        }
        else
        {
            state.Raise(Names.TypeError, "wrong argument type"u8);
            return MRubyValue.False;
        }

        var input = str.ToString();
        var pos = 0;
        if (state.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)state.AsInteger(posValue);
        }

        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            return MRubyValue.False;
        }

        var match = regexpData.Regex.Match(input, pos);
        return match.Success ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>
    /// Returns a new string with the first match of <c>pattern</c> replaced by <c>replacement</c>, or by the block's return value when a block is given.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello world".sub("world", "there")   # => "hello there"
    /// "hello".sub(/l/) { |m| m.upcase }     # => "heLlo"
    /// </code>
    /// </example>
    [RubyDef("(Regexp | String, ?String) ?{ (String) -> String } -> String")]
    public static MRubyValue Sub(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        return SubImpl(state, str, false);
    }

    /// <summary>
    /// Replaces the first match of <c>pattern</c> in <c>self</c> in place. Returns <c>self</c> when a substitution was made, otherwise <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hello"
    /// s.sub!("ll", "LL")   # => "heLLo"
    /// s                    # => "heLLo"
    /// </code>
    /// </example>
    [RubyDef("(Regexp | String, ?String) ?{ (String) -> String } -> self?")]
    public static MRubyValue SubBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        return SubImpl(state, str, true);
    }

    static MRubyValue SubImpl(MRubyState state, RString str, bool inPlace)
    {
        var patternArg = state.GetArgumentAt(0);
        var block = state.GetBlockArgument();
        var input = str.ToString();

        // Handle Regexp pattern
        if (RegexpMembers.TryGetRegexpData(patternArg, out var regexpData))
        {
            var match = regexpData.Regex.Match(input);
            if (!match.Success)
            {
                RegexpMembers.UpdateRegexpGlobalVariables(state, null);
                return inPlace ? MRubyValue.Nil : str.Dup();
            }

            var matchData = new MRubyMatchData(match, regexpData, input);
            RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);

            string replacement;
            if (block != null)
            {
                var matchStr = state.NewString(match.Value);
                var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                replacement = state.Stringify(blockResult).ToString();
            }
            else
            {
                var replacementArg = state.GetArgumentAsStringAt(1);
                replacement = ProcessReplacementString(replacementArg.ToString(), match, input);
            }

            var result = input.Substring(0, match.Index) + replacement + input.Substring(match.Index + match.Length);

            if (inPlace)
            {
                var newBytes = Encoding.UTF8.GetBytes(result);
                str.MakeModifiable(newBytes.Length, true);
                newBytes.CopyTo(str.AsSpan());
                return str;
            }
            return state.NewString(result);
        }

        // Handle String pattern
        if (patternArg.Object is RString patternStr)
        {
            var pattern = patternStr.ToString();
            var index = input.IndexOf(pattern, StringComparison.Ordinal);

            if (index < 0)
            {
                return inPlace ? MRubyValue.Nil : str.Dup();
            }

            string replacement;
            if (block != null)
            {
                var matchStr = state.NewString(pattern);
                var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                replacement = state.Stringify(blockResult).ToString();
            }
            else
            {
                var replacementArg = state.GetArgumentAsStringAt(1);
                // Process replacement string for \0, \&, etc. but without capture groups
                replacement = ProcessSimpleReplacementString(replacementArg.ToString(), pattern, input, index);
            }

            var result = input.Substring(0, index) + replacement + input.Substring(index + pattern.Length);

            if (inPlace)
            {
                var newBytes = Encoding.UTF8.GetBytes(result);
                str.MakeModifiable(newBytes.Length, true);
                newBytes.CopyTo(str.AsSpan());
                return str;
            }
            return state.NewString(result);
        }

        state.Raise(Names.TypeError, "wrong argument type"u8);
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns a new string with all matches of <c>pattern</c> replaced. Accepts a replacement string, a hash, or a block returning the replacement.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc abc".gsub("a", "A")          # => "Abc Abc"
    /// "hello".gsub(/l/) { |m| m * 2 }   # => "hellllo"
    /// </code>
    /// </example>
    [RubyDef("(Regexp | String, ?(String | Hash[String, String])) ?{ (String) -> String } -> String")]
    public static MRubyValue Gsub(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        return GsubImpl(state, str, false);
    }

    /// <summary>
    /// Replaces all matches of <c>pattern</c> in <c>self</c> in place. Returns <c>self</c> when any substitution was made, otherwise <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "abc abc"
    /// s.gsub!("a", "A")    # => "Abc Abc"
    /// s                    # => "Abc Abc"
    /// </code>
    /// </example>
    [RubyDef("(Regexp | String, ?(String | Hash[String, String])) ?{ (String) -> String } -> self?")]
    public static MRubyValue GsubBang(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        return GsubImpl(state, str, true);
    }

    static MRubyValue GsubImpl(MRubyState state, RString str, bool inPlace)
    {
        var argc = state.GetArgumentCount();
        if (argc == 0)
        {
            state.RaiseArgumentNumberError(argc, 1, 2);
            return MRubyValue.Nil;
        }
        if (argc > 2)
        {
            state.RaiseArgumentNumberError(argc, 1, 2);
            return MRubyValue.Nil;
        }

        var patternArg = state.GetArgumentAt(0);
        var block = state.GetBlockArgument();
        var input = str.ToString();

        // Check for hash argument
        RHash? hashArg = null;
        RString? replacementStr = null;
        if (block == null && state.TryGetArgumentAt(1, out var arg1))
        {
            if (arg1.Object is RHash hash)
            {
                hashArg = hash;
            }
            else
            {
                replacementStr = state.GetArgumentAsStringAt(1);
            }
        }

        // Handle Regexp pattern
        if (RegexpMembers.TryGetRegexpData(patternArg, out var regexpData))
        {
            var matches = regexpData.Regex.Matches(input);
            if (matches.Count == 0)
            {
                RegexpMembers.UpdateRegexpGlobalVariables(state, null);
                return inPlace ? MRubyValue.Nil : str.Dup();
            }

            var sb = new StringBuilder();
            var lastEnd = 0;
            MRubyMatchData? lastMatchData = null;

            foreach (Match match in matches)
            {
                sb.Append(input, lastEnd, match.Index - lastEnd);

                var matchData = new MRubyMatchData(match, regexpData, input);
                lastMatchData = matchData;

                string replacement;
                if (block != null)
                {
                    // Set global variables before calling block
                    RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
                    var matchStr = state.NewString(match.Value);
                    var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                    replacement = state.Stringify(blockResult).ToString();
                }
                else if (hashArg != null)
                {
                    var key = state.NewString(match.Value);
                    if (hashArg.TryGetValue(key, out var value))
                    {
                        replacement = state.Stringify(value).ToString();
                    }
                    else
                    {
                        // Key not found in hash - remove the match (Ruby behavior)
                        replacement = "";
                    }
                }
                else
                {
                    replacement = ProcessReplacementString(replacementStr!.ToString(), match, input);
                }

                sb.Append(replacement);
                lastEnd = match.Index + match.Length;
            }

            sb.Append(input, lastEnd, input.Length - lastEnd);

            // Update global variables with last match
            RegexpMembers.UpdateRegexpGlobalVariables(state, lastMatchData);

            var result = sb.ToString();

            if (inPlace)
            {
                var newBytes = Encoding.UTF8.GetBytes(result);
                str.MakeModifiable(newBytes.Length, true);
                newBytes.CopyTo(str.AsSpan());
                return str;
            }
            return state.NewString(result);
        }

        // Handle String pattern
        if (patternArg.Object is RString patternStr)
        {
            var pattern = patternStr.ToString();

            // Handle empty pattern - replace between each character
            if (pattern.Length == 0)
            {
                var sb = new StringBuilder();

                // Insert replacement at start, between each character, and at end
                for (var i = 0; i <= input.Length; i++)
                {
                    string replacement;
                    if (block != null)
                    {
                        var matchStr = state.NewString("");
                        var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                        replacement = state.Stringify(blockResult).ToString();
                    }
                    else if (hashArg != null)
                    {
                        var key = state.NewString("");
                        if (hashArg.TryGetValue(key, out var value))
                        {
                            replacement = state.Stringify(value).ToString();
                        }
                        else
                        {
                            replacement = "";
                        }
                    }
                    else
                    {
                        replacement = replacementStr?.ToString() ?? "";
                    }

                    sb.Append(replacement);
                    if (i < input.Length) sb.Append(input[i]);
                }

                var result = sb.ToString();
                if (inPlace)
                {
                    var newBytes = Encoding.UTF8.GetBytes(result);
                    str.MakeModifiable(newBytes.Length, true);
                    newBytes.CopyTo(str.AsSpan());
                    return str;
                }
                return state.NewString(result);
            }

            {
                var sb = new StringBuilder();
                var lastEnd = 0;
                var hasMatch = false;

                var index = 0;
                while ((index = input.IndexOf(pattern, lastEnd, StringComparison.Ordinal)) >= 0)
                {
                    hasMatch = true;
                    sb.Append(input, lastEnd, index - lastEnd);

                    string replacement;
                    if (block != null)
                    {
                        var matchStr = state.NewString(pattern);
                        var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                        replacement = state.Stringify(blockResult).ToString();
                    }
                    else if (hashArg != null)
                    {
                        var key = state.NewString(pattern);
                        if (hashArg.TryGetValue(key, out var value))
                        {
                            replacement = state.Stringify(value).ToString();
                        }
                        else
                        {
                            replacement = "";
                        }
                    }
                    else
                    {
                        replacement = ProcessSimpleReplacementString(replacementStr!.ToString(), pattern, input, index);
                    }

                    sb.Append(replacement);
                    lastEnd = index + pattern.Length;
                }

                if (!hasMatch)
                {
                    return inPlace ? MRubyValue.Nil : str.Dup();
                }

                sb.Append(input, lastEnd, input.Length - lastEnd);
                var result = sb.ToString();

                if (inPlace)
                {
                    var newBytes = Encoding.UTF8.GetBytes(result);
                    str.MakeModifiable(newBytes.Length, true);
                    newBytes.CopyTo(str.AsSpan());
                    return str;
                }
                return state.NewString(result);
            }
        }

        state.Raise(Names.TypeError, "wrong argument type"u8);
        return MRubyValue.Nil;
    }

    static string ProcessReplacementString(string replacement, Match match, string input)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] == '\\' && i + 1 < replacement.Length)
            {
                var next = replacement[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case '&':
                    case '0':
                        sb.Append(match.Value);
                        i++;
                        break;
                    case '`':
                        sb.Append(input, 0, match.Index);
                        i++;
                        break;
                    case '\'':
                        sb.Append(input, match.Index + match.Length, input.Length - (match.Index + match.Length));
                        i++;
                        break;
                    case '+':
                        // Last successful capture
                        for (var j = match.Groups.Count - 1; j >= 1; j--)
                        {
                            if (match.Groups[j].Success)
                            {
                                sb.Append(match.Groups[j].Value);
                                break;
                            }
                        }
                        i++;
                        break;
                    case >= '1' and <= '9':
                        var groupIndex = next - '0';
                        if (groupIndex < match.Groups.Count && match.Groups[groupIndex].Success)
                        {
                            sb.Append(match.Groups[groupIndex].Value);
                        }
                        i++;
                        break;
                    default:
                        sb.Append(replacement[i]);
                        break;
                }
            }
            else
            {
                sb.Append(replacement[i]);
            }
        }
        return sb.ToString();
    }

    static string ProcessSimpleReplacementString(string replacement, string matched, string input, int matchIndex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] == '\\' && i + 1 < replacement.Length)
            {
                var next = replacement[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case '&':
                    case '0':
                        sb.Append(matched);
                        i++;
                        break;
                    case '`':
                        sb.Append(input, 0, matchIndex);
                        i++;
                        break;
                    case '\'':
                        sb.Append(input, matchIndex + matched.Length, input.Length - (matchIndex + matched.Length));
                        i++;
                        break;
                    case >= '1' and <= '9':
                    case '+':
                        // No capture groups for string pattern - these are empty
                        i++;
                        break;
                    default:
                        sb.Append(replacement[i]);
                        break;
                }
            }
            else
            {
                sb.Append(replacement[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns an array of all non-overlapping matches of <c>pattern</c> in <c>self</c>. If the pattern has capture groups, each element is an array of captures. With a block, yields each match and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "abc 123 def 456".scan(/\d+/)          # => ["123", "456"]
    /// "a1b2".scan(/(\w)(\d)/)                # => [["a","1"], ["b","2"]]
    /// </code>
    /// </example>
    [RubyDef("(Regexp | String) ?{ (untyped) -> void } -> Array[untyped] | self")]
    public static MRubyValue Scan(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var patternArg = state.GetArgumentAt(0);
        var block = state.GetBlockArgument();
        var input = str.ToString();

        MRubyRegexpData regexpData;
        if (RegexpMembers.TryGetRegexpData(patternArg, out var data))
        {
            regexpData = data;
        }
        else if (patternArg.Object is RString patternStr)
        {
            try
            {
                regexpData = new MRubyRegexpData(Regex.Escape(patternStr.ToString()));
            }
            catch (ArgumentException ex)
            {
                state.Raise(Names.RegexpError, $"{ex.Message}");
                return MRubyValue.Nil;
            }
        }
        else
        {
            state.Raise(Names.TypeError, "wrong argument type"u8);
            return MRubyValue.Nil;
        }

        var matches = regexpData.Regex.Matches(input);
        var result = state.NewArray(matches.Count);

        foreach (Match match in matches)
        {
            var matchData = new MRubyMatchData(match, regexpData, input);
            RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);

            MRubyValue item;
            if (match.Groups.Count > 1)
            {
                // Has capture groups - return array of captures
                var captures = state.NewArray(match.Groups.Count - 1);
                for (var i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success)
                    {
                        captures.Push(state.NewString(match.Groups[i].Value));
                    }
                    else
                    {
                        captures.Push(MRubyValue.Nil);
                    }
                }
                item = captures;
            }
            else
            {
                // No capture groups - return matched string
                item = state.NewString(match.Value);
            }

            if (block != null)
            {
                state.YieldWithClass(state.StringClass, self, [item], block);
            }
            else
            {
                result.Push(item);
            }
        }

        return block != null ? self : (MRubyValue)result;
    }

    /// <summary>
    /// Returns the index of the first occurrence of the given <c>Regexp</c> or substring in <c>self</c>, or <c>nil</c> when not found. Searches start at the optional offset.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello world".index(/world/)   # => 6
    /// "hello".index(/xyz/)           # => nil
    /// </code>
    /// </example>
    [RubyDef("(Regexp | String, ?Integer) -> Integer?")]
    public static MRubyValue Index(MRubyState state, MRubyValue self)
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        // Check if it's a Regexp
        if (RegexpMembers.TryGetRegexpData(arg, out var regexpData))
        {
            var input = str.ToString();
            var pos = 0;
            if (state.TryGetArgumentAt(1, out var posValue))
            {
                pos = (int)state.AsInteger(posValue);
            }

            if (pos < 0)
            {
                pos = input.Length + pos;
            }
            if (pos < 0 || pos > input.Length)
            {
                return MRubyValue.Nil;
            }

            var match = regexpData.Regex.Match(input, pos);
            if (!match.Success)
            {
                return MRubyValue.Nil;
            }

            var matchData = new MRubyMatchData(match, regexpData, input);
            RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
            return match.Index;
        }

        // Fall back to string index
        return StringMembers.Index(state, self);
    }
}
