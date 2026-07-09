using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Compiler;
using ChibiRuby.Debugger;

namespace ChibiRuby.Debugger.Tests;

/// <summary>
/// Test client that drives the debugger from a background thread, simulating what a DAP
/// server would do: receive OnStopped, fire off evaluate requests, then continue.
/// </summary>
sealed class ScriptedClient : IDebuggerClient
{
    readonly System.Action<MRubyDebugger, StopEvent> onStop;
    public int StopCount;
    public int ResumeCount;

    public ScriptedClient(System.Action<MRubyDebugger, StopEvent> onStop) => this.onStop = onStop;

    public void OnStopped(MRubyDebugger debugger, StopEvent ev)
    {
        StopCount++;
        // Run the test scenario on a background thread to mirror DAP-server topology.
        // The VM thread (caller of OnStopped) must keep pumping commands while this runs.
        var thread = new Thread(() =>
        {
            onStop(debugger, ev);
            debugger.Continue();
        })
        { IsBackground = true };
        thread.Start();
    }

    public void OnResumed(MRubyDebugger debugger) => ResumeCount++;
}

[TestFixture]
public class MRubyDebuggerTest
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

    // Phase-1 tests run a single client through the whole scenario; helper centralizes
    // the construct + attach sequence that the refactored API now requires.
    MRubyDebugger CreateAttached(IDebuggerClient client)
    {
        var d = new MRubyDebugger(mrb, compiler);
        d.Attach();
        d.AttachClient(client);
        return d;
    }

    [Test]
    public void StopAndContinue_FromToplevel()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            evalResult = dbg.Evaluate("1 + 2");
        }));

        var result = compiler.LoadSourceCode("""
            binding.break
            42
            """u8);

        Assert.That(result.IntegerValue, Is.EqualTo(42));
        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.Value.IntegerValue, Is.EqualTo(3));
        Assert.That(evalResult.DisplayString, Is.EqualTo("3"));
    }

    [Test]
    public void Eval_CanCallMethodsOnReceiver()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            // At toplevel, self responds to `class` which returns `Object`.
            evalResult = dbg.Evaluate("self.class.to_s");
        }));

        compiler.LoadSourceCode("binding.break"u8);

        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.DisplayString, Is.EqualTo("\"Object\""));
    }

    [Test]
    public void Eval_SyntaxErrorIsReportedNotPropagated()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            // Syntax error path - the compiler returns HasError, we never enter the VM.
            evalResult = dbg.Evaluate("1 +");
        }));

        var result = compiler.LoadSourceCode("""
            binding.break
            :ok
            """u8);

        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("ok"u8)));
        Assert.That(evalResult!.IsError, Is.True);
    }

    [Test]
    public void Eval_RubyRaiseIsReportedNotPropagated()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            evalResult = dbg.Evaluate("raise 'boom'");
        }));

        // The raise inside eval must not bubble out of binding.break.
        var result = compiler.LoadSourceCode("""
            binding.break
            :ok
            """u8);

        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("ok"u8)));
        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.True);
        Assert.That(evalResult.DisplayString, Does.Contain("boom"));
    }

    [Test]
    public void Eval_AfterRaise_NextEvalStillWorks()
    {
        EvalResult? firstResult = null;
        EvalResult? secondResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            firstResult = dbg.Evaluate("raise 'boom'");
            secondResult = dbg.Evaluate("1 + 2");
        }));

        var result = compiler.LoadSourceCode("""
            binding.break
            42
            """u8);

        Assert.That(result.IntegerValue, Is.EqualTo(42));
        Assert.That(firstResult!.IsError, Is.True);
        Assert.That(secondResult, Is.Not.Null);
        Assert.That(secondResult!.IsError, Is.False);
        Assert.That(secondResult.Value.IntegerValue, Is.EqualTo(3));
    }

    [Test]
    public void Eval_AccessLocalsViaBindingHandle()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            // Locals are also reachable explicitly through the bound RBinding. This path
            // is kept as a low-level escape hatch alongside the bare-identifier path.
            mrb.SetGlobalVariable(mrb.Intern("$__binding"u8), new MRubyValue(ev.Binding));
            evalResult = dbg.Evaluate("$__binding.local_variable_get(:x) * 10");
        }));

        compiler.LoadSourceCode("""
            x = 5
            binding.break
            """u8);

        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.Value.IntegerValue, Is.EqualTo(50));
    }

    [Test]
    public void Eval_BareLocalIdentifier_ResolvesToBindingLocal()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            // Bare identifier matching a captured local should resolve to that local's
            // value, not fall through to method dispatch on self.
            evalResult = dbg.Evaluate("hero");
        }));

        compiler.LoadSourceCode("""
            hero = "knight"
            binding.break
            """u8);

        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.DisplayString, Is.EqualTo("\"knight\""));
    }

    [Test]
    public void Eval_LocalIdentifierInExpression_Works()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            evalResult = dbg.Evaluate("hp + bonus");
        }));

        compiler.LoadSourceCode("""
            hp = 100
            bonus = 25
            binding.break
            """u8);

        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.Value.IntegerValue, Is.EqualTo(125));
    }

    [Test]
    public void Eval_BindingLocalVariableSet_WritesBackToOuterScope()
    {
        // Regression: typing `binding.local_variable_set(:x, 5)` in the debug REPL must
        // mutate the *outer* (binding.break) scope's local, not a throwaway eval-scope
        // binding. The wrapper shadows Kernel#binding with the captured outer binding.
        EvalResult? setResult = null;
        EvalResult? getResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            setResult = dbg.Evaluate("binding.local_variable_set(:hp, 999)");
            getResult = dbg.Evaluate("hp");
        }));

        var result = compiler.LoadSourceCode("""
            hp = 100
            binding.break
            hp
            """u8);

        Assert.That(setResult, Is.Not.Null);
        Assert.That(setResult!.IsError, Is.False);
        Assert.That(getResult, Is.Not.Null);
        Assert.That(getResult!.Value.IntegerValue, Is.EqualTo(999),
            "the second eval must see the value the first eval set");
        Assert.That(result.IntegerValue, Is.EqualTo(999),
            "the script's post-binding read of hp must see the value the REPL set");
    }

    [Test]
    public void Eval_LocalIdentifier_MultilineUserSourceStillCompiles()
    {
        // The wrapper prefix stays on a single line so user-source line numbers in
        // error reports are preserved (column shifted, but line preserved).
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            evalResult = dbg.Evaluate("a = name.length\na * 2");
        }));

        compiler.LoadSourceCode("""
            name = "sword"
            binding.break
            """u8);

        Assert.That(evalResult, Is.Not.Null);
        Assert.That(evalResult!.IsError, Is.False);
        Assert.That(evalResult.Value.IntegerValue, Is.EqualTo(10));
    }

    [Test]
    public void Eval_DoesNotLeakTemporaryGlobal()
    {
        EvalResult? firstEval = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            firstEval = dbg.Evaluate("x");
        }));

        compiler.LoadSourceCode("""
            x = 42
            binding.break
            """u8);

        Assert.That(firstEval!.IsError, Is.False);
        // The wrapper relies on $__chibiruby_dbg_binding internally; it must be cleared
        // after the eval session so callers can't accidentally observe it.
        Assert.That(mrb.GlobalVariableDefined(mrb.Intern("$__chibiruby_dbg_binding"u8)), Is.False);
    }

    [Test]
    public void MultipleEvals_BeforeContinue()
    {
        var results = new System.Collections.Generic.List<EvalResult>();
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            results.Add(dbg.Evaluate("1"));
            results.Add(dbg.Evaluate("2"));
            results.Add(dbg.Evaluate("3"));
        }));

        compiler.LoadSourceCode("binding.break"u8);

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].Value.IntegerValue, Is.EqualTo(1));
        Assert.That(results[1].Value.IntegerValue, Is.EqualTo(2));
        Assert.That(results[2].Value.IntegerValue, Is.EqualTo(3));
    }

    [Test]
    public void Eval_TypeErrorIsReportedNotPropagated()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            // Implicit raise: integer + string raises TypeError.
            evalResult = dbg.Evaluate("1 + 'two'");
        }));

        var result = compiler.LoadSourceCode("""
            binding.break
            :done
            """u8);

        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("done"u8)));
        Assert.That(evalResult!.IsError, Is.True);
    }

    [Test]
    public void Eval_Raise_WhenStoppedInsideMethod()
    {
        EvalResult? evalResult = null;
        int? finalReturn = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            evalResult = dbg.Evaluate("raise 'inside-method'");
        }));

        // binding.break is called from inside a method body so the surrounding call stack
        // depth is non-trivial (root + method + Send :binding + Send :break).
        var result = compiler.LoadSourceCode("""
            def f(n)
              binding.break
              n * 2
            end
            f(21)
            """u8);

        finalReturn = (int)result.IntegerValue;
        Assert.That(finalReturn, Is.EqualTo(42));
        Assert.That(evalResult!.IsError, Is.True);
        Assert.That(evalResult.DisplayString, Does.Contain("inside-method"));
    }

    [Test]
    public void Eval_RaiseInNestedMethod_IsContained()
    {
        EvalResult? evalResult = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            evalResult = dbg.Evaluate("""
                def helper
                  raise 'deep'
                end
                helper
                """);
        }));

        var result = compiler.LoadSourceCode("""
            binding.break
            42
            """u8);

        Assert.That(result.IntegerValue, Is.EqualTo(42));
        Assert.That(evalResult!.IsError, Is.True);
        Assert.That(evalResult.DisplayString, Does.Contain("deep"));
    }

    [Test]
    public void StopEvent_CarriesBinding()
    {
        StopEvent? captured = null;
        using var debugger = CreateAttached(new ScriptedClient((dbg, ev) =>
        {
            captured = ev;
        }));

        compiler.LoadSourceCode("""
            zz = 99
            binding.break
            """u8);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Reason, Is.EqualTo(StopReason.BindingBreak));
        Assert.That(captured.Binding.TryGetLocal(mrb.Intern("zz"u8), out var v), Is.True);
        Assert.That(v.IntegerValue, Is.EqualTo(99));
    }
}
