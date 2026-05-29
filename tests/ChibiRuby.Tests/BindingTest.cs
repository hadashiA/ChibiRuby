using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class BindingTest
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

    [Test]
    public void KernelBinding_ReturnsBindingInstance()
    {
        var result = compiler.LoadSourceCode("binding"u8);
        Assert.That(result.Object, Is.InstanceOf<RBinding>());
    }

    [Test]
    public void Binding_Receiver_ReturnsTopSelfAtToplevel()
    {
        var result = compiler.LoadSourceCode("binding.receiver"u8);
        // Top-level self is unique
        Assert.That(result, Is.EqualTo(new MRubyValue(mrb.TopSelf)));
    }

    [Test]
    public void Binding_LocalVariables_ExposesEnclosingLocals()
    {
        // The toplevel script has locals a, b, c; binding is called as the last
        // expression so the result is its local_variables list.
        var result = compiler.LoadSourceCode("""
            a = 1
            b = 2
            c = 3
            binding.local_variables
            """u8);

        var array = result.As<RArray>();
        var names = new System.Collections.Generic.List<string>();
        for (var i = 0; i < array.Length; i++)
        {
            names.Add(System.Text.Encoding.UTF8.GetString(mrb.NameOf(array[i].SymbolValue)));
        }
        Assert.That(names, Does.Contain("a"));
        Assert.That(names, Does.Contain("b"));
        Assert.That(names, Does.Contain("c"));
    }

    [Test]
    public void Binding_InsideBlock_SeesOuterScopeLocals()
    {
        // Regression: a binding captured inside a block must see both the block's own
        // locals (e.g. the block parameter) AND the outer scope's locals captured by
        // the block closure. Without an Upper-chain walk in RBinding's live ctor the
        // user only sees the block-local names — `msg` from the outer scope goes
        // missing in the debugger's variables view.
        var result = compiler.LoadSourceCode("""
            msg = "hello"
            captured = nil
            [10].each do |i|
              captured = binding
            end
            captured.local_variables
            """u8);

        var array = result.As<RArray>();
        var names = new System.Collections.Generic.List<string>();
        for (var i = 0; i < array.Length; i++)
        {
            names.Add(System.Text.Encoding.UTF8.GetString(mrb.NameOf(array[i].SymbolValue)));
        }
        Assert.That(names, Does.Contain("i"));        // block-local
        Assert.That(names, Does.Contain("msg"));      // outer scope
        Assert.That(names, Does.Contain("captured")); // outer scope
    }

    [Test]
    public void Binding_InsideBlock_OuterLocalReadsCurrentValue()
    {
        // Verify the captured binding reads the LIVE value of the outer-scope local —
        // i.e. it routes the read through the outer frame's stack slot, not a snapshot
        // taken at block invocation time.
        var result = compiler.LoadSourceCode("""
            x = 1
            captured = nil
            [10].each do |_|
              captured = binding
            end
            x = 999
            captured.local_variable_get(:x)
            """u8);

        // The block returns, x is reassigned, then we ask the (now-frozen) binding for x.
        // FreezeFromFrame snapshots while the outer frame is still alive — so the value
        // at freeze time (still 1) is what we get back. The block frame popped on each
        // iteration; the binding's freeze happened with the toplevel frame still alive
        // and x still == 1.
        Assert.That(result, Is.EqualTo(new MRubyValue(1L)));
    }

    [Test]
    public void Binding_LocalVariableGet_ReturnsValue()
    {
        var result = compiler.LoadSourceCode("""
            x = 42
            binding.local_variable_get(:x)
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(42));
    }

    [Test]
    public void Binding_LocalVariableDefined_True()
    {
        var result = compiler.LoadSourceCode("""
            y = "hi"
            binding.local_variable_defined?(:y)
            """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.True));
    }

    [Test]
    public void Binding_LocalVariableDefined_FalseForUnknown()
    {
        var result = compiler.LoadSourceCode("""
            y = "hi"
            binding.local_variable_defined?(:nope)
            """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.False));
    }

    [Test]
    public void Binding_LocalVariableSet_UpdatesExistingLocal()
    {
        // Note: `binding` returns a fresh snapshot each call, so the same binding object
        // must be reused to observe the mutation.
        var result = compiler.LoadSourceCode("""
            x = 1
            b = binding
            b.local_variable_set(:x, 99)
            b.local_variable_get(:x)
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(99));
    }

    [Test]
    public void Binding_LocalVariableSet_IntroducesNewLocal()
    {
        // CRuby behavior: setting a name not in the captured scope silently introduces it.
        // The binding's local_variables list grows to include the new name.
        var result = compiler.LoadSourceCode("""
            b = binding
            b.local_variable_set(:weapon, "sword")
            b.local_variable_get(:weapon)
            """u8);
        Assert.That(mrb.Stringify(result).ToString(), Is.EqualTo("sword"));
    }

    [Test]
    public void Binding_LocalVariableSet_IntroducedNameAppearsInLocalVariables()
    {
        var result = compiler.LoadSourceCode("""
            b = binding
            b.local_variable_set(:weapon, "sword")
            b.local_variables.include?(:weapon)
            """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.True));
    }

    [Test]
    public void Binding_LiveWrite_ReflectsInOriginatingFrame()
    {
        // Live binding semantics: while the frame is on the call stack, mutating a captured
        // local via the binding updates the actual register slot in the running frame.
        var result = compiler.LoadSourceCode("""
            x = 1
            b = binding
            b.local_variable_set(:x, 99)
            x
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(99));
    }

    [Test]
    public void Binding_LiveWrite_PropagatesAcrossSeveralReads()
    {
        // The mutation persists for the remainder of the frame, not just the next read.
        // Each read of `hp` after a set sees the latest written value (live semantics).
        var result = compiler.LoadSourceCode("""
            hp = 100
            b = binding
            b.local_variable_set(:hp, hp - 25)   # hp 100 -> 75
            b.local_variable_set(:hp, hp - 10)   # hp 75 -> 65 (hp is live)
            hp
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(65));
    }

    [Test]
    public void Binding_FromReturnedFrame_StillReadable()
    {
        // The frame that captured the binding has popped. The PopCallStack hook should have
        // copied the live register values into the binding's own storage, so reads still
        // return the values as of the binding-capture time.
        var result = compiler.LoadSourceCode("""
            def make
              x = 42
              binding
            end
            make.local_variable_get(:x)
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(42));
    }

    [Test]
    public void Binding_FromReturnedFrame_SeesFinalValue()
    {
        // The freeze copies values as of the moment the frame returns, not as of when
        // `binding` was called. Mutations between binding-capture and return are visible.
        var result = compiler.LoadSourceCode("""
            def make
              x = 1
              b = binding
              x = 2
              b
            end
            make.local_variable_get(:x)
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(2));
    }

    [Test]
    public void Binding_IntroducedExtra_PersistsAcrossFreeze()
    {
        // Extras introduced via local_variable_set while live remain present after the
        // frame returns.
        var result = compiler.LoadSourceCode("""
            def make
              b = binding
              b.local_variable_set(:treasure, "gem")
              b
            end
            make.local_variable_get(:treasure)
            """u8);
        Assert.That(mrb.Stringify(result).ToString(), Is.EqualTo("gem"));
    }

    [Test]
    public void Binding_FrozenWrite_AffectsBindingOnly()
    {
        // After the frame has returned, the binding is frozen. Setting an existing local now
        // writes to the binding's own storage (there's no live frame to mutate).
        var result = compiler.LoadSourceCode("""
            def make
              x = 1
              binding
            end
            b = make
            b.local_variable_set(:x, 99)
            b.local_variable_get(:x)
            """u8);
        Assert.That(result.IntegerValue, Is.EqualTo(99));
    }

    [Test]
    public void BindingIrb_RaisesWhenNoDebuggerAttached()
    {
        Assert.That(mrb.DebuggerHook, Is.Null);
        Assert.Throws<MRubyRaiseException>(() =>
            compiler.LoadSourceCode("binding.irb"u8));
    }

    sealed class CaptureHook : IMRubyDebuggerHook
    {
        public RBinding? CapturedBinding;
        public int CallCount;

        public void OnBindingIrb(MRubyState state, RBinding binding)
        {
            CapturedBinding = binding;
            CallCount++;
            // Immediately return - simulating an instant resume.
        }

        public void OnInstruction(MRubyState state, Irep irep, int pc)
        {
            // No-op for these tests; the binding.irb path is what we exercise.
        }
    }

    [Test]
    public void BindingIrb_InvokesDebuggerHook_AndContinues()
    {
        var hook = new CaptureHook();
        mrb.DebuggerHook = hook;

        var result = compiler.LoadSourceCode("""
            x = 100
            binding.irb
            x + 1
            """u8);

        Assert.That(hook.CallCount, Is.EqualTo(1));
        Assert.That(hook.CapturedBinding, Is.Not.Null);
        Assert.That(hook.CapturedBinding!.TryGetLocal(mrb.Intern("x"u8), out var xValue), Is.True);
        Assert.That(xValue.IntegerValue, Is.EqualTo(100));
        Assert.That(result.IntegerValue, Is.EqualTo(101));
    }

    [Test]
    public void Binding_CapturesLocalsInMethodBody()
    {
        // The binding is captured inside a method, and we then read locals from C# via the hook.
        var hook = new CaptureHook();
        mrb.DebuggerHook = hook;

        compiler.LoadSourceCode("""
            def f
              aaa = 7
              bbb = "hello"
              binding.irb
            end
            f
            """u8);

        Assert.That(hook.CapturedBinding, Is.Not.Null);
        var names = new System.Collections.Generic.List<string>();
        foreach (var n in hook.CapturedBinding!.LocalVariableNames)
        {
            var bytes = mrb.NameOf(n);
            names.Add($"id={n.Value} name=\"{System.Text.Encoding.UTF8.GetString(bytes)}\"");
        }
        TestContext.Out.WriteLine("locals seen: " + string.Join(" | ", names));

        Assert.That(hook.CapturedBinding!.TryGetLocal(mrb.Intern("aaa"u8), out var aVal), Is.True);
        Assert.That(aVal.IntegerValue, Is.EqualTo(7));
        Assert.That(hook.CapturedBinding!.TryGetLocal(mrb.Intern("bbb"u8), out var bVal), Is.True);
        var s = System.Text.Encoding.UTF8.GetString(bVal.As<RString>().AsSpan());
        Assert.That(s, Is.EqualTo("hello"));
    }
}
