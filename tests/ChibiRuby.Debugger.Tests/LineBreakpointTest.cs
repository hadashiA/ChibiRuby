using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Compiler;

namespace ChibiRuby.Debugger.Tests;

/// <summary>
/// Phase 2.2 line-breakpoint coverage. These tests drive the debugger from a background
/// thread (same topology as the DAP server) so the VM-thread suspend pump is exercised
/// end-to-end. Each test compiles a script with explicit filename, sets one or more
/// breakpoints, and verifies the expected stop count + per-stop line.
/// </summary>
[TestFixture]
public class LineBreakpointTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void BeforeEach()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void AfterEach()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    sealed class Recorder : IDebuggerClient
    {
        public readonly System.Collections.Generic.List<StopEvent> Stops = new();

        public void OnStopped(MRubyDebugger debugger, StopEvent ev)
        {
            Stops.Add(ev);
            new Thread(_ => debugger.Continue()) { IsBackground = true }.Start();
        }

        public void OnResumed(MRubyDebugger debugger) { }
    }

    MRubyDebugger NewDebugger(IDebuggerClient client)
    {
        var d = new MRubyDebugger(mrb, compiler);
        d.Attach();
        d.AttachClient(client);
        return d;
    }

    void Run(string source, string filename)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(source);
        using var c = compiler.Compile(bytes, filename: filename);
        mrb.LoadBytecode(c.AsBytecode());
    }

    [Test]
    public void StopsAtSetLine()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("script.rb", [2]);

        Run("""
            a = 1
            b = 2
            c = 3
            """, "script.rb");

        Assert.That(rec.Stops, Has.Count.EqualTo(1));
        Assert.That(rec.Stops[0].Reason, Is.EqualTo(StopReason.LineBreakpoint));
        Assert.That(rec.Stops[0].Line, Is.EqualTo(2));
    }

    [Test]
    public void DoesNotStop_WhenLineHasNoBreakpoint()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("script.rb", [99]); // unreachable

        Run("a = 1\nb = 2\n", "script.rb");

        Assert.That(rec.Stops, Is.Empty);
    }

    [Test]
    public void StopsTwiceInLoop_OnSameLine()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("loop.rb", [2]);

        Run("""
            3.times do
              x = 1
            end
            """, "loop.rb");

        // Line 2 (`x = 1`) executes once per iteration -> 3 stops. The within-line dedup
        // suppresses extra stops at sibling pcs of the same line within a single
        // iteration, but the loop re-enters the line each iteration so it re-triggers.
        Assert.That(rec.Stops, Has.Count.EqualTo(3));
        foreach (var stop in rec.Stops) Assert.That(stop.Line, Is.EqualTo(2));
    }

    [Test]
    public void MultipleBreakpointsInSameFile()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("multi.rb", [1, 3]);

        Run("""
            a = 1
            b = 2
            c = 3
            d = 4
            """, "multi.rb");

        Assert.That(rec.Stops, Has.Count.EqualTo(2));
        Assert.That(rec.Stops[0].Line, Is.EqualTo(1));
        Assert.That(rec.Stops[1].Line, Is.EqualTo(3));
    }

    [Test]
    public void DoesNotStop_AfterBreakpointIsCleared()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("clear.rb", new[] { 2 });
        d.SetBreakpoints("clear.rb", System.Array.Empty<int>()); // clear

        Run("a = 1\nb = 2\nc = 3\n", "clear.rb");

        Assert.That(rec.Stops, Is.Empty);
    }

    [Test]
    public void OtherFile_DoesNotMatch()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("other.rb", new[] { 1 });

        Run("a = 1\n", "actual.rb");

        Assert.That(rec.Stops, Is.Empty);
    }

    [Test]
    public void BreakpointInMethodBody()
    {
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("method.rb", [3]);

        Run("""
            def f
              x = 1
              y = 2
            end
            f
            """, "method.rb");

        Assert.That(rec.Stops, Has.Count.EqualTo(1));
        Assert.That(rec.Stops[0].Line, Is.EqualTo(3));
    }

    [Test]
    public void Eval_AtBreakpoint_DoesNotRecursivelyTrigger()
    {
        // While suspended at a breakpoint the user might evaluate code that itself touches
        // a breakpointed line. Eval-suppress flag prevents the recursive re-stop.
        var rec = new Recorder();
        EvalResult? evalResult = null;
        using var d = new MRubyDebugger(mrb, compiler);
        d.Attach();
        d.AttachClient(new RecorderWithEval(rec, dbg => evalResult = dbg.Evaluate("def helper; x = 1; end; helper; 42")));
        d.SetBreakpoints("self.rb", [1]);

        Run("a = 1\n", "self.rb");

        Assert.That(rec.Stops, Has.Count.EqualTo(1));
        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.Value.IntegerValue, Is.EqualTo(42));
    }

    sealed class RecorderWithEval(Recorder inner, Action<MRubyDebugger> evalAction) : IDebuggerClient
    {
        public void OnStopped(MRubyDebugger debugger, StopEvent ev)
        {
            inner.Stops.Add(ev);
            new Thread(_ =>
            {
                evalAction(debugger);
                debugger.Continue();
            }) { IsBackground = true }.Start();
        }

        public void OnResumed(MRubyDebugger debugger) { }
    }

    [Test]
    public void StopsAtSetLine_WhenBreakpointPathIsAbsoluteButDbgIsRelative()
    {
        // Editor (e.g. VSCode gutter click) sends an absolute path; the running host
        // compiled the script with a relative `filename:`. The two should still match
        // by path-tail.
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        var absPath = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "absrel.rb"));
        d.SetBreakpoints(absPath, [2]);

        Run("""
            a = 1
            b = 2
            c = 3
            """, "absrel.rb"); // relative filename in DBG

        Assert.That(rec.Stops, Has.Count.EqualTo(1));
        Assert.That(rec.Stops[0].Line, Is.EqualTo(2));
    }

    [Test]
    public void StopsAtSetLine_WhenBreakpointPathIsRelativeButDbgIsAbsolute()
    {
        // Reverse mismatch: BP set by a tool that knows only the basename, while the
        // host compiled with an absolute path. Suffix match should still kick in.
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        var absPath = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "relabs.rb"));
        d.SetBreakpoints("relabs.rb", [2]);

        Run("""
            a = 1
            b = 2
            c = 3
            """, absPath);

        Assert.That(rec.Stops, Has.Count.EqualTo(1));
        Assert.That(rec.Stops[0].Line, Is.EqualTo(2));
    }

    [Test]
    public void DoesNotStop_WhenOnlySubstringMatchesButNotAtPathBoundary()
    {
        // Defensive: `bar.rb` must not match `foobar.rb` (substring of filename, no
        // path separator before the tail).
        var rec = new Recorder();
        using var d = NewDebugger(rec);
        d.SetBreakpoints("bar.rb", [2]);

        Run("""
            a = 1
            b = 2
            """, "foobar.rb");

        Assert.That(rec.Stops, Is.Empty);
    }
}
