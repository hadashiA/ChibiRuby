using System;
using System.Text;
using System.Text.RegularExpressions;
using MRubyCS;

namespace MRubyCS.StdLib;

/// <summary>
/// Wraps a .NET Match object for use in MRuby.
/// </summary>
class MRubyMatchData
{
    public Match Match { get; }
    public MRubyRegexpData Regexp { get; }
    public string OriginalString { get; }

    public MRubyMatchData(Match match, MRubyRegexpData regexp, string originalString)
    {
        Match = match;
        Regexp = regexp;
        OriginalString = originalString;
    }
}

/// <summary>
/// Result of a successful regexp match -- returned by <c>Regexp#match</c> and
/// available as the <c>$~</c> implicit variable after a match. Indexing
/// (<c>md[0]</c>, <c>md[1]</c>, ...) returns the matched substring or capture
/// groups; named captures are accessed by symbol or string.
/// </summary>
[RubyClass("MatchData")]
static class MatchDataMembers
{
    public static RData CreateRDataFromMatchData(MRubyState mrb, MRubyMatchData matchData)
    {
        return new RData(mrb.GetConst(mrb.Intern("MatchData"u8)).As<RClass>(), matchData);
    }

    public static bool TryGetMatchData(MRubyValue value, out MRubyMatchData data)
    {
        if (value.Object is RData { Data: MRubyMatchData matchData })
        {
            data = matchData;
            return true;
        }
        data = default!;
        return false;
    }

    public static MRubyMatchData GetMatchData(MRubyState mrb, MRubyValue value)
    {
        if (TryGetMatchData(value, out var data))
        {
            return data;
        }
        mrb.Raise(Names.TypeError, "expected MatchData"u8);
        return default!; // unreachable
    }

    /// <summary>
    /// Returns a capture group by integer index, name, or range. With two integer arguments returns a sub-array of captures.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m[0]      # => "hello world"
    /// m[1]      # => "hello"
    /// m[1, 2]   # => ["hello", "world"]
    /// </code>
    /// </example>
    [RubyDef("(Integer | Symbol | String | Range[Integer]) -> String? | (Integer, Integer) -> Array[String?]?")]
    public static MRubyValue OpAref(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var arg = mrb.GetArgumentAt(0);

        // Handle range
        if (arg.Object is RRange range)
        {
            return GetByRange(mrb, matchData, range);
        }

        // Handle named capture (symbol or string)
        if (arg.IsSymbol)
        {
            var name = mrb.NameOf(arg.SymbolValue);
            return GetByName(mrb, matchData, name.ToString());
        }

        if (arg.Object is RString nameStr)
        {
            return GetByName(mrb, matchData, nameStr.ToString());
        }

        // Handle numeric index
        var index = (int)mrb.AsInteger(arg);

        // Handle two-argument form: m[index, length]
        if (mrb.TryGetArgumentAt(1, out var lengthArg))
        {
            var length = (int)mrb.AsInteger(lengthArg);
            return GetByIndexAndLength(mrb, matchData, index, length);
        }

        return GetByIndex(mrb, matchData, index);
    }

    static MRubyValue GetByIndex(MRubyState mrb, MRubyMatchData matchData, int index)
    {
        var groups = matchData.Match.Groups;

        if (index < 0)
        {
            index += groups.Count;
        }

        if (index < 0 || index >= groups.Count)
        {
            return MRubyValue.Nil;
        }

        var group = groups[index];
        if (!group.Success)
        {
            return MRubyValue.Nil;
        }

        return mrb.NewString(group.Value);
    }

    static MRubyValue GetByName(MRubyState mrb, MRubyMatchData matchData, string name)
    {
        try
        {
            var group = matchData.Match.Groups[name];
            if (!group.Success)
            {
                return MRubyValue.Nil;
            }
            return mrb.NewString(group.Value);
        }
        catch (ArgumentException)
        {
            mrb.Raise(Names.IndexError, $"undefined group name reference: {name}");
            return MRubyValue.Nil;
        }
    }

    static MRubyValue GetByRange(MRubyState mrb, MRubyMatchData matchData, RRange range)
    {
        var groups = matchData.Match.Groups;
        var totalCount = groups.Count;

        if (range.Calculate(totalCount, true, out var start, out var length) != RangeCalculateResult.Ok)
        {
            return MRubyValue.Nil;
        }

        var array = mrb.NewArray(length);
        for (var i = 0; i < length && start + i < totalCount; i++)
        {
            var group = groups[start + i];
            if (group.Success)
            {
                array.Push(mrb.NewString(group.Value));
            }
            else
            {
                array.Push(MRubyValue.Nil);
            }
        }
        return array;
    }

    static MRubyValue GetByIndexAndLength(MRubyState mrb, MRubyMatchData matchData, int start, int length)
    {
        var groups = matchData.Match.Groups;
        var totalCount = groups.Count;

        if (start < 0)
        {
            start += totalCount;
        }

        if (start < 0 || start >= totalCount || length < 0)
        {
            return MRubyValue.Nil;
        }

        var array = mrb.NewArray(length);
        for (var i = 0; i < length && start + i < totalCount; i++)
        {
            var group = groups[start + i];
            if (group.Success)
            {
                array.Push(mrb.NewString(group.Value));
            }
            else
            {
                array.Push(MRubyValue.Nil);
            }
        }
        return array;
    }

    /// <summary>
    /// Returns the character offset of the start of the nth capture group, or <c>nil</c> when the group did not participate in the match.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+)/.match("hello world")
    /// m.begin(0)   # => 0
    /// m.begin(1)   # => 0
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer?")]
    public static MRubyValue Begin(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var index = (int)mrb.GetArgumentAsIntegerAt(0);
        var groups = matchData.Match.Groups;

        if (index < 0)
        {
            index += groups.Count;
        }

        if (index < 0 || index >= groups.Count)
        {
            mrb.Raise(Names.IndexError, $"index {index} out of matches");
            return MRubyValue.Nil;
        }

        var group = groups[index];
        if (!group.Success)
        {
            return MRubyValue.Nil;
        }

        // Return character index
        return group.Index;
    }

    /// <summary>
    /// Returns the character offset just past the end of the nth capture group, or <c>nil</c> when the group did not participate in the match.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+)/.match("hello world")
    /// m.end(1)   # => 5
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer?")]
    public static MRubyValue End(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var index = (int)mrb.GetArgumentAsIntegerAt(0);
        var groups = matchData.Match.Groups;

        if (index < 0)
        {
            index += groups.Count;
        }

        if (index < 0 || index >= groups.Count)
        {
            mrb.Raise(Names.IndexError, $"index {index} out of matches");
            return MRubyValue.Nil;
        }

        var group = groups[index];
        if (!group.Success)
        {
            return MRubyValue.Nil;
        }

        // Return character index of end (exclusive)
        return group.Index + group.Length;
    }

    /// <summary>
    /// Returns a two-element array <c>[begin, end]</c> giving the character offsets of the nth capture group.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.offset(2)   # => [6, 11]
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Array[Integer?]")]
    public static MRubyValue Offset(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var index = (int)mrb.GetArgumentAsIntegerAt(0);
        var groups = matchData.Match.Groups;

        if (index < 0)
        {
            index += groups.Count;
        }

        if (index < 0 || index >= groups.Count)
        {
            mrb.Raise(Names.IndexError, $"index {index} out of matches");
            return MRubyValue.Nil;
        }

        var group = groups[index];
        var array = mrb.NewArray(2);

        if (!group.Success)
        {
            array.Push(MRubyValue.Nil);
            array.Push(MRubyValue.Nil);
        }
        else
        {
            array.Push(new MRubyValue(group.Index));
            array.Push(new MRubyValue(group.Index + group.Length));
        }

        return array;
    }

    /// <summary>
    /// Returns the captured groups from <c>self</c> as an array, excluding the whole match at index 0.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.captures   # => ["hello", "world"]
    /// </code>
    /// </example>
    [RubyDef("() -> Array[String?]")]
    public static MRubyValue Captures(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var groups = matchData.Match.Groups;

        // captures excludes index 0 (full match)
        var array = mrb.NewArray(groups.Count - 1);
        for (var i = 1; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Success)
            {
                array.Push(mrb.NewString(group.Value));
            }
            else
            {
                array.Push(MRubyValue.Nil);
            }
        }
        return array;
    }

    /// <summary>
    /// Returns the whole match followed by every capture as an array.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.to_a   # => ["hello world", "hello", "world"]
    /// </code>
    /// </example>
    [RubyDef("() -> Array[String?]")]
    public static MRubyValue ToA(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var groups = matchData.Match.Groups;

        var array = mrb.NewArray(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Success)
            {
                array.Push(mrb.NewString(group.Value));
            }
            else
            {
                array.Push(MRubyValue.Nil);
            }
        }
        return array;
    }

    /// <summary>
    /// Returns the entire matched substring of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.to_s   # => "hello world"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return mrb.NewString(matchData.Match.Value);
    }

    /// <summary>
    /// Returns the number of elements in <c>self</c>, including the whole match plus all capture groups.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.size   # => 3
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return matchData.Match.Groups.Count;
    }

    /// <summary>
    /// Alias for <c>size</c>. Returns the number of elements in <c>self</c> (the whole match plus captures).
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.length   # => 3
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Length(MRubyState mrb, MRubyValue self)
    {
        return Size(mrb, self);
    }

    /// <summary>
    /// Returns the portion of the original string before the match.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /world/.match("hello world!")
    /// m.pre_match   # => "hello "
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue PreMatch(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var match = matchData.Match;
        return mrb.NewString(matchData.OriginalString.Substring(0, match.Index));
    }

    /// <summary>
    /// Returns the portion of the original string after the match.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /world/.match("hello world!")
    /// m.post_match   # => "!"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue PostMatch(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var match = matchData.Match;
        return mrb.NewString(matchData.OriginalString.Substring(match.Index + match.Length));
    }

    /// <summary>
    /// Returns the <c>Regexp</c> that produced <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+)/.match("hello")
    /// m.regexp   # => /(\w+)/
    /// </code>
    /// </example>
    [RubyDef("() -> Regexp")]
    public static MRubyValue Regexp(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return RegexpMembers.CreateRDataFromRegexp(mrb, matchData.Regexp);
    }

    /// <summary>
    /// Returns the frozen copy of the original string that was matched against.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /world/.match("hello world")
    /// m.string   # => "hello world"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue String(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var str = mrb.NewString(matchData.OriginalString);
        str.MarkAsFrozen();
        return str;
    }

    /// <summary>
    /// Returns a hash mapping each named capture group to its matched substring, or <c>nil</c> when the group did not participate.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(?&lt;y&gt;\d{4})-(?&lt;m&gt;\d{2})/.match("2024-01")
    /// m.named_captures   # => {"y" => "2024", "m" => "01"}
    /// </code>
    /// </example>
    [RubyDef("() -> Hash[String, String?]")]
    public static MRubyValue NamedCaptures(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var hash = mrb.NewHash(0);

        var groupNames = matchData.Regexp.Regex.GetGroupNames();
        foreach (var name in groupNames)
        {
            // Skip numeric group names
            if (int.TryParse(name, out _)) continue;

            var group = matchData.Match.Groups[name];
            if (group.Success)
            {
                hash[mrb.NewString(name)] = mrb.NewString(group.Value);
            }
            else
            {
                hash[mrb.NewString(name)] = MRubyValue.Nil;
            }
        }

        return hash;
    }

    /// <summary>
    /// Returns the list of named capture group names defined by the regexp that produced <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(?&lt;y&gt;\d{4})-(?&lt;m&gt;\d{2})/.match("2024-01")
    /// m.names   # => ["y", "m"]
    /// </code>
    /// </example>
    [RubyDef("() -> Array[String]")]
    public static MRubyValue NamesMethod(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return RegexpMembers.NamesMethod(mrb, RegexpMembers.CreateRDataFromRegexp(mrb, matchData.Regexp));
    }

    /// <summary>
    /// Returns an array of the elements at the given integer indices into <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+) (\w+)/.match("a b c")
    /// m.values_at(1, 3)   # => ["a", "c"]
    /// </code>
    /// </example>
    [RubyDef("(*Integer) -> Array[String?]")]
    public static MRubyValue ValuesAt(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var argc = mrb.GetArgumentCount();

        var array = mrb.NewArray(argc);
        for (var i = 0; i < argc; i++)
        {
            var indexArg = mrb.GetArgumentAt(i);
            var index = (int)mrb.AsInteger(indexArg);
            array.Push(GetByIndex(mrb, matchData, index));
        }
        return array;
    }

    /// <summary>
    /// Returns a human-readable representation of <c>self</c> showing the whole match and each capture group.
    /// </summary>
    /// <example>
    /// <code>
    /// m = /(\w+) (\w+)/.match("hello world")
    /// m.inspect   # => "#&lt;MatchData \"hello world\" 1:\"hello\" 2:\"world\"&gt;"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var sb = new StringBuilder();
        sb.Append("#<MatchData ");

        // Main match
        sb.Append('"');
        sb.Append(matchData.Match.Value);
        sb.Append('"');

        var groupNames = matchData.Regexp.Regex.GetGroupNames();
        var groups = matchData.Match.Groups;

        // Capture groups
        for (var i = 1; i < groups.Count; i++)
        {
            sb.Append(' ');

            // Check if this is a named capture
            var name = groupNames.Length > i && !int.TryParse(groupNames[i], out _) ? groupNames[i] : null;
            if (name != null)
            {
                sb.Append(name);
                sb.Append(':');
            }
            else
            {
                sb.Append(i);
                sb.Append(':');
            }

            var group = groups[i];
            if (group.Success)
            {
                sb.Append('"');
                sb.Append(group.Value);
                sb.Append('"');
            }
            else
            {
                sb.Append("nil");
            }
        }

        sb.Append('>');
        return mrb.NewString(sb.ToString());
    }
}
