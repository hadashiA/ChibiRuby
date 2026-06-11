using System;

namespace ChibiRuby.StdLib;

/// <summary>Buffered response body bytes plus Content-Type. The wrapper class leaves room for future streaming.</summary>
internal sealed class MRubyHttpBodyData
{
    public byte[] Bytes { get; }
    public string? ContentType { get; }

    public MRubyHttpBodyData(byte[] bytes, string? contentType)
    {
        Bytes = bytes;
        ContentType = contentType;
    }
}

/// <summary><c>HTTP::Body</c> — fully buffered HTTP response body.</summary>
[RubyClass("HTTP::Body")]
static class HttpBodyMembers
{
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Bytes));
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Bytesize(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue((long)data.Bytes.Length);
    }

    /// <summary>Content-Type header value, or nil.</summary>
    [RubyDef("() -> String?")]
    public static MRubyValue ContentType(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return data.ContentType is null
            ? MRubyValue.Nil
            : new MRubyValue(mrb.NewString(data.ContentType));
    }

    [RubyDef("() -> bool")]
    public static MRubyValue EmptyQ(MRubyState mrb, MRubyValue self)
    {
        return GetData(mrb, self).Bytes.Length == 0 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>Yields the body to the block — always one chunk in v1; the shape allows future streaming.</summary>
    [RubyDef("() { (String) -> void } -> self")]
    public static MRubyValue Each(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var block = mrb.GetBlockArgument(optional: false)!;
        var selfClass = self.As<RObject>().Class;
        var chunk = new MRubyValue(mrb.NewString(data.Bytes));
        mrb.YieldWithClass(selfClass, self, new ReadOnlySpan<MRubyValue>(new[] { chunk }), block);
        return self;
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        var ct = data.ContentType is null ? "" : $" content_type={data.ContentType}";
        return new MRubyValue(mrb.NewString($"#<HTTP::Body bytesize={data.Bytes.Length}{ct}>"));
    }

    internal static MRubyHttpBodyData GetData(MRubyState mrb, MRubyValue self)
    {
        if (self.Object is RData { Data: MRubyHttpBodyData d }) return d;
        mrb.Raise(Names.TypeError, "not an HTTP::Body"u8);
        return null!;
    }
}
