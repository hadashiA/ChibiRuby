using System;
using System.Collections.Generic;

namespace ChibiRuby.StdLib;

/// <summary>
/// Case-insensitive header bag. Wraps an ordered list of
/// <c>(name, value)</c> pairs so duplicate-key headers (Set-Cookie, etc.)
/// survive a round-trip; lookups are CI per RFC 7230 §3.2.
/// <para>
/// The list is mutable to allow <c>HTTP::Headers#[]=</c>, but instances
/// returned on responses are exposed read-mostly — callers freeze them on
/// the Ruby side if needed.
/// </para>
/// </summary>
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

    /// <summary>Case-insensitive lookup; returns the first matching value or null.</summary>
    public string? Get(string name)
    {
        foreach (var kv in entries)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(kv.Key, name)) return kv.Value;
        }
        return null;
    }

    /// <summary>Replace every entry whose name matches case-insensitively. Adds if absent.</summary>
    public void Set(string name, string value)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entries[i].Key, name))
            {
                entries[i] = new KeyValuePair<string, string>(entries[i].Key, value);
                // Drop later duplicates so the assignment semantics match a Hash.
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

    /// <summary>Snapshot — used when an outer Session/call needs to capture
    /// the current entries without exposing the mutable backing list.</summary>
    public List<KeyValuePair<string, string>> SnapshotEntries() => new(entries);
}

/// <summary>
/// <c>HTTP::Headers</c> — read/write view over response (or session-default)
/// HTTP header fields. Indexing is case-insensitive
/// (<c>headers["content-type"] == headers["Content-Type"]</c>) and key order is
/// preserved so iteration matches the wire ordering.
/// </summary>
[RubyClass("HTTP::Headers")]
static class HttpHeadersMembers
{
    /// <summary><c>headers[name]</c> — case-insensitive lookup. Returns the
    /// first matching value, or nil if absent.</summary>
    [RubyDef("(String) -> String?")]
    public static MRubyValue OpAref(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var name = mrb.GetArgumentAsStringAt(0).ToString();
        var value = data.Get(name);
        return value is null ? MRubyValue.Nil : new MRubyValue(mrb.NewString(value));
    }

    /// <summary><c>headers[name] = value</c> — replaces every same-named entry
    /// (case-insensitive); appends if absent.</summary>
    [RubyDef("(String, String) -> String")]
    public static MRubyValue OpAset(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var name = mrb.GetArgumentAsStringAt(0).ToString();
        var value = mrb.GetArgumentAsStringAt(1).ToString();
        data.Set(name, value);
        return new MRubyValue(mrb.NewString(value));
    }

    /// <summary><c>headers.key?(name)</c> — true iff the name is set
    /// (case-insensitive).</summary>
    [RubyDef("(String) -> bool")]
    public static MRubyValue KeyQ(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var name = mrb.GetArgumentAsStringAt(0).ToString();
        return data.ContainsKey(name) ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary><c>headers.each { |name, value| … }</c> — yields each entry in
    /// insertion order. Returns an Array of <c>[name, value]</c> pairs (the
    /// yielded values) so it works as an iterator chain even without a block.</summary>
    [RubyDef("() { (String, String) -> void } -> Array[Array[String]]")]
    public static MRubyValue Each(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var block = mrb.GetBlockArgument(optional: true);
        if (block is null)
        {
            // Block-less: return Array of pairs. (No Enumerator support in v1.)
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

    /// <summary><c>headers.to_h</c> — flat <c>Hash[String, String]</c> snapshot.
    /// Duplicate header names collapse to the last value (Ruby Hash semantics).</summary>
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

    /// <summary><c>headers.size</c> — number of header entries (counting
    /// duplicates separately).</summary>
    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState mrb, MRubyValue self)
    {
        return new MRubyValue((long)GetData(mrb, self).Count);
    }

    /// <summary><c>headers.inspect</c> — Ruby-debug representation as a Hash
    /// literal of the underlying entries (duplicates collapse).</summary>
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
