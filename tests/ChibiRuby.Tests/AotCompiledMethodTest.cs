using System.Diagnostics;
using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

// PoC for the AOT codegen path: a hot Ruby method gets a hand-written C# body
// (standing in for what a build-time Ruby->C# source generator would emit),
// attached behind type/shape guards, with deopt to the bytecode interpreter on a
// guard miss. The body is ordinary compiled C# (no runtime IL), so it is
// IL2CPP / NativeAOT compatible.
[TestFixture]
public class AotCompiledMethodTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    MRubyValue Exec(ReadOnlySpan<byte> code)
    {
        using var compilation = compiler.Compile(code);
        return mrb.LoadBytecode(compilation.AsBytecode());
    }

    RProc MethodProc(RClass owner, ReadOnlySpan<byte> name)
    {
        Assert.That(mrb.TryFindMethod(owner, mrb.Intern(name), out var method, out _), Is.True);
        return method.Proc!;
    }

    // The "generated" body for `def sum; @x + @y; end` on a class whose @x,@y are
    // Integers. Guards: receiver is an RObject and both ivars are fixnums. Anything
    // else -> return false -> the VM interprets the original bytecode (deopt).
    CompiledRubyMethodBody SumCompiledBody()
    {
        var xSym = mrb.Intern("@x"u8);
        var ySym = mrb.Intern("@y"u8);
        return (MRubyState state, int sp, out MRubyValue result) =>
        {
            var self = state.Context.Stack[sp];
            if (self.Object is RObject obj)
            {
                var x = obj.InstanceVariables.Get(xSym);
                var y = obj.InstanceVariables.Get(ySym);
                if (x.IsFixnum && y.IsFixnum)
                {
                    result = new MRubyValue(x.FixnumValue + y.FixnumValue);
                    return true;
                }
            }
            result = default;
            return false; // deopt
        };
    }

    [Test]
    public void CompiledBodyReturnsCorrectValue()
    {
        Exec("""
             class Point
               def initialize; @x = 3; @y = 4; end
               def sum; @x + @y; end
             end
             $p = Point.new
             """u8);

        var pointClass = mrb.ClassOf(Exec("$p"u8));
        MethodProc(pointClass, "sum"u8).Irep.CompiledBody = SumCompiledBody();

        Assert.That(Exec("$p.sum"u8), Is.EqualTo(new MRubyValue(7)));
    }

    [Test]
    public void DeoptsToInterpreterWhenGuardFails()
    {
        Exec("""
             class Point
               def initialize; @x = 3; @y = 4; end
               def sum; @x + @y; end
             end
             $p = Point.new
             """u8);

        var pointClass = mrb.ClassOf(Exec("$p"u8));
        MethodProc(pointClass, "sum"u8).Irep.CompiledBody = SumCompiledBody();

        // Float ivar: compiled guard (both fixnum) misses -> interpreter runs the
        // original @x + @y producing a Float. Proves deopt correctness.
        Exec("$p.instance_variable_set(:@x, 1.5)"u8);
        Assert.That(Exec("$p.sum"u8).FloatValue, Is.EqualTo(5.5));

        // Back to fixnum -> compiled path again.
        Exec("$p.instance_variable_set(:@x, 10)"u8);
        Assert.That(Exec("$p.sum"u8), Is.EqualTo(new MRubyValue(14)));
    }

    [Test]
    public void CodegenEmitsCSharpForSimpleMethod()
    {
        Exec("""
             class Point
               def initialize; @x = 3; @y = 4; end
               def sum; @x + @y; end
             end
             $p = Point.new
             """u8);

        var sumIrep = MethodProc(mrb.ClassOf(Exec("$p"u8)), "sum"u8).Irep;
        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, sumIrep, "Sum", out var gen);

        Assert.That(gen, Is.Not.Null, "sum should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);

        // Symbols interned once into per-method static fields (not per call). The field name
        // carries the sanitized symbol name for readability (@x -> __sym0_at_x), and the name is
        // interned from statically-initialized UTF-8 bytes (@x -> { 64, 120 }), not a UTF-16 string.
        Assert.That(gen.Source, Does.Contain("Sum__sym0_at_x_u8 = new byte[] { 64, 120 };"));
        Assert.That(gen.Source, Does.Contain("Sum__sym0_at_x = state.Intern(Sum__sym0_at_x_u8)"));
        Assert.That(gen.Source, Does.Contain("Sum__sym1_at_y = state.Intern(Sum__sym1_at_y_u8)"));
        // Both ivar reads are on self, so the (RObject)self cast is hoisted ONCE into a local and
        // both reads go through it via the RObject IvarGet overload (no per-access castclass).
        Assert.That(gen.Source, Does.Contain("= v0.As<global::ChibiRuby.RObject>();"));
        Assert.That(
            gen.Source.Split("v0.As<global::ChibiRuby.RObject>()").Length - 1, Is.EqualTo(1),
            "self cast emitted once, not per ivar access");
        Assert.That(gen.Source, Does.Contain("__ro0.InstanceVariables.Get(Sum__sym0_at_x)"));
        Assert.That(gen.Source, Does.Contain("__ro0.InstanceVariables.Get(Sum__sym1_at_y)"));
        Assert.That(gen.Source, Does.Contain(".IsFixnum"));
        Assert.That(gen.Source, Does.Contain(".FixnumValue + "));
        Assert.That(gen.Source, Does.Contain("return true;"));
        Assert.That(gen.Source, Does.Contain("return false;")); // deopt on guard miss
    }

    [Test]
    public void CodegenCompilesLoopMethod()
    {
        // A `while` loop is a backward branch: the codegen lowers it to a C# goto/label loop
        // (fully boxed, deopt-free) rather than bailing to the interpreter.
        Exec("""
             class Counter
               def count_to(n)
                 i = 0
                 while i < n
                   i = i + 1
                 end
                 i
               end
             end
             $c = Counter.new
             """u8);

        var irep = MethodProc(mrb.ClassOf(Exec("$c"u8)), "count_to"u8).Irep;
        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, irep, "CountTo", out var gen);
        Assert.That(gen, Is.Not.Null);
        Assert.That(gen!.Source, Does.Contain("goto L"));   // loop back-edge lowered to a goto
        Assert.That(gen.Source, Does.Match(@"L\d+: ;"));     // loop header label
    }

    [Test]
    public void CodegenUnboxesFloatLoop()
    {
        // Phase 2+3: a float-heavy while loop whose arg is used numerically gets a method-entry
        // Fixnum guard + sound MUST typing, so the float chain lowers to RAW `double` locals (FP
        // registers, no per-op IsFixnum/IsFloat dispatch, no MRubyValue box). `2.0 * x / size`
        // proves Float once `size` (arg) and `x` (counter) are typed Fixnum.
        Exec("""
             class Mb
               def run(size)
                 x = 0
                 sum = 0.0
                 while x < size
                   sum = sum + (2.0 * x / size) - 1.5
                   x = x + 1
                 end
                 sum
               end
             end
             $m = Mb.new
             """u8);

        var irep = MethodProc(mrb.ClassOf(Exec("$m"u8)), "run"u8).Irep;
        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, irep, "Run", out var gen);
        Assert.That(gen, Is.Not.Null);
        // Method-entry argument guard for the numerically-used arg `size`.
        Assert.That(gen!.Source, Does.Contain(".IsFixnum) { result = default; return false; }"));
        // Raw double loop locals (FP registers) + the fixnum operand read as (double) in mixed arith.
        Assert.That(gen.Source, Does.Match(@"\n\s*double d\d+"));   // a raw double local is declared
        Assert.That(gen.Source, Does.Contain("(double)"));          // fixnum operand read as double
        Assert.That(gen.Source, Does.Match(@"d\d+ = d\d+ [*/+-]")); // raw double arithmetic, no box
    }

    [Test]
    public void CodegenScalarReplacesLocalArrayLiteral()
    {
        // A non-escaping `[a,b,c]` accessed by constant index is replaced with per-element locals
        // (no RArray allocation). An array that escapes (passed to a method) keeps the allocation.
        Exec("""
             class Arr
               def pick(a, b, c)
                 t = [a, b, c]
                 t[0] + t[2]
               end
               def keep(a, b)
                 t = [a, b]
                 t.length
               end
             end
             $a = Arr.new
             """u8);

        var cls = mrb.ClassOf(Exec("$a"u8));
        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, MethodProc(cls, "pick"u8).Irep, "Pick", out var pick);
        Assert.That(pick, Is.Not.Null);
        Assert.That(pick!.Source, Does.Not.Contain("state.NewArray")); // literal eliminated
        Assert.That(pick.Source, Does.Match(@"av\d+_0"));               // element local

        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, MethodProc(cls, "keep"u8).Irep, "Keep", out var keep);
        Assert.That(keep, Is.Not.Null);
        Assert.That(keep!.Source, Does.Contain("state.NewArray"));      // escapes -> still allocated
    }

    [Test]
    public void CodegenScalarReplacesArrayNew()
    {
        // `Array.new(const)` with constant-index access and no escape becomes nil-init element
        // locals (no `:new` dispatch, no allocation).
        Exec("""
             class An
               def build(a, b)
                 t = Array.new(3)
                 t[0] = a; t[1] = b; t[2] = a + b
                 t[0] + t[1] + t[2]
               end
             end
             $n = An.new
             """u8);

        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, MethodProc(mrb.ClassOf(Exec("$n"u8)), "build"u8).Irep, "Build", out var gen);
        Assert.That(gen, Is.Not.Null);
        Assert.That(gen!.Source, Does.Not.Contain(", new")); // no `:new` dispatch (selector send gone)
        Assert.That(gen.Source, Does.Contain("MRubyValue.Nil")); // nil-initialized element locals
        Assert.That(gen.Source, Does.Match(@"av\d+_2"));         // element local for index 2
    }

    [Test]
    public void CodegenScalarReplacesConstKeyHash()
    {
        // A `{k => v}` with constant keys, constant-key access, and no escape becomes per-key locals
        // (no RHash allocation, no [] / []= dispatch). A method that compiles with a hash literal at
        // all also proves OP_HASH lowering works (it previously bailed entirely).
        Exec("""
             class Hh
               def build(a, b)
                 t = { :x => a, :y => b }
                 t[:x] + t[:y]
               end
               def keep(a)
                 t = { :x => a }
                 t                  # escapes -> real hash
               end
             end
             $h = Hh.new
             """u8);

        var cls = mrb.ClassOf(Exec("$h"u8));
        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, MethodProc(cls, "build"u8).Irep, "Build", out var build);
        Assert.That(build, Is.Not.Null);
        Assert.That(build!.Source, Does.Not.Contain("state.NewHash")); // hash eliminated
        Assert.That(build.Source, Does.Match(@"hv\d+_s\d+"));           // per-key local

        ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.TryCompileMethod(mrb, MethodProc(cls, "keep"u8).Irep, "Keep", out var keep);
        Assert.That(keep, Is.Not.Null);
        Assert.That(keep!.Source, Does.Contain("state.NewHash"));       // escapes -> allocated
    }

    [Test]
    public void FingerprintBindsCompiledBodyAtLoad()
    {
        byte[] bytes;
        using (var compilation = compiler.Compile("""
             class Point
               def initialize; @x = 3; @y = 4; end
               def sum; @x + @y; end
             end
             $p = Point.new
             """u8))
        {
            bytes = compilation.AsBytecode().ToArray();
        }

        // First load = stand-in for "build time": define Point, get sum's irep,
        // compute its fingerprint, register a body under it.
        mrb.LoadBytecode(bytes);
        var sumIrep1 = MethodProc(mrb.ClassOf(Exec("$p"u8)), "sum"u8).Irep;
        Assert.That(sumIrep1.CompiledBody, Is.Null, "nothing registered yet");

        var fingerprint = mrb.ComputeIrepFingerprint(sumIrep1);
        mrb.RegisterCompiledMethod(fingerprint, SumCompiledBody());

        // Re-load the SAME bytecode: a fresh sum irep with the same fingerprint must
        // auto-bind the body at parse time — no names, no explicit attach.
        mrb.LoadBytecode(bytes);
        var sumIrep2 = MethodProc(mrb.ClassOf(Exec("$p"u8)), "sum"u8).Irep;
        Assert.That(sumIrep2.CompiledBody, Is.Not.Null, "auto-bound by fingerprint at load");
        Assert.That(Exec("$p.sum"u8), Is.EqualTo(new MRubyValue(7)));
    }

    [Test]
    public void DifferentBytecodeDoesNotBind()
    {
        var body = SumCompiledBody();

        // Register the body under the fingerprint of one method...
        mrb.LoadBytecode(compiler.Compile("""
             class A
               def sum; @x + @y; end
             end
             """u8).AsBytecode().ToArray());
        var aSum = MethodProc(mrb.ClassOf(Exec("A.new"u8)), "sum"u8).Irep;
        mrb.RegisterCompiledMethod(mrb.ComputeIrepFingerprint(aSum), body);

        // ...a structurally different method must NOT bind it (different fingerprint).
        mrb.LoadBytecode(compiler.Compile("""
             class B
               def sum; @x - @y; end
             end
             """u8).AsBytecode().ToArray());
        var bSum = MethodProc(mrb.ClassOf(Exec("B.new"u8)), "sum"u8).Irep;
        Assert.That(bSum.CompiledBody, Is.Null, "different bytecode -> different fingerprint -> no bind");
    }

    [Test]
    public void CompiledBodyViaCSharpSend()
    {
        Exec("""
             class Point
               def initialize; @x = 3; @y = 4; end
               def sum; @x + @y; end
             end
             $p = Point.new
             """u8);

        var pointVal = Exec("$p"u8);
        var pointClass = mrb.ClassOf(pointVal);
        MethodProc(pointClass, "sum"u8).Irep.CompiledBody = SumCompiledBody();
        var sumSym = mrb.Intern("sum"u8);

        // C#-initiated Send (SendWithStackPointer path): must use the compiled body
        // and keep the call stack consistent across repeated calls.
        for (var i = 0; i < 5; i++)
        {
            Assert.That(mrb.Send(pointVal, sumSym), Is.EqualTo(new MRubyValue(7)));
        }

        // Deopt through the C# Send path as well.
        Exec("$p.instance_variable_set(:@x, 1.5)"u8);
        Assert.That(mrb.Send(pointVal, sumSym).FloatValue, Is.EqualTo(5.5));
        Exec("$p.instance_variable_set(:@x, 3)"u8);
        Assert.That(mrb.Send(pointVal, sumSym), Is.EqualTo(new MRubyValue(7)));
    }

    [Test]
    public void CompiledIsFasterThanInterpreted()
    {
        Exec("""
             class Point
               def initialize; @x = 3; @y = 4; end
               def sum; @x + @y; end
             end
             $p = Point.new
             def run_bench(n)
               i = 0
               s = 0
               while i < n
                 s = $p.sum
                 i = i + 1
               end
               s
             end
             """u8);

        var pointClass = mrb.ClassOf(Exec("$p"u8));
        var sumProc = MethodProc(pointClass, "sum"u8);
        var compiled = SumCompiledBody();

        const int n = 3_000_000;

        var benchSrc = System.Text.Encoding.UTF8.GetBytes($"run_bench({n})");

        // Warm both paths.
        sumProc.Irep.CompiledBody = null;
        Exec("run_bench(100000)"u8);
        sumProc.Irep.CompiledBody = compiled;
        Exec("run_bench(100000)"u8);

        // Interpreted.
        sumProc.Irep.CompiledBody = null;
        var swInterp = Stopwatch.StartNew();
        var interpResult = Exec(benchSrc);
        swInterp.Stop();

        // Compiled.
        sumProc.Irep.CompiledBody = compiled;
        var swCompiled = Stopwatch.StartNew();
        var compiledResult = Exec(benchSrc);
        swCompiled.Stop();

        TestContext.Out.WriteLine(
            $"sum x{n}: interpreted={swInterp.Elapsed.TotalMilliseconds:F1}ms compiled={swCompiled.Elapsed.TotalMilliseconds:F1}ms " +
            $"speedup={swInterp.Elapsed.TotalMilliseconds / swCompiled.Elapsed.TotalMilliseconds:F2}x");

        Assert.That(compiledResult, Is.EqualTo(new MRubyValue(7)));
        Assert.That(interpResult, Is.EqualTo(new MRubyValue(7)));
    }
}
