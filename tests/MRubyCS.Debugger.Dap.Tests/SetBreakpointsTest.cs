using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MRubyCS.Compiler;
using MRubyCS.Debugger.Dap.Protocol;
using Thread = System.Threading.Thread;

namespace MRubyCS.Debugger.Dap.Tests;

[TestFixture]
public class SetBreakpointsTest
{
    [Test]
    public async Task SetBreakpoints_StopsAtRequestedLine_OverTcp()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        using var server = new MRubyDapServer(state, compiler, port: 0);
        _ = server.StartAsync();
        var port = server.LocalEndpoint.Port;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"bp-{System.Guid.NewGuid():N}.rb");
        File.WriteAllText(scriptPath, "a = 1\nb = 2\nc = 3\nd = 4\n");
        try
        {
            var startSignal = new ManualResetEventSlim();
            var vmDone = new ManualResetEventSlim();
            var vmThread = new Thread(() =>
            {
                startSignal.Wait();
                try
                {
                    var src = File.ReadAllBytes(scriptPath);
                    using var c = compiler.Compile(src, filename: scriptPath);
                    state.LoadBytecode(c.AsBytecode());
                }
                finally { vmDone.Set(); }
            })
            { IsBackground = true };
            vmThread.Start();

            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port);
            var stream = tcp.GetStream();
            var session = new ClientSession(PipeReader.Create(stream), PipeWriter.Create(stream));

            await session.InitializeAsync();
            await session.WaitForEventAsync("initialized");

            var bpResp = await session.SetBreakpointsAsync(scriptPath, 3);
            Assert.That(bpResp.Success, Is.True);
            var bps = bpResp.Body.Breakpoints;
            Assert.That(bps.Length, Is.EqualTo(1));
            Assert.That(bps[0].Verified, Is.True);
            Assert.That(bps[0].Line, Is.EqualTo((ulong)3));

            await session.AttachAsync();

            startSignal.Set();
            var stopped = await session.WaitForEventAsync<StoppedEvent>("stopped");
            Assert.That(stopped.Body.Reason, Is.EqualTo("breakpoint"));

            var stack = await session.StackTraceAsync(threadId: 1);
            var frames = stack.Body.StackFrames;
            Assert.That(frames.Length, Is.GreaterThan(0));
            Assert.That(frames[0].Line, Is.EqualTo((ulong)3));

            await session.ContinueAsync(threadId: 1);
            Assert.That(vmDone.Wait(2000), Is.True);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Test]
    public async Task SetBreakpoints_EmptyArray_ClearsAllForFile()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        using var server = new MRubyDapServer(state, compiler, port: 0);
        _ = server.StartAsync();
        var port = server.LocalEndpoint.Port;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"bp-clr-{System.Guid.NewGuid():N}.rb");
        File.WriteAllText(scriptPath, "a = 1\nb = 2\nc = 3\n");
        try
        {
            var vmDone = new ManualResetEventSlim();
            var startSignal = new ManualResetEventSlim();
            var vmThread = new Thread(() =>
            {
                startSignal.Wait();
                try
                {
                    var src = File.ReadAllBytes(scriptPath);
                    using var c = compiler.Compile(src, filename: scriptPath);
                    state.LoadBytecode(c.AsBytecode());
                }
                finally { vmDone.Set(); }
            })
            { IsBackground = true };
            vmThread.Start();

            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port);
            var stream = tcp.GetStream();
            var session = new ClientSession(PipeReader.Create(stream), PipeWriter.Create(stream));

            await session.InitializeAsync();
            await session.WaitForEventAsync("initialized");

            await session.SetBreakpointsAsync(scriptPath, 2);
            await session.SetBreakpointsAsync(scriptPath /* no lines = clear */);

            await session.AttachAsync();
            startSignal.Set();

            Assert.That(vmDone.Wait(2000), Is.True, "VM should finish without hitting any breakpoint");
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
