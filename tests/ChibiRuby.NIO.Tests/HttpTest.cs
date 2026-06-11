using System.Net;
using System.Text;
using ChibiRuby.Compiler;

namespace ChibiRuby.NIO.Tests;

[TestFixture]
public class HttpTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;
    HttpListener listener = default!;
    string baseUrl = default!;
    CancellationTokenSource serverCts = default!;
    Task serverTask = default!;
    // What the next request should do, populated per-test via Configure().
    Func<HttpListenerContext, Task> handle = _ => Task.CompletedTask;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        mrb.DefineHttp();
        compiler = MRubyCompiler.Create(mrb);

        // 127.0.0.1:0 → kernel picks a free port. HttpListener needs a slash
        // suffix and a specific path prefix; we use "/" so every request matches.
        var port = PickFreePort();
        baseUrl = $"http://127.0.0.1:{port}";
        listener = new HttpListener();
        listener.Prefixes.Add($"{baseUrl}/");
        listener.Start();
        serverCts = new CancellationTokenSource();
        serverTask = RunServerAsync(serverCts.Token);
    }

    [TearDown]
    public void After()
    {
        try { serverCts.Cancel(); } catch { }
        try { serverTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        listener.Close();
        serverCts.Dispose();
        compiler.Dispose();
        mrb.Dispose();
    }

    [Test]
    public void Get_Sync_ReturnsBody()
    {
        Configure(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await WriteAsync(ctx, "hello sync");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       resp = HTTP.get("{{baseUrl}}/x")
                       [resp.status, resp.body.to_s]
                       """);

        var result = compiler.LoadSourceCode(script).As<RArray>();
        Assert.That(result[0].IntegerValue, Is.EqualTo(200));
        Assert.That(result[1].As<RString>().ToString(), Is.EqualTo("hello sync"));
    }

    [Test]
    public async Task Get_InsideFiber_WithScheduler_ReturnsBody()
    {
        mrb.UseFiberScheduler();
        Configure(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteAsync(ctx, "hello fiber");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       resp = HTTP.get("{{baseUrl}}/x")
                       [resp.status, resp.body.to_s]
                       """);

        var fiber = compiler.LoadSourceCodeAsFiber(script);
        fiber.Resume();
        var result = (await fiber.WaitForTerminateAsync()).As<RArray>();

        Assert.That(result[0].IntegerValue, Is.EqualTo(200));
        Assert.That(result[1].As<RString>().ToString(), Is.EqualTo("hello fiber"));
    }

    [Test]
    public async Task Post_FormEncoded_ServerReceivesFields()
    {
        mrb.UseFiberScheduler();
        string? receivedBody = null;
        string? receivedContentType = null;
        Configure(async ctx =>
        {
            receivedContentType = ctx.Request.ContentType;
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            receivedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 201;
            await WriteAsync(ctx, "created");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       HTTP.post("{{baseUrl}}/x", form: { "name" => "alice", "age" => 30 }).status
                       """);

        var fiber = compiler.LoadSourceCodeAsFiber(script);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync();

        Assert.That(result.IntegerValue, Is.EqualTo(201));
        Assert.That(receivedContentType, Does.StartWith("application/x-www-form-urlencoded"));
        Assert.That(receivedBody, Is.EqualTo("name=alice&age=30"));
    }

    [Test]
    public void Get_MultiUrl_Raises_ArgumentError()
    {
        // Multi-URL dispatch is not supported. Confirm the error path is wired
        // so callers don't silently get an array of one element or similar.
        Configure(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteAsync(ctx, "ok");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       begin
                         HTTP.get("{{baseUrl}}/a", "{{baseUrl}}/b")
                         :no_raise
                       rescue ArgumentError
                         :raised
                       end
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Get_4xx_DoesNotRaise_ReturnsResponse()
    {
        Configure(async ctx =>
        {
            ctx.Response.StatusCode = 404;
            await WriteAsync(ctx, "not found");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       resp = HTTP.get("{{baseUrl}}/missing")
                       [resp.status, resp.success?, resp.client_error?, resp.error?]
                       """);

        var result = compiler.LoadSourceCode(script).As<RArray>();
        Assert.That(result[0].IntegerValue, Is.EqualTo(404));
        Assert.That(result[1].VType, Is.EqualTo(MRubyVType.False));
        Assert.That(result[2].VType, Is.EqualTo(MRubyVType.True));
        Assert.That(result[3].VType, Is.EqualTo(MRubyVType.True));
    }

    [Test]
    public void EnsureSuccessStatus_Bang_Raises_On_4xx()
    {
        Configure(async ctx =>
        {
            ctx.Response.StatusCode = 418;
            await WriteAsync(ctx, "teapot");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       begin
                         HTTP.get("{{baseUrl}}/teapot").ensure_success_status!
                         :no_raise
                       rescue HTTP::Error
                         :raised
                       end
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Get_ConnectionRefused_RaisesConnectionError()
    {
        // Pick a port nothing is listening on. HttpListener for our test
        // server is bound to a *different* port, so this one is dark.
        var deadPort = PickFreePort();
        var script = Encoding.UTF8.GetBytes($$"""
                       begin
                         HTTP.get("http://127.0.0.1:{{deadPort}}/x")
                         :no_raise
                       rescue HTTP::ConnectionError
                         :raised
                       end
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Get_Timeout_RaisesTimeoutError()
    {
        Configure(async ctx =>
        {
            await Task.Delay(2000);
            ctx.Response.StatusCode = 200;
            await WriteAsync(ctx, "too late");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       begin
                         HTTP.get("{{baseUrl}}/slow", timeout: 0.2)
                         :no_raise
                       rescue HTTP::TimeoutError
                         :raised
                       end
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Get_HeadersOption_SendsHeaders()
    {
        var allHeaders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Configure(async ctx =>
        {
            foreach (string? name in ctx.Request.Headers)
            {
                if (name is null) continue;
                allHeaders[name] = ctx.Request.Headers[name];
            }
            ctx.Response.StatusCode = 200;
            await WriteAsync(ctx, "ok");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       HTTP.get("{{baseUrl}}/x", headers: { "user-agent" => "test/1.0", "x-custom" => "abc" }).status
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.IntegerValue, Is.EqualTo(200));
        Assert.That(allHeaders.GetValueOrDefault("User-Agent"), Is.EqualTo("test/1.0"));
        Assert.That(allHeaders.GetValueOrDefault("X-Custom"), Is.EqualTo("abc"));
    }

    [Test]
    public void Get_BasicAuthOption_SendsAuthorizationHeader()
    {
        string? auth = null;
        Configure(async ctx =>
        {
            auth = ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode = 200;
            await WriteAsync(ctx, "ok");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       HTTP.get("{{baseUrl}}/x", basic_auth: ["alice", "secret"]).status
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.IntegerValue, Is.EqualTo(200));
        // "alice:secret" base64-encoded
        Assert.That(auth, Is.EqualTo("Basic YWxpY2U6c2VjcmV0"));
    }

    [Test]
    public void Headers_CaseInsensitiveLookup()
    {
        Configure(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["X-Trace"] = "deadbeef";
            await WriteAsync(ctx, "ok");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       resp = HTTP.get("{{baseUrl}}/x")
                       [resp.headers["X-Trace"], resp.headers["x-trace"]]
                       """);

        var result = compiler.LoadSourceCode(script).As<RArray>();
        Assert.That(result[0].As<RString>().ToString(), Is.EqualTo("deadbeef"));
        Assert.That(result[1].As<RString>().ToString(), Is.EqualTo("deadbeef"));
    }

    [Test]
    public void Get_ParamsAppendsQuery()
    {
        string? gotQuery = null;
        Configure(async ctx =>
        {
            gotQuery = ctx.Request.Url!.Query;
            ctx.Response.StatusCode = 200;
            await WriteAsync(ctx, "ok");
        });

        var script = Encoding.UTF8.GetBytes($$"""
                       HTTP.get("{{baseUrl}}/x", params: { "q" => "hello world", "page" => 2 }).status
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.IntegerValue, Is.EqualTo(200));
        Assert.That(gotQuery, Is.EqualTo("?q=hello+world&page=2").Or.EqualTo("?q=hello%20world&page=2"));
    }

    // ─────────────── helpers ────────────────────────────────────────────

    void Configure(Func<HttpListenerContext, Task> nextHandler) => handle = nextHandler;

    async Task RunServerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(token);
            }
            catch { break; }
            _ = Task.Run(async () =>
            {
                try { await handle(ctx); }
                catch { /* swallow */ }
                finally { try { ctx.Response.Close(); } catch { } }
            });
        }
    }

    static async Task WriteAsync(HttpListenerContext ctx, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    static int PickFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
