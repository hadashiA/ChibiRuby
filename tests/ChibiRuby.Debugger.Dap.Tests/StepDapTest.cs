using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Compiler;
using ChibiRuby.Debugger.Dap.Protocol;
using Thread = System.Threading.Thread;

namespace ChibiRuby.Debugger.Dap.Tests;

/// <summary>
/// End-to-end tests for the DAP `next` / `stepIn` / `stepOut` commands over real TCP.
/// </summary>
[TestFixture]
public class StepDapTest
{
    [Test]
    public async Task NextRequest_StepsOverMethodCall()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        using var server = new MRubyDapServer(state, compiler, port: 0);
        _ = server.StartAsync();
        var port = server.LocalEndpoint.Port;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"step-{System.Guid.NewGuid():N}.rb");
        File.WriteAllText(scriptPath, "def foo\n  x = 1\nend\nfoo\nz = 3\n");
        try
        {
            var startSignal = new ManualResetEventSlim();
            var vmDone = new ManualResetEventSlim();
            new Thread(() =>
            {
                startSignal.Wait();
                try
                {
                    var src = File.ReadAllBytes(scriptPath);
                    using var c = compiler.Compile(src, filename: scriptPath);
                    state.LoadBytecode(c.AsBytecode());
                }
                finally { vmDone.Set(); }
            }) { IsBackground = true }.Start();

            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port);
            var stream = tcp.GetStream();
            var session = new ClientSession(PipeReader.Create(stream), PipeWriter.Create(stream));

            await session.InitializeAsync();
            await session.WaitForEventAsync("initialized");

            await session.SetBreakpointsAsync(scriptPath, 4);
            await session.AttachAsync();

            startSignal.Set();
            await session.WaitForEventAsync("stopped");

            var nextResp = await session.NextAsync(threadId: 1);
            Assert.That(nextResp.Success, Is.True);

            var stopped = await session.WaitForEventAsync<StoppedEvent>("stopped");
            Assert.That(stopped.Body.Reason, Is.EqualTo("step"));

            var stack = await session.StackTraceAsync(threadId: 1);
            var line = stack.Body.StackFrames[0].Line;
            Assert.That(line, Is.EqualTo((ulong)5));

            await session.ContinueAsync(threadId: 1);
            Assert.That(vmDone.Wait(2000), Is.True);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Test]
    public async Task StepInRequest_EntersMethodBody()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        using var server = new MRubyDapServer(state, compiler, port: 0);
        _ = server.StartAsync();
        var port = server.LocalEndpoint.Port;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"stepin-{System.Guid.NewGuid():N}.rb");
        File.WriteAllText(scriptPath, "def foo\n  x = 1\nend\nfoo\n");
        try
        {
            var startSignal = new ManualResetEventSlim();
            var vmDone = new ManualResetEventSlim();
            new Thread(() =>
            {
                startSignal.Wait();
                try
                {
                    var src = File.ReadAllBytes(scriptPath);
                    using var c = compiler.Compile(src, filename: scriptPath);
                    state.LoadBytecode(c.AsBytecode());
                }
                finally { vmDone.Set(); }
            }) { IsBackground = true }.Start();

            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port);
            var stream = tcp.GetStream();
            var session = new ClientSession(PipeReader.Create(stream), PipeWriter.Create(stream));

            await session.InitializeAsync();
            await session.WaitForEventAsync("initialized");

            await session.SetBreakpointsAsync(scriptPath, 4);
            await session.AttachAsync();

            startSignal.Set();
            await session.WaitForEventAsync("stopped");

            await session.StepInAsync(threadId: 1);
            await session.WaitForEventAsync("stopped");

            var stack = await session.StackTraceAsync(threadId: 1);
            var line = stack.Body.StackFrames[0].Line;
            Assert.That(line, Is.LessThanOrEqualTo((ulong)2), "stepIn should land inside foo's body (line 1 def or 2 x=1)");

            await session.ContinueAsync(threadId: 1);
            Assert.That(vmDone.Wait(2000), Is.True);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
