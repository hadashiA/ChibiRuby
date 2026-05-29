using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Compiler;

namespace ChibiRuby.Debugger.Dap.Tests;

/// <summary>
/// Verifies that Phase 2.1 DBG-section plumbing actually reaches the DAP wire: the
/// stackTrace response should carry the real source filename + line where binding.irb
/// was invoked, not the (toplevel) / line 1 placeholder.
/// </summary>
[TestFixture]
public class StackTraceLineTest
{
    [Test]
    public async Task StackTrace_ReturnsRealLineFromDbgSection_Attach()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        using var server = new MRubyDapServer(state, compiler, port: 0);
        _ = server.StartAsync();
        var port = server.LocalEndpoint.Port;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"dbg-{System.Guid.NewGuid():N}.rb");
        File.WriteAllText(scriptPath, "x = 10\ny = 20\nbinding.irb\nx + y\n");
        try
        {
            var vmDone = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    var src = File.ReadAllBytes(scriptPath);
                    using var compilation = compiler.Compile(src, filename: scriptPath);
                    state.LoadBytecode(compilation.AsBytecode());
                }
                finally { vmDone.Set(); }
            })
            { IsBackground = true };
            thread.Start();

            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port);
            var stream = tcp.GetStream();
            var session = new ClientSession(PipeReader.Create(stream), PipeWriter.Create(stream));

            await session.InitializeAsync();
            await session.WaitForEventAsync("initialized");
            await session.AttachAsync();
            await session.WaitForEventAsync("stopped");

            var stack = await session.StackTraceAsync(threadId: 1);
            var frames = stack.Body.StackFrames;
            Assert.That(frames.Length, Is.GreaterThan(0));

            var frame0 = frames[0];
            Assert.That(frame0.Line, Is.EqualTo((ulong)3),
                "stackTrace should report the source line of binding.irb (line 3)");

            var sourcePath = frame0.Source?.Path;
            Assert.That(sourcePath, Is.EqualTo(scriptPath));

            await session.ContinueAsync(threadId: 1);
            Assert.That(vmDone.Wait(2000), Is.True);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
