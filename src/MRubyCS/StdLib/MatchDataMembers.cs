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

    // MatchData#[](index) or MatchData#[](name) or MatchData#[](range)
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

    // MatchData#begin(n)
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

    // MatchData#end(n)
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

    // MatchData#offset(n)
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

    // MatchData#captures
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

    // MatchData#to_a
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

    // MatchData#to_s
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return mrb.NewString(matchData.Match.Value);
    }

    // MatchData#size
    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return matchData.Match.Groups.Count;
    }

    // MatchData#length - alias for size
    [RubyDef("() -> Integer")]
    public static MRubyValue Length(MRubyState mrb, MRubyValue self)
    {
        return Size(mrb, self);
    }

    // MatchData#pre_match
    [RubyDef("() -> String")]
    public static MRubyValue PreMatch(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var match = matchData.Match;
        return mrb.NewString(matchData.OriginalString.Substring(0, match.Index));
    }

    // MatchData#post_match
    [RubyDef("() -> String")]
    public static MRubyValue PostMatch(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var match = matchData.Match;
        return mrb.NewString(matchData.OriginalString.Substring(match.Index + match.Length));
    }

    // MatchData#regexp
    [RubyDef("() -> Regexp")]
    public static MRubyValue Regexp(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return RegexpMembers.CreateRDataFromRegexp(mrb, matchData.Regexp);
    }

    // MatchData#string
    [RubyDef("() -> String")]
    public static MRubyValue String(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        var str = mrb.NewString(matchData.OriginalString);
        str.MarkAsFrozen();
        return str;
    }

    // MatchData#named_captures
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

    // MatchData#names
    [RubyDef("() -> Array[String]")]
    public static MRubyValue NamesMethod(MRubyState mrb, MRubyValue self)
    {
        var matchData = GetMatchData(mrb, self);
        return RegexpMembers.NamesMethod(mrb, RegexpMembers.CreateRDataFromRegexp(mrb, matchData.Regexp));
    }

    // MatchData#values_at(*indices)
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

    // MatchData#inspect
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
