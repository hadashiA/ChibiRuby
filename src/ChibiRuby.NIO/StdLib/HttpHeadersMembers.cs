using System;
using System.Collections.Generic;

namespace ChibiRuby.StdLib;

/// <summary>Ordered (name, value) pair list with case-insensitive lookup; duplicate names (Set-Cookie etc.) are preserved.</summary>
internal sealed class MRubyHttpHeadersData
{
    readonly List<KeyValuePair<string, string>> entries;

    public MRubyHttpHeadersData()
    {
        entries = new List<KeyValuePair<string, string>>();
    }

    public MRubyHttpHeadersData(IEnumerable<KeyValuePair<string, string>> initial)
    {
        entries = new List<KeyValuePair<string, string>>(initial);
    }

    public int Count => entries.Count;

    public IReadOnlyList<KeyValuePair<string, string>> Entries => entries;

    public string? Get(string name)
    {
        foreach (var kv in entries)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(kv.Key, name)) return kv.Value;
        }
        return null;
    }

    public void Set(string name, string value)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entries[i].Key, name))
            {
                entries[i] = new KeyValuePair<string, string>(entries[i].Key, value);
                // Drop later duplicates: assignment behaves like a Hash.
                for (var j = entries.Count - 1; j > i; j--)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(entries[j].Key, name))
                        entries.RemoveAt(j);
                }
                return;
            }
        }
        entries.Add(new KeyValuePair<string, string>(name, value));
    }

    public bool ContainsKey(string name)
    {
        foreach (var kv in entries)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(kv.Key, name)) return true;
        }
        return false;
    }

    public List<KeyValuePair<string, string>> SnapshotEntries() => new(entries);
}

/// <summary><c>HTTP::Headers</c> — case-insensitive, order-preserving header fields.</summary>
[RubyClass("HTTP::Headers")]
static class HttpHeadersMembers
{
    /// <summary>Case-insensitive lookup; first matching value or nil.</summary>
    [RubyDef("(String) -> String?")]
    public static MRubyValue OpAref(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var name = mrb.GetArgumentAsStringAt(0).ToString();
        var value = data.Get(name);
        return value is null ? MRubyValue.Nil : new MRubyValue(mrb.NewString(value));
    }

    /// <summary>Replaces every same-named entry (case-insensitive); appends if absent.</summary>
    [RubyDef("(String, String) -> String")]
    public static MRubyValue OpAset(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var name = mrb.GetArgumentAsStringAt(0).ToString();
        var value = mrb.GetArgumentAsStringAt(1).ToString();
        data.Set(name, value);
        return new MRubyValue(mrb.NewString(value));
    }

    /// <summary>True iff the name is present (case-insensitive).</summary>
    [RubyDef("(String) -> bool")]
    public static MRubyValue KeyQ(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var name = mrb.GetArgumentAsStringAt(0).ToString();
        return data.ContainsKey(name) ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>Yields each entry in insertion order; without a block returns an Array of pairs.</summary>
    [RubyDef("() { (String, String) -> void } -> Array[Array[String]]")]
    public static MRubyValue Each(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var block = mrb.GetBlockArgument(optional: true);
        if (block is null)
        {
            var pairs = new MRubyValue[data.Count];
            var idx = 0;
            foreach (var kv in data.Entries)
            {
                var pair = new MRubyValue[]
                {
                    new(mrb.NewString(kv.Key)),
                    new(mrb.NewString(kv.Value)),
                };
                pairs[idx++] = new MRubyValue(mrb.NewArray(pair));
            }
            return new MRubyValue(mrb.NewArray(pairs));
        }

        var selfClass = self.As<RObject>().Class;
        foreach (var kv in data.Entries)
        {
            var name = new MRubyValue(mrb.NewString(kv.Key));
            var value = new MRubyValue(mrb.NewString(kv.Value));
            mrb.YieldWithClass(selfClass, self, new ReadOnlySpan<MRubyValue>(new[] { name, value }), block);
        }
        return self;
    }

    /// <summary>Hash snapshot; duplicate names collapse to the last value.</summary>
    [RubyDef("() -> Hash[String, String]")]
    public static MRubyValue ToH(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var hash = mrb.NewHash(data.Count);
        foreach (var kv in data.Entries)
        {
            hash.Add(new MRubyValue(mrb.NewString(kv.Key)), new MRubyValue(mrb.NewString(kv.Value)));
        }
        return new MRubyValue(hash);
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState mrb, MRubyValue self)
    {
        return new MRubyValue((long)GetData(mrb, self).Count);
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var sb = new System.Text.StringBuilder("#<HTTP::Headers");
        var first = true;
        foreach (var kv in data.Entries)
        {
            sb.Append(first ? ' ' : ',').Append(' ');
            first = false;
            sb.Append('"').Append(kv.Key).Append('"').Append("=>").Append('"').Append(kv.Value).Append('"');
        }
        sb.Append('>');
        return new MRubyValue(mrb.NewString(sb.ToString()));
    }

    internal static MRubyHttpHeadersData GetData(MRubyState mrb, MRubyValue self)
    {
        if (self.Object is RData { Data: MRubyHttpHeadersData d }) return d;
        mrb.Raise(Names.TypeError, "not an HTTP::Headers"u8);
        return null!;
    }
}
