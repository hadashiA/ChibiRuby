using System.Threading;
using MRubyCS.Compiler;

namespace MRubyCS.Debugger.Tests;

/// <summary>
/// Phase 2.3 step semantics: stepIn enters callees, stepOver skips them, stepOut
/// resumes until the next line at a strictly shallower call depth.
/// </summary>
[TestFixture]
public class StepTest
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

    /// <summary>
    /// Drives the debugger from a background thread that decides per-stop what to do
    /// next (continue / step). The Stops list grows by one entry per stop fired.
    /// </summary>
    sealed class Scripted : IDebuggerClient
    {
        public readonly System.Collections.Generic.List<StopEvent> Stops = new();
        readonly System.Action<MRubyDebugger, StopEvent, int> reaction;

        public Scripted(System.Action<MRubyDebugger, StopEvent, int> reaction)
        {
            this.reaction = reaction;
        }

        public void OnStopped(MRubyDebugger debugger, StopEvent ev)
        {
            var index = Stops.Count;
            Stops.Add(ev);
            new Thread(_ => reaction(debugger, ev, index)) { IsBackground = true }.Start();
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
    public void StepOver_SkipsMethodCall_AdvancesToNextTopLevelLine()
    {
        // Set a breakpoint at line 5 (the `foo` call), then step-over; we should land on
        // line 6 without entering `foo`.
        Scripted? scripted = null;
        scripted = new Scripted((dbg, ev, idx) =>
        {
            if (idx == 0) dbg.StepOver();
            else dbg.Continue();
        });
        using var d = NewDebugger(scripted);
        d.SetBreakpoints("over.rb", new[] { 5 });

        Run("""
            def foo
              x = 1
              y = 2
            end
            foo
            z = 3
            """, "over.rb");

        Assert.That(scripted.Stops, Has.Count.EqualTo(2));
        Assert.That(scripted.Stops[0].Line, Is.EqualTo(5));
        Assert.That(scripted.Stops[1].Reason, Is.EqualTo(StopReason.Step));
        Assert.That(scripted.Stops[1].Line, Is.EqualTo(6), "Step-over should advance past `foo` to z = 3");
    }

    [Test]
    public void StepIn_EntersMethod_StopsAtFirstLineOfCallee()
    {
        // BP at line 5 (the call), then stepIn; we should land on the first executable
        // line *inside* foo (line 2).
        Scripted? scripted = null;
        scripted = new Scripted((dbg, ev, idx) =>
        {
            if (idx == 0) dbg.StepIn();
            else dbg.Continue();
        });
        using var d = NewDebugger(scripted);
        d.SetBreakpoints("in.rb", new[] { 5 });

        Run("""
            def foo
              x = 1
              y = 2
            end
            foo
            """, "in.rb");

        Assert.That(scripted.Stops, Has.Count.EqualTo(2));
        Assert.That(scripted.Stops[0].Line, Is.EqualTo(5));
        Assert.That(scripted.Stops[1].Reason, Is.EqualTo(StopReason.Step));
        // mruby's DBG section maps the method-prelude opcode (OP_ENTER) to the def line,
        // so the first line boundary inside `foo` is line 1, not line 2.
        Assert.That(scripted.Stops[1].Line, Is.EqualTo(1));
    }

    [Test]
    public void StepOut_ResumesUntilCallerLine()
    {
        // BP inside `foo` at line 2, then stepOut; we should land back at the caller (line
        // 6, the line *after* the call site -- since the call site itself has already
        // executed by the time control returns).
        Scripted? scripted = null;
        scripted = new Scripted((dbg, ev, idx) =>
        {
            if (idx == 0) dbg.StepOut();
            else dbg.Continue();
        });
        using var d = NewDebugger(scripted);
        d.SetBreakpoints("out.rb", new[] { 2 });

        Run("""
            def foo
              x = 1
              y = 2
            end
            foo
            z = 3
            """, "out.rb");

        Assert.That(scripted.Stops, Has.Count.EqualTo(2));
        Assert.That(scripted.Stops[0].Line, Is.EqualTo(2));
        Assert.That(scripted.Stops[1].Reason, Is.EqualTo(StopReason.Step));
        // After stepping out, control returns to the script's main flow. The "next line at
        // shallower depth" is line 6 (z = 3) since line 5 (foo) is the call site we just
        // left.
        Assert.That(scripted.Stops[1].Line, Is.EqualTo(6));
    }

    [Test]
    public void StepOver_NextLineInSameFunction()
    {
        // Inside a method, step over a simple assignment. No nested call, so stepOver
        // behaves the same as stepIn here -- it just advances one line.
        Scripted? scripted = null;
        scripted = new Scripted((dbg, ev, idx) =>
        {
            if (idx == 0) dbg.StepOver();
            else dbg.Continue();
        });
        using var d = NewDebugger(scripted);
        d.SetBreakpoints("inner.rb", new[] { 2 });

        Run("""
            def f
              a = 1
              b = 2
              c = 3
            end
            f
            """, "inner.rb");

        Assert.That(scripted.Stops, Has.Count.EqualTo(2));
        Assert.That(scripted.Stops[0].Line, Is.EqualTo(2));
        Assert.That(scripted.Stops[1].Line, Is.EqualTo(3));
    }
}
