using System;
using System.Collections.Generic;
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
/// End-to-end test of the embedded host scenario: a host C# thread runs Ruby (with
/// <c>binding.irb</c> inside), <see cref="MRubyDapServer"/> listens for an attach over
/// loopback TCP, the test plays the client role.
/// </summary>
[TestFixture]
public class TcpDapServerTest
{
    [Test]
    public async Task EmbeddedHost_BlocksAtBindingIrb_UntilClientAttaches_ThenContinues()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        using var server = new MRubyDapServer(state, compiler, port: 0);
        _ = server.StartAsync();
        var port = server.LocalEndpoint.Port;

        Exception? vmError = null;
        var vmDone = new ManualResetEventSlim();
        var vmThread = new Thread(() =>
        {
            try
            {
                compiler.LoadSourceCode("""
                    secret = 42
                    binding.irb
                    secret
                    """u8);
            }
            catch (Exception ex) { vmError = ex; }
            finally { vmDone.Set(); }
        })
        { IsBackground = true, Name = "test-vm" };
        vmThread.Start();

        Assert.That(vmDone.Wait(200), Is.False, "VM should be blocked at binding.irb waiting for client");

        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port);
        var stream = tcp.GetStream();
        var session = new ClientSession(PipeReader.Create(stream), PipeWriter.Create(stream));

        await session.InitializeAsync();
        await session.WaitForEventAsync("initialized");
        var attachResp = await session.AttachAsync();
        Assert.That(attachResp.Success, Is.True);

        var stopped = await session.WaitForEventAsync<StoppedEvent>("stopped");
        Assert.That(stopped.Body.Reason, Is.EqualTo("pause"));

        var eval = await session.EvaluateAsync("1 + 2");
        Assert.That(eval.Success, Is.True);
        Assert.That(eval.Body.Result, Is.EqualTo("3"));

        var stack = await session.StackTraceAsync(threadId: 1);
        var frameId = stack.Body.StackFrames[0].Id;
        var scopes = await session.ScopesAsync(frameId);
        var varsRef = scopes.Body.Scopes[0].VariablesReference;
        var vars = await session.VariablesAsync(varsRef);
        var varNames = new List<string>();
        foreach (var v in vars.Body.Variables) varNames.Add(v.Name);
        Assert.That(varNames, Does.Contain("secret"));

        await session.ContinueAsync(threadId: 1);
        Assert.That(vmDone.Wait(2000), Is.True, "VM should resume after continue");
        Assert.That(vmError, Is.Null);
    }
}
