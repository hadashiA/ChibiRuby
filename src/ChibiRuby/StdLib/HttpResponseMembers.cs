using System;

namespace ChibiRuby.StdLib;

/// <summary>
/// Backing data for <c>HTTP::Response</c>. Built once during dispatch and
/// stored on the <see cref="RData"/> payload. The associated
/// <c>HTTP::Headers</c> / <c>HTTP::Body</c> wrappers are pre-allocated as
/// <see cref="MRubyValue"/>s during dispatch so <c>#headers</c> and
/// <c>#body</c> are constant-time getters with stable identity.
/// </summary>
internal sealed class MRubyHttpResponseData
{
    public int Status { get; }
    public string Uri { get; }
    public string Version { get; }
    public MRubyHttpHeadersData Headers { get; }
    public MRubyHttpBodyData Body { get; }

    /// <summary>Lazily-created (during response construction) wrapper value
    /// for the headers — exposed so accessors don't reallocate per call.</summary>
    public MRubyValue HeadersValue { get; set; }

    /// <summary>Same idea for the body.</summary>
    public MRubyValue BodyValue { get; set; }

    public MRubyHttpResponseData(
        int status,
        string uri,
        string version,
        MRubyHttpHeadersData headers,
        MRubyHttpBodyData body)
    {
        Status = status;
        Uri = uri;
        Version = version;
        Headers = headers;
        Body = body;
    }
}

/// <summary>
/// <c>HTTP::Response</c> — one HTTP exchange's response. The body is
/// already-buffered by the time this object exists; access it with
/// <c>#body</c> (which gives you an <c>HTTP::Body</c>) or convert to a
/// String via <c>#body.to_s</c>.
/// <para>
/// 4xx / 5xx responses are returned to Ruby without raising (matching
/// <c>HttpClient</c>'s default). Use <c>#success?</c> / <c>#error?</c> /
/// <c>#ensure_success_status!</c> to branch on the result.
/// </para>
/// </summary>
[RubyClass("HTTP::Response")]
static class HttpResponseMembers
{
    /// <summary><c>response.status</c> — numeric HTTP status code (200, 404, …).</summary>
    [RubyDef("() -> Integer")]
    public static MRubyValue Status(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue((long)data.Status);
    }

    /// <summary><c>response.headers</c> — <c>HTTP::Headers</c> bag for this
    /// response. Identity is stable across calls.</summary>
    [RubyDef("() -> HTTP::Headers")]
    public static MRubyValue Headers(MRubyState mrb, MRubyValue self)
    {
        return GetData(mrb, self).HeadersValue;
    }

    /// <summary><c>response.body</c> — <c>HTTP::Body</c> wrapper. Call
    /// <c>.to_s</c> for the raw String.</summary>
    [RubyDef("() -> HTTP::Body")]
    public static MRubyValue Body(MRubyState mrb, MRubyValue self)
    {
        return GetData(mrb, self).BodyValue;
    }

    /// <summary><c>response.uri</c> — the final request URI as a String
    /// (after redirect following, if any was performed by the client).</summary>
    [RubyDef("() -> String")]
    public static MRubyValue Uri(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Uri));
    }

    /// <summary><c>response.version</c> — HTTP protocol version negotiated
    /// for this response (e.g. <c>"1.1"</c>, <c>"2"</c>).</summary>
    [RubyDef("() -> String")]
    public static MRubyValue Version(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Version));
    }

    /// <summary><c>response.content_type</c> — the parsed Content-Type
    /// header value (or nil). Convenience over <c>response.headers["content-type"]</c>.</summary>
    [RubyDef("() -> String?")]
    public static MRubyValue ContentType(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return data.Body.ContentType is null
            ? MRubyValue.Nil
            : new MRubyValue(mrb.NewString(data.Body.ContentType));
    }

    /// <summary><c>response.success?</c> — true iff <c>200 ≤ status &lt; 300</c>.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue SuccessQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 200 and < 300 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary><c>response.redirect?</c> — true iff <c>300 ≤ status &lt; 400</c>.
    /// Note: by default the HTTP client transparently follows redirects, so
    /// you only see one of these if you set <c>follow_redirects: false</c>.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue RedirectQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 300 and < 400 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary><c>response.client_error?</c> — true iff <c>400 ≤ status &lt; 500</c>.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue ClientErrorQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 400 and < 500 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary><c>response.server_error?</c> — true iff <c>500 ≤ status &lt; 600</c>.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue ServerErrorQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 500 and < 600 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary><c>response.error?</c> — alias for <c>client_error? || server_error?</c>.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue ErrorQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 400 and < 600 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>
    /// <c>response.ensure_success_status!</c> — raise <c>HTTP::Error</c> if
    /// the status indicates failure (≥ 400); otherwise return <c>self</c> so
    /// the call chains. Modeled after
    /// <see cref="System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode"/>.
    /// </summary>
    [RubyDef("() -> self")]
    public static MRubyValue EnsureSuccessStatusBang(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        if (data.Status < 400) return self;

        var httpModule = mrb.GetConst(mrb.Intern("HTTP"u8)).As<RClass>();
        var errorClass = mrb.GetConst(mrb.Intern("Error"u8), httpModule).As<RClass>();
        mrb.Raise(errorClass, mrb.NewString($"HTTP {data.Status} for {data.Uri}"));
        return MRubyValue.Nil; // unreachable
    }

    /// <summary><c>response.inspect</c> — debug summary (status, uri, byte length).</summary>
    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(
            $"#<HTTP::Response status={data.Status} uri={data.Uri} bytes={data.Body.Bytes.Length}>"));
    }

    /// <summary><c>response.to_s</c> — alias for <c>body.to_s</c>. Mirrors the
    /// convenience HTTPX offers so <c>puts HTTP.get(url)</c> prints the body.</summary>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Body.Bytes));
    }

    internal static MRubyHttpResponseData GetData(MRubyState mrb, MRubyValue self)
    {
        if (self.Object is RData { Data: MRubyHttpResponseData d }) return d;
        mrb.Raise(Names.TypeError, "not an HTTP::Response"u8);
        return null!;
    }
}
