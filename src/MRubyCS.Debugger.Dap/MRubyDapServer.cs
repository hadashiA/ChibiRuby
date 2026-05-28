using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MRubyCS.Compiler;

namespace MRubyCS.Debugger.Dap;

/// <summary>
/// TCP listener for DAP. One <see cref="MRubyDapMessageHandler"/> at a time per server
/// (sequential, no concurrent sessions on the same VM). Pass
/// <see cref="IPAddress.Any"/> as <c>bindAddress</c> to allow LAN attaches.
/// </summary>
public sealed class MRubyDapServer : IDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource stopSource = new();
    readonly LogDelegate? log;

    // Whichever handler is servicing the current session, or null between accepts.
    // Used by Dispose to send `terminated(restart=true)` for clean re-attach.
    MRubyDapMessageHandler? activeHandler;

    public MRubyDapServer(
        MRubyState state,
        MRubyCompiler? compiler = null,
        int port = 4711,
        IPAddress? bindAddress = null,
        LogDelegate? log = null)
    {
        this.log = log;
        compiler ??= MRubyCompiler.Create(state);
        listener = new TcpListener(bindAddress ?? IPAddress.Loopback, port);
        Debugger = new MRubyDebugger(state, compiler);
    }

    /// <summary>Bound endpoint. Only valid after <see cref="StartAsync"/> has run <c>listener.Start()</c>.</summary>
    public IPEndPoint LocalEndpoint => (IPEndPoint)listener.LocalEndpoint;
    public MRubyDebugger Debugger { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Debugger.Attach();
        listener.Start();
        log?.Invoke(LogLevel.Information, $"mruby-cs debug: listening on {LocalEndpoint}", null);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopSource.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
#if NET6_0_OR_GREATER
                    client = await listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
#else
                    // netstandard2.1 has no CancellationToken overload; tie cancellation to listener.Stop().
                    using (linked.Token.Register(static l => ((TcpListener)l!).Stop(), listener))
                    {
                        client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
#endif
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) when (linked.IsCancellationRequested) { break; }

                log?.Invoke(LogLevel.Information, $"mruby-cs debug: client connected from {client.Client.RemoteEndPoint}", null);
                var stream = client.GetStream();
                using var handler = new MRubyDapMessageHandler(
                    Debugger,
                    PipeReader.Create(stream),
                    PipeWriter.Create(stream),
                    subsystem: client,
                    log: log);
                activeHandler = handler;
                try
                {
                    await handler.RunAsync(linked.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log?.Invoke(LogLevel.Warning, $"mruby-cs debug: session ended with: {ex.Message}", ex);
                }
                finally
                {
                    activeHandler = null;
                }
                log?.Invoke(LogLevel.Information, "mruby-cs debug: client disconnected", null);
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    public void Dispose()
    {
        // Tell any attached client we're going away on purpose so it auto-reconnects later.
        // Bounded wait keeps a flaky socket from blocking the disposing thread.
        var current = activeHandler;
        if (current is not null)
        {
            try
            {
                current.NotifyTerminatedAsync(restart: true)
                    .Wait(TimeSpan.FromMilliseconds(500));
            }
            catch { /* best-effort */ }
        }

        try { stopSource.Cancel(); } catch { /* already disposed */ }
        try { listener.Stop(); } catch { /* already stopped */ }
        Debugger.Dispose();
        stopSource.Dispose();
    }
}
