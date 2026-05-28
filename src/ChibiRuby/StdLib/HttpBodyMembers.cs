using System;

namespace ChibiRuby.StdLib;

/// <summary>
/// Backing data for <c>HTTP::Body</c>. Holds the buffered response body as a
/// byte array plus the response's <c>Content-Type</c>. v1 reads the whole
/// body eagerly during dispatch; making <c>Body</c> a wrapper class (rather
/// than handing back a raw String) leaves room to add streaming support later
/// without breaking the Ruby surface.
/// </summary>
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

/// <summary>
/// <c>HTTP::Body</c> — buffered HTTP response body. The full bytes have
/// already been read by the time you see this object; <c>#to_s</c> gives you
/// the body as a String, <c>#bytesize</c> the byte length without forcing the
/// decode. Iteration via <c>#each</c> yields the body in a single chunk in
/// v1 (no streaming) so calling code written against an eventual streaming
/// implementation still works.
/// </summary>
[RubyClass("HTTP::Body")]
static class HttpBodyMembers
{
    /// <summary><c>body.to_s</c> — the response body as a String (raw bytes;
    /// the caller decides how to decode it).</summary>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Bytes));
    }

    /// <summary><c>body.bytesize</c> — the byte length of the buffered body.</summary>
    [RubyDef("() -> Integer")]
    public static MRubyValue Bytesize(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue((long)data.Bytes.Length);
    }

    /// <summary><c>body.content_type</c> — the parsed Content-Type header
    /// value, or nil if absent.</summary>
    [RubyDef("() -> String?")]
    public static MRubyValue ContentType(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return data.ContentType is null
            ? MRubyValue.Nil
            : new MRubyValue(mrb.NewString(data.ContentType));
    }

    /// <summary><c>body.empty?</c> — true iff the body has zero bytes.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue EmptyQ(MRubyState mrb, MRubyValue self)
    {
        return GetData(mrb, self).Bytes.Length == 0 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>
    /// <c>body.each { |chunk| … }</c> — yields the body to the block. v1
    /// always yields a single chunk; the iteration shape is preserved so a
    /// future streaming implementation can yield piecewise without breaking
    /// existing Ruby code.
    /// </summary>
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

    /// <summary><c>body.inspect</c> — preview of the body length, no contents.</summary>
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
