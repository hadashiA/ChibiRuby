using System;

namespace ChibiRuby.StdLib;

/// <summary>Backing data for <c>HTTP::Response</c>; wrapper values are pre-allocated so accessors have stable identity.</summary>
internal sealed class MRubyHttpResponseData
{
    public int Status { get; }
    public string Uri { get; }
    public string Version { get; }
    public MRubyHttpHeadersData Headers { get; }
    public MRubyHttpBodyData Body { get; }

    public MRubyValue HeadersValue { get; set; }
    public MRubyValue BodyValue { get; set; }

    /// <summary>Cached <c>JSON.parse(body)</c>; populated by the first <c>#json</c> call.</summary>
    public MRubyValue? JsonCache;

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

/// <summary><c>HTTP::Response</c> — one HTTP exchange's buffered response. 4xx/5xx never raise; branch via <c>#success?</c> / <c>#ensure_success_status!</c>.</summary>
[RubyClass("HTTP::Response")]
static class HttpResponseMembers
{
    /// <summary>Numeric HTTP status code.</summary>
    [RubyDef("() -> Integer")]
    public static MRubyValue Status(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue((long)data.Status);
    }

    /// <summary>Response headers as <c>HTTP::Headers</c>.</summary>
    [RubyDef("() -> HTTP::Headers")]
    public static MRubyValue Headers(MRubyState mrb, MRubyValue self)
    {
        return GetData(mrb, self).HeadersValue;
    }

    /// <summary>Response body as <c>HTTP::Body</c>.</summary>
    [RubyDef("() -> HTTP::Body")]
    public static MRubyValue Body(MRubyState mrb, MRubyValue self)
    {
        return GetData(mrb, self).BodyValue;
    }

    /// <summary>Request URI as a String.</summary>
    [RubyDef("() -> String")]
    public static MRubyValue Uri(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Uri));
    }

    /// <summary>Negotiated HTTP protocol version, e.g. <c>"1.1"</c>.</summary>
    [RubyDef("() -> String")]
    public static MRubyValue Version(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(data.Version));
    }

    /// <summary>Content-Type header value, or nil.</summary>
    [RubyDef("() -> String?")]
    public static MRubyValue ContentType(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return data.Body.ContentType is null
            ? MRubyValue.Nil
            : new MRubyValue(mrb.NewString(data.Body.ContentType));
    }

    /// <summary>True iff status is 2xx.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue SuccessQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 200 and < 300 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>True iff status is 3xx (only observable when redirects are not followed).</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue RedirectQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 300 and < 400 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>True iff status is 4xx.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue ClientErrorQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 400 and < 500 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>True iff status is 5xx.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue ServerErrorQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 500 and < 600 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>True iff status is 4xx or 5xx.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue ErrorQ(MRubyState mrb, MRubyValue self)
    {
        var s = GetData(mrb, self).Status;
        return s is >= 400 and < 600 ? MRubyValue.True : MRubyValue.False;
    }

    /// <summary>Raises <c>HTTP::Error</c> when status ≥ 400; otherwise returns self.</summary>
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

    /// <summary>Parses the body via <c>JSON.parse</c> (cached). NotImplementedError unless <c>DefineJson()</c> was called.</summary>
    [RubyDef("() -> untyped")]
    public static MRubyValue Json(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        if (data.JsonCache is { } cached) return cached;

        if (!mrb.TryGetConst(mrb.Intern("JSON"u8), out var jsonConst) ||
            jsonConst.VType != MRubyVType.Module)
        {
            mrb.Raise(mrb.GetExceptionClass(mrb.Intern("NotImplementedError"u8)),
                "HTTP::Response#json requires the JSON module — call MRubyState.DefineJson() to enable it"u8);
        }

        var bodyString = new MRubyValue(mrb.NewString(data.Body.Bytes));
        var parsed = mrb.Send(jsonConst, mrb.Intern("parse"u8), bodyString);
        data.JsonCache = parsed;
        return parsed;
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState mrb, MRubyValue self)
    {
        var data = GetData(mrb, self);
        return new MRubyValue(mrb.NewString(
            $"#<HTTP::Response status={data.Status} uri={data.Uri} bytes={data.Body.Bytes.Length}>"));
    }

    /// <summary>Alias for <c>body.to_s</c>.</summary>
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
