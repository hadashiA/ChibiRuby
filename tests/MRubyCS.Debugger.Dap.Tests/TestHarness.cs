using System.IO.Pipelines;
using MRubyCS.Compiler;
using MRubyCS.Debugger.Dap.Protocol;
using Thread = System.Threading.Thread;

namespace MRubyCS.Debugger.Dap.Tests;

/// <summary>
/// Back-to-back in-memory pipe pair for driving a <see cref="MRubyDapMessageHandler"/> from a
/// test, mirroring the editor-spawned launch wiring (DAP handler owns the VM, launch
/// arguments kick off a VM thread loaded from a script file).
/// </summary>
sealed class TestHarness : IDisposable
{
    readonly Pipe clientToServer = new();
    readonly Pipe serverToClient = new();
    readonly ClientSession client;
    readonly MRubyDebugger debugger;

    public MRubyDapMessageHandler Handler { get; }
    public Task HandlerTask { get; }

    public TestHarness()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        debugger = new MRubyDebugger(state, compiler);
        debugger.Attach();

        MRubyDapMessageHandler? capturedHandler = null;

        Handler = new MRubyDapMessageHandler(
            debugger,
            reader: clientToServer.Reader,
            writer: serverToClient.Writer,
            onLaunch: LaunchHandler);
        capturedHandler = Handler;
        HandlerTask = Handler.RunAsync();
        client = new ClientSession(serverToClient.Reader, clientToServer.Writer);
        return;

        Task LaunchHandler(string programPath, CancellationToken launchCancellation)
        {
            var handler = capturedHandler!;
            var thread = new Thread(() =>
            {
                try
                {
                    var src = File.ReadAllBytes(programPath);
                    using var compilation = compiler.Compile(src, filename: programPath);
                    state.LoadBytecode(compilation.AsBytecode());
                }
                catch (Exception ex)
                {
                    _ = handler.NotifyOutputAsync("stderr", ex.ToString());
                }
                finally
                {
                    _ = handler.NotifyTerminatedAsync();
                }
            }) { IsBackground = true };
            thread.Start();
            return Task.CompletedTask;
        }
    }

    // --- Forwarding helpers (let tests call harness.XxxAsync(...) directly) ----------

    public Task<InitializeResponse> InitializeAsync(string adapterId = "mruby-cs") =>
        client.InitializeAsync(adapterId);

    public Task<AttachResponse> AttachAsync() => client.AttachAsync();

    public Task<LaunchResponse> LaunchAsync(string program) => client.LaunchAsync(program);

    public Task<ConfigurationDoneResponse> ConfigurationDoneAsync() =>
        client.ConfigurationDoneAsync();

    public Task<SetBreakpointsResponse> SetBreakpointsAsync(string sourcePath, params int[] lines) =>
        client.SetBreakpointsAsync(sourcePath, lines);

    public Task<StackTraceResponse> StackTraceAsync(int threadId) =>
        client.StackTraceAsync(threadId);

    public Task<ScopesResponse> ScopesAsync(int frameId) => client.ScopesAsync(frameId);

    public Task<VariablesResponse> VariablesAsync(int variablesReference) =>
        client.VariablesAsync(variablesReference);

    public Task<EvaluateResponse> EvaluateAsync(string expression, string context = "repl") =>
        client.EvaluateAsync(expression, context);

    public Task<ContinueResponse> ContinueAsync(int threadId) => client.ContinueAsync(threadId);

    public Task<TEvent> WaitForEventAsync<TEvent>(string eventName, int timeoutMs = 5000)
        where TEvent : Event =>
        client.WaitForEventAsync<TEvent>(eventName, timeoutMs);

    public Task<Event> WaitForEventAsync(string eventName, int timeoutMs = 5000) =>
        client.WaitForEventAsync(eventName, timeoutMs);

    public void Dispose()
    {
        // Closing the client side will EOF the handler's reader and unblock RunAsync.
        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();
        Handler.Dispose();
        debugger.Dispose();
    }
}
