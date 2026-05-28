// Minimal embedded host that demonstrates how to wire up MRubyCS.Debugger.Dap from a
// regular C# application. Run with `dotnet run --project sandbox/DebuggerEmbeddedSample`
// and the program will block at the first binding.irb call until a DAP client (e.g.
// VSCode with the mruby-cs-debugger extension) attaches to 127.0.0.1:4711.

using System.Net;
using MRubyCS;
using MRubyCS.Compiler;
using MRubyCS.Debugger.Dap;

var mrb = MRubyState.Create();
var compiler = MRubyCompiler.Create(mrb);

// One DapServer per port — owns the listener and serves clients sequentially (one at a
// time, with reconnect). In a real game / app guard this with `#if DEBUG` or a config
// flag so production builds don't hold a TCP port.
//
// `bindAddress: IPAddress.Any` exposes the debugger to the LAN so an attached editor
// on another machine (iPhone on the same Wi-Fi, etc.) can attach to <host-LAN-IP>:4711.
// Drop the parameter (or pass IPAddress.Loopback) to restrict to 127.0.0.1 only.
using var dap = new MRubyDapServer(mrb, compiler, port: 4711, bindAddress: IPAddress.Any);
_ = dap.StartAsync();

var scriptPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "scenarios", "quest.rb");

Console.Error.WriteLine($"running script: {scriptPath}");
var source = File.ReadAllBytes(scriptPath);
try
{
    // Compile with the file path so the bytecode's DBG section records the right
    // filename; the debugger uses that to surface file:line in the editor.
    using var compilation = compiler.Compile(source, filename: scriptPath);
    mrb.LoadBytecode(compilation.AsBytecode());
    Console.Error.WriteLine("script completed");
}
catch (MRubyRaiseException ex)
{
    Console.Error.WriteLine($"unhandled mruby exception: {ex.Message}");
    var bt = ex.ExceptionObject.Backtrace;
    if (bt is not null)
    {
        Console.Error.WriteLine(bt.ToString(mrb).TrimEnd());
    }
}
