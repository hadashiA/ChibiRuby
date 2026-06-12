using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChibiRuby.NIO;

/// <summary>Process-wide <see cref="HttpClient"/> singleton (avoids socket exhaustion); all per-request state lives on the request message.</summary>
internal static class MRubyHttpClientHolder
{
    static HttpClient? cached;
    static readonly object gate = new();

    public static HttpClient Default
    {
        get
        {
            if (cached is { } existing) return existing;
            lock (gate)
            {
                if (cached is not null) return cached;
                var c = new HttpClient
                {
                    // Timeouts are per-request via CancellationToken.
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                cached = c;
                return c;
            }
        }
    }
}

/// <summary>Built-out plan for one HTTP request.</summary>
internal sealed class MRubyHttpRequestPlan
{
    public required HttpMethod Method;
    public required Uri RequestUri;
    public List<KeyValuePair<string, string>>? Headers;
    public HttpContent? Content;
    public TimeSpan? OperationTimeout;
    public (string User, string Password)? BasicAuth;
}

/// <summary>Shared dispatch for the <c>HTTP.*</c> methods: parks the fiber when a scheduler is active, otherwise blocks on the same async pipeline.</summary>
internal static class MRubyHttpExecutor
{
    public static MRubyValue Dispatch(MRubyState mrb, HttpMethod method)
    {
        var argc = mrb.GetArgumentCount();
        if (argc < 1)
        {
            mrb.Raise(Names.ArgumentError, "wrong number of arguments (given 0, expected 1)"u8);
        }
        if (argc > 1)
        {
            mrb.Raise(Names.ArgumentError, "wrong number of arguments (expected 1 url; multiple urls are not supported)"u8);
        }
        var uri = ParseUri(mrb, mrb.GetArgumentAt(0));

        var opts = ReadOptions(mrb);
        if (opts.PendingParams is { } extraParams && extraParams.Count > 0)
        {
            uri = AppendQuery(uri, extraParams);
        }

        var plan = BuildPlan(method, uri, opts);

        // Resolve classes before parking; the async lambda must not touch the constant table.
        var responseClass = ResolveHttpClass(mrb, "Response"u8);
        var headersClass = ResolveHttpClass(mrb, "Headers"u8);
        var bodyClass = ResolveHttpClass(mrb, "Body"u8);

        return Execute(mrb, plan, responseClass, headersClass, bodyClass);
    }

    /// <summary><c>HTTP.request(:verb, url, **opts)</c> form.</summary>
    public static MRubyValue DispatchExplicitMethod(MRubyState mrb)
    {
        var argc = mrb.GetArgumentCount();
        if (argc < 2)
        {
            mrb.Raise(Names.ArgumentError, "wrong number of arguments (expected verb and url)"u8);
        }
        if (argc > 2)
        {
            mrb.Raise(Names.ArgumentError, "wrong number of arguments (expected verb and a single url)"u8);
        }
        var method = ParseMethod(mrb, mrb.GetArgumentAt(0));
        var uri = ParseUri(mrb, mrb.GetArgumentAt(1));

        var opts = ReadOptions(mrb);
        if (opts.PendingParams is { } extraParams && extraParams.Count > 0)
        {
            uri = AppendQuery(uri, extraParams);
        }

        var plan = BuildPlan(method, uri, opts);
        var responseClass = ResolveHttpClass(mrb, "Response"u8);
        var headersClass = ResolveHttpClass(mrb, "Headers"u8);
        var bodyClass = ResolveHttpClass(mrb, "Body"u8);

        return Execute(mrb, plan, responseClass, headersClass, bodyClass);
    }

    static RClass ResolveHttpClass(MRubyState mrb, ReadOnlySpan<byte> name)
    {
        var httpModule = mrb.GetConst(mrb.Intern("HTTP"u8)).As<RClass>();
        return mrb.GetConst(mrb.Intern(name), httpModule).As<RClass>();
    }

    static Uri ParseUri(MRubyState mrb, MRubyValue value)
    {
        if (value.Object is not RString str)
        {
            mrb.Raise(Names.TypeError, "URL must be a String"u8);
            return null!;
        }
        var raw = str.ToString();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            mrb.Raise(Names.ArgumentError, $"invalid URL: {raw}");
        }
        if (uri!.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            mrb.Raise(Names.ArgumentError, $"unsupported URL scheme: {uri.Scheme}");
        }
        return uri;
    }

    static HttpMethod ParseMethod(MRubyState mrb, MRubyValue value)
    {
        string verb;
        if (value.VType == MRubyVType.Symbol)
        {
            verb = mrb.NameOf(value.SymbolValue).ToString();
        }
        else if (value.Object is RString s)
        {
            verb = s.ToString();
        }
        else
        {
            mrb.Raise(Names.TypeError, "HTTP verb must be a Symbol or String"u8);
            return default!;
        }
        return verb.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            "PATCH" => new HttpMethod("PATCH"),
            "TRACE" => new HttpMethod("TRACE"),
            _ => new HttpMethod(verb.ToUpperInvariant()),
        };
    }

    static CallOptions ReadOptions(MRubyState mrb)
    {
        var opts = new CallOptions();

        if (mrb.TryGetKeywordArgument(mrb.Intern("headers"u8), out var headersValue))
        {
            opts.Headers = ReadHeaderMap(mrb, headersValue);
        }

        if (mrb.TryGetKeywordArgument(mrb.Intern("params"u8), out var paramsValue))
        {
            opts.PendingParams = ReadStringMap(mrb, paramsValue, "params");
        }

        if (mrb.TryGetKeywordArgument(mrb.Intern("body"u8), out var bodyValue))
        {
            opts.BodyBytes = ReadBodyBytes(mrb, bodyValue);
        }

        if (mrb.TryGetKeywordArgument(mrb.Intern("form"u8), out var formValue))
        {
            opts.FormEntries = ReadStringMap(mrb, formValue, "form");
        }

        if (mrb.TryGetKeywordArgument(mrb.Intern("json"u8), out var jsonValue))
        {
            opts.BodyBytes = EncodeJsonBody(mrb, jsonValue);
            opts.JsonContentType = true;
        }

        if (mrb.TryGetKeywordArgument(mrb.Intern("timeout"u8), out var timeoutValue))
        {
            opts.OperationTimeout = ReadTimeoutSeconds(mrb, timeoutValue);
        }

        if (mrb.TryGetKeywordArgument(mrb.Intern("basic_auth"u8), out var basicAuthValue))
        {
            opts.BasicAuth = ReadBasicAuth(mrb, basicAuthValue);
        }

        if (opts.FormEntries is not null && opts.BodyBytes is not null)
        {
            mrb.Raise(Names.ArgumentError, "specify only one of body: or form:"u8);
        }

        return opts;
    }

    static List<KeyValuePair<string, string>> ReadHeaderMap(MRubyState mrb, MRubyValue value)
    {
        if (value.Object is RData { Data: MRubyHttpHeadersData hdrs })
        {
            return hdrs.SnapshotEntries();
        }
        if (value.Object is not RHash hash)
        {
            mrb.Raise(Names.TypeError, "headers: must be a Hash or HTTP::Headers"u8);
            return null!;
        }
        var list = new List<KeyValuePair<string, string>>(hash.Length);
        foreach (var entry in hash)
        {
            list.Add(new KeyValuePair<string, string>(
                CoerceStringKey(mrb, entry.Key, "headers"),
                CoerceStringValue(mrb, entry.Value, "headers")));
        }
        return list;
    }

    static List<KeyValuePair<string, string>> ReadStringMap(MRubyState mrb, MRubyValue value, string label)
    {
        if (value.Object is not RHash hash)
        {
            mrb.Raise(Names.TypeError, $"{label}: must be a Hash");
            return null!;
        }
        var list = new List<KeyValuePair<string, string>>(hash.Length);
        foreach (var entry in hash)
        {
            list.Add(new KeyValuePair<string, string>(
                CoerceStringKey(mrb, entry.Key, label),
                CoerceStringValue(mrb, entry.Value, label)));
        }
        return list;
    }

    static byte[] ReadBodyBytes(MRubyState mrb, MRubyValue value)
    {
        if (value.IsNil) return Array.Empty<byte>();
        if (value.Object is RString str)
        {
            return str.AsSpan().ToArray();
        }
        mrb.Raise(Names.TypeError, "body: must be a String"u8);
        return null!;
    }

    static TimeSpan ReadTimeoutSeconds(MRubyState mrb, MRubyValue value)
    {
        double seconds;
        switch (value.VType)
        {
            case MRubyVType.Integer:
                seconds = value.IntegerValue;
                break;
            case MRubyVType.Float:
                seconds = value.FloatValue;
                break;
            default:
                mrb.Raise(Names.TypeError, "timeout: must be a Number"u8);
                return default;
        }
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
        {
            mrb.Raise(Names.ArgumentError, "timeout: must be a positive finite Number"u8);
        }
        return TimeSpan.FromSeconds(seconds);
    }

    static (string User, string Password) ReadBasicAuth(MRubyState mrb, MRubyValue value)
    {
        if (value.Object is RArray arr && arr.Length == 2)
        {
            return (CoerceStringValue(mrb, arr[0], "basic_auth"),
                    CoerceStringValue(mrb, arr[1], "basic_auth"));
        }
        if (value.Object is RHash h)
        {
            var u = h.TryGetValue(new MRubyValue(mrb.Intern("user"u8)), out var uv)
                ? CoerceStringValue(mrb, uv, "basic_auth")
                : null;
            var p = h.TryGetValue(new MRubyValue(mrb.Intern("password"u8)), out var pv)
                ? CoerceStringValue(mrb, pv, "basic_auth")
                : null;
            if (u is not null && p is not null) return (u, p);
        }
        mrb.Raise(Names.ArgumentError, "basic_auth: expected [user, password] or {user:, password:}"u8);
        return default;
    }

    static string CoerceStringKey(MRubyState mrb, MRubyValue value, string label)
    {
        if (value.Object is RString s) return s.ToString();
        if (value.VType == MRubyVType.Symbol) return mrb.NameOf(value.SymbolValue).ToString();
        mrb.Raise(Names.TypeError, $"{label}: keys must be String or Symbol");
        return default!;
    }

    static string CoerceStringValue(MRubyState mrb, MRubyValue value, string label)
    {
        if (value.Object is RString s) return s.ToString();
        if (value.VType == MRubyVType.Symbol) return mrb.NameOf(value.SymbolValue).ToString();
        if (value.IsInteger) return value.IntegerValue.ToString();
        if (value.IsFloat) return value.FloatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (value.VType == MRubyVType.True) return "true";
        if (value.VType == MRubyVType.False) return "false";
        mrb.Raise(Names.TypeError, $"{label}: values must be String, Symbol, Numeric, or Boolean");
        return default!;
    }

    sealed class CallOptions
    {
        public List<KeyValuePair<string, string>>? Headers;
        public List<KeyValuePair<string, string>>? PendingParams;
        public byte[]? BodyBytes;
        public List<KeyValuePair<string, string>>? FormEntries;
        public TimeSpan? OperationTimeout;
        public (string User, string Password)? BasicAuth;
        /// <summary>True when <c>json:</c> was used; adds <c>Content-Type: application/json</c>.</summary>
        public bool JsonContentType;
    }

    static MRubyHttpRequestPlan BuildPlan(HttpMethod method, Uri uri, CallOptions opts)
    {
        var plan = new MRubyHttpRequestPlan
        {
            Method = method,
            RequestUri = uri,
            Headers = opts.Headers,
            OperationTimeout = opts.OperationTimeout,
            BasicAuth = opts.BasicAuth,
        };

        if (opts.FormEntries is { } form)
        {
            plan.Content = new FormUrlEncodedContent(form);
        }
        else if (opts.BodyBytes is { } body)
        {
            plan.Content = new ByteArrayContent(body);
            if (opts.JsonContentType)
            {
                plan.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            }
        }

        return plan;
    }

    /// <summary>Dispatches <c>JSON.generate</c> via the constant table; NotImplementedError when the JSON module is absent (soft dependency).</summary>
    static byte[] EncodeJsonBody(MRubyState mrb, MRubyValue value)
    {
        if (!mrb.TryGetConst(mrb.Intern("JSON"u8), out var jsonConst) ||
            jsonConst.VType != MRubyVType.Module)
        {
            mrb.Raise(mrb.GetExceptionClass(mrb.Intern("NotImplementedError"u8)),
                "json: option requires the JSON module — call MRubyState.DefineJson() to enable it"u8);
        }
        var rendered = mrb.Send(jsonConst, mrb.Intern("generate"u8), value);
        if (rendered.Object is not RString s)
        {
            mrb.Raise(Names.TypeError, "JSON.generate did not return a String"u8);
            return null!;
        }
        return s.AsSpan().ToArray();
    }

    static Uri AppendQuery(Uri baseUri, List<KeyValuePair<string, string>> extras)
    {
        var builder = new UriBuilder(baseUri);
        var existing = builder.Query;
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(existing))
        {
            // Strip the leading '?'; UriBuilder re-adds it on assignment.
            sb.Append(existing.StartsWith('?') ? existing.AsSpan(1) : existing.AsSpan());
        }
        foreach (var kv in extras)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(WebUtility.UrlEncode(kv.Key));
            sb.Append('=');
            sb.Append(WebUtility.UrlEncode(kv.Value));
        }
        builder.Query = sb.ToString();
        return builder.Uri;
    }

    static MRubyValue Execute(
        MRubyState mrb,
        MRubyHttpRequestPlan plan,
        RClass responseClass,
        RClass headersClass,
        RClass bodyClass)
    {
        if (mrb.TryGetActiveFiberScheduler(out var scheduler))
        {
            scheduler.Await(async _ =>
            {
                HttpCapture captured;
                try
                {
                    captured = await ExecuteOneAsync(plan).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MapException(mrb, ex);
                    return MRubyValue.Nil;
                }
                return BuildResponseValue(mrb, plan, captured, responseClass, headersClass, bodyClass);
            });
            return MRubyValue.Nil;
        }

        // Sync path: SendAsync doesn't capture a SynchronizationContext, so blocking is safe.
        HttpCapture capturedSync;
        try
        {
            capturedSync = ExecuteOneAsync(plan).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MapException(mrb, ex);
            return MRubyValue.Nil;
        }
        return BuildResponseValue(mrb, plan, capturedSync, responseClass, headersClass, bodyClass);
    }

    static MRubyValue BuildResponseValue(
        MRubyState mrb,
        MRubyHttpRequestPlan plan,
        HttpCapture cap,
        RClass responseClass,
        RClass headersClass,
        RClass bodyClass)
    {
        var headers = new MRubyHttpHeadersData(cap.Headers);
        var body = new MRubyHttpBodyData(cap.BodyBytes, cap.ContentType);
        var response = new MRubyHttpResponseData(
            status: cap.Status,
            uri: plan.RequestUri.ToString(),
            version: cap.Version,
            headers: headers,
            body: body);

        var responseValue = new MRubyValue(new RData(responseClass, response));
        // Pre-built so #headers / #body are allocation-free with stable identity.
        response.HeadersValue = new MRubyValue(new RData(headersClass, headers));
        response.BodyValue = new MRubyValue(new RData(bodyClass, body));
        return responseValue;
    }

    /// <summary>Issues one request and buffers the whole response body (no streaming in v1).</summary>
    static async Task<HttpCapture> ExecuteOneAsync(MRubyHttpRequestPlan plan)
    {
        var request = new HttpRequestMessage(plan.Method, plan.RequestUri);
        if (plan.Content is not null)
        {
            request.Content = plan.Content;
        }
        if (plan.Headers is not null)
        {
            foreach (var kv in plan.Headers)
            {
                ApplyHeader(request, kv.Key, kv.Value);
            }
        }
        if (plan.BasicAuth is { } cred)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.User}:{cred.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        using var cts = plan.OperationTimeout is { } to
            ? new CancellationTokenSource(to)
            : new CancellationTokenSource();

        try
        {
            using var resp = await MRubyHttpClientHolder.Default
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            var bytes = resp.Content is null
                ? Array.Empty<byte>()
                : await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            var headerList = new List<KeyValuePair<string, string>>();
            foreach (var h in resp.Headers)
            {
                foreach (var v in h.Value) headerList.Add(new KeyValuePair<string, string>(h.Key, v));
            }
            if (resp.Content is not null)
            {
                foreach (var h in resp.Content.Headers)
                {
                    foreach (var v in h.Value) headerList.Add(new KeyValuePair<string, string>(h.Key, v));
                }
            }

            return new HttpCapture
            {
                Status = (int)resp.StatusCode,
                Headers = headerList,
                BodyBytes = bytes,
                Version = resp.Version.ToString(),
                ContentType = resp.Content?.Headers.ContentType?.ToString(),
            };
        }
        catch (OperationCanceledException) when (plan.OperationTimeout is not null && cts.IsCancellationRequested)
        {
            throw new MRubyHttpTimeoutException();
        }
    }

    static void ApplyHeader(HttpRequestMessage request, string name, string value)
    {
        // Content-Type etc. live on the content headers, not the request headers.
        if (request.Headers.TryAddWithoutValidation(name, value)) return;
        request.Content?.Headers.TryAddWithoutValidation(name, value);
    }

    /// <summary>Response data snapshot, decoupled from <see cref="HttpResponseMessage"/>.</summary>
    sealed class HttpCapture
    {
        public int Status;
        public List<KeyValuePair<string, string>> Headers = new();
        public byte[] BodyBytes = Array.Empty<byte>();
        public string Version = "1.1";
        public string? ContentType;
    }

    internal static void MapException(MRubyState mrb, Exception ex)
    {
        var httpModule = mrb.GetConst(mrb.Intern("HTTP"u8)).As<RClass>();

        if (ex is AggregateException agg && agg.InnerException is not null)
        {
            ex = agg.InnerException;
        }

        switch (ex)
        {
            case MRubyHttpTimeoutException:
            {
                var klass = mrb.GetConst(mrb.Intern("TimeoutError"u8), httpModule).As<RClass>();
                mrb.Raise(klass, "HTTP request timed out"u8);
                return;
            }
            case TaskCanceledException tce when tce.InnerException is TimeoutException:
            {
                var klass = mrb.GetConst(mrb.Intern("TimeoutError"u8), httpModule).As<RClass>();
                mrb.Raise(klass, "HTTP request timed out"u8);
                return;
            }
            case HttpRequestException hre:
            {
                var klass = mrb.GetConst(mrb.Intern("ConnectionError"u8), httpModule).As<RClass>();
                mrb.Raise(klass, mrb.NewString(hre.Message ?? ""));
                return;
            }
            case SocketException se:
            {
                var klass = mrb.GetConst(mrb.Intern("ConnectionError"u8), httpModule).As<RClass>();
                mrb.Raise(klass, mrb.NewString(se.Message ?? ""));
                return;
            }
            default:
            {
                var klass = mrb.GetConst(mrb.Intern("Error"u8), httpModule).As<RClass>();
                mrb.Raise(klass, mrb.NewString(ex.Message ?? ex.GetType().Name));
                return;
            }
        }
    }
}

/// <summary>Per-request timeout marker; translated to <c>HTTP::TimeoutError</c>.</summary>
internal sealed class MRubyHttpTimeoutException : Exception
{
    public MRubyHttpTimeoutException() : base("HTTP request timed out") { }
}

/// <summary>Ruby <c>HTTP</c> module: one-shot verbs taking per-request keyword options and returning <c>HTTP::Response</c>.</summary>
/// <remarks>
/// 4xx/5xx do not raise (matches <see cref="HttpClient"/>); transport failures raise
/// <c>HTTP::ConnectionError</c>, timeouts <c>HTTP::TimeoutError</c>. Inside a scheduled
/// fiber the request parks the fiber; otherwise the calling thread blocks.
/// </remarks>
[RubyModule("HTTP")]
static class HttpMembers
{
    /// <summary><c>HTTP.get(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Get(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, HttpMethod.Get);

    /// <summary><c>HTTP.post(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Post(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, HttpMethod.Post);

    /// <summary><c>HTTP.put(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Put(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, HttpMethod.Put);

    /// <summary><c>HTTP.patch(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Patch(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, new HttpMethod("PATCH"));

    /// <summary><c>HTTP.delete(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Delete(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, HttpMethod.Delete);

    /// <summary><c>HTTP.head(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Head(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, HttpMethod.Head);

    /// <summary><c>HTTP.options(url, **opts)</c>.</summary>
    [RubyDef("(String, **untyped) -> HTTP::Response")]
    public static MRubyValue Options(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.Dispatch(mrb, HttpMethod.Options);

    /// <summary><c>HTTP.request(:verb, url, **opts)</c>.</summary>
    [RubyDef("(Symbol | String, String, **untyped) -> HTTP::Response")]
    public static MRubyValue Request(MRubyState mrb, MRubyValue self) =>
        MRubyHttpExecutor.DispatchExplicitMethod(mrb);
}
