using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChibiRuby.NIO.Tests;

/// <summary>
/// Minimal in-process HTTP server for HTTP/JSON tests. Picks a free
/// loopback port, accepts requests, dispatches to <see cref="OnRequest"/>.
/// Disposing stops the listener and waits briefly for the accept loop to exit.
/// </summary>
sealed class LocalServer : IDisposable
{
    public string BaseUrl { get; }
    public Func<HttpListenerContext, Task> OnRequest { get; set; } = _ => Task.CompletedTask;

    readonly HttpListener listener;
    readonly CancellationTokenSource cts;
    readonly Task acceptLoop;

    LocalServer(HttpListener listener, string baseUrl)
    {
        this.listener = listener;
        BaseUrl = baseUrl;
        cts = new CancellationTokenSource();
        acceptLoop = RunAsync(cts.Token);
    }

    public static LocalServer Start()
    {
        var port = PickFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var listener = new HttpListener();
        listener.Prefixes.Add($"{baseUrl}/");
        listener.Start();
        return new LocalServer(listener, baseUrl);
    }

    /// <summary>Convenience: write a String to the response body as UTF-8 with
    /// Content-Length set.</summary>
    public async Task WriteAsync(HttpListenerContext ctx, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    async Task RunAsync(CancellationToken token)
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
                try { await OnRequest(ctx); }
                catch { /* swallow */ }
                finally { try { ctx.Response.Close(); } catch { } }
            });
        }
    }

    public void Dispose()
    {
        try { cts.Cancel(); } catch { }
        try { acceptLoop.Wait(TimeSpan.FromSeconds(1)); } catch { }
        listener.Close();
        cts.Dispose();
    }

    static int PickFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
