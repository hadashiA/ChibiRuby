using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiRuby.Compiler;
using ChibiRuby.JetPack;
using ChibiRuby.JetPack.Mrb2Cs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ChibiRuby.Tests;

// Closes the codegen loop: Ruby -> generated C# (Mrb2Cs) -> Roslyn compile
// -> delegate -> register by fingerprint -> reload auto-binds it -> run the GENERATED
// code through the VM, including deopt. Proves the emitted C# actually compiles and
// runs correctly (not just that it looks right).
[TestFixture]
public class AotCodegenEndToEndTest
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

    Irep MethodIrep(RClass owner, ReadOnlySpan<byte> name)
    {
        Assert.That(mrb.TryFindMethod(owner, mrb.Intern(name), out var method, out _), Is.True);
        return method.Proc!.Irep;
    }

    [Test]
    public void GeneratedCSharpCompilesRunsAndDeopts()
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

        mrb.LoadBytecode(bytes);
        var sumIrep = MethodIrep(mrb.ClassOf(Exec("$p"u8)), "sum"u8);

        // Generate C# from the method's RubyIR.
        Mrb2CsCompiler.TryCompileMethod(mrb, sumIrep, "Sum", out var gen);
        Assert.That(gen, Is.Not.Null);

        // Compile the generated C# into a real assembly and bind it as the body.
        var asm = CompileToAssembly("public sealed class GeneratedAot : global::ChibiRuby.AotGeneratedMethods\n{\n" + gen!.Source + "\n}\n");
        var method = asm.GetType("GeneratedAot")!.GetMethod("Sum", BindingFlags.Public | BindingFlags.Static)!;
        var body = (CompiledRubyMethodBody)method.CreateDelegate(typeof(CompiledRubyMethodBody));

        // Register by fingerprint; re-load -> the fresh sum irep auto-binds the body.
        mrb.RegisterCompiledMethod(mrb.ComputeIrepFingerprint(sumIrep), body);
        mrb.LoadBytecode(bytes);
        var reboundIrep = MethodIrep(mrb.ClassOf(Exec("$p"u8)), "sum"u8);
        Assert.That(reboundIrep.CompiledBody, Is.Not.Null, "generated body auto-bound by fingerprint");

        // The GENERATED C# runs (3 + 4 = 7).
        Assert.That(Exec("$p.sum"u8), Is.EqualTo(new MRubyValue(7)));

        // Deopt: Float ivar -> generated fixnum guard misses -> interpreter -> 5.5.
        Exec("$p.instance_variable_set(:@x, 1.5)"u8);
        Assert.That(Exec("$p.sum"u8).FloatValue, Is.EqualTo(5.5));

        // Back to fixnum -> generated fast path again.
        Exec("$p.instance_variable_set(:@x, 10)"u8);
        Assert.That(Exec("$p.sum"u8), Is.EqualTo(new MRubyValue(14)));
    }

    [Test]
    public void GeneratedBranchingMethodCompilesAndRuns()
    {
        Exec("""
             class Box
               def initialize; @x = 5; @y = 9; end
               def maxxy
                 if @x < @y
                   @y
                 else
                   @x
                 end
               end
             end
             $b = Box.new
             """u8);

        var irep = MethodIrep(mrb.ClassOf(Exec("$b"u8)), "maxxy"u8);
        Mrb2CsCompiler.TryCompileMethod(mrb, irep, "MaxXY", out var gen);
        Assert.That(gen, Is.Not.Null, "branch+compare method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);

        var asm = CompileToAssembly("public sealed class GeneratedAot : global::ChibiRuby.AotGeneratedMethods\n{\n" + gen.Source + "\n}\n");
        var method = asm.GetType("GeneratedAot")!.GetMethod("MaxXY", BindingFlags.Public | BindingFlags.Static)!;
        irep.CompiledBody = (CompiledRubyMethodBody)method.CreateDelegate(typeof(CompiledRubyMethodBody));

        Assert.That(Exec("$b.maxxy"u8), Is.EqualTo(new MRubyValue(9)), "@x<@y -> @y");
        Exec("$b.instance_variable_set(:@x, 20)"u8);
        Assert.That(Exec("$b.maxxy"u8), Is.EqualTo(new MRubyValue(20)), "@x>=@y -> @x");
    }

    [Test]
    public void GeneratedSendAndIndexMethodsCompileAndRun()
    {
        Exec("""
             class Box
               def initialize; @arr = [10, 20, 30]; @i = 1; end
               def helper(n); n * 2; end
               def at; @arr[@i]; end       # GetIndex -> []
               def via_send; helper(@i); end  # self-send with 1 arg
             end
             $b = Box.new
             """u8);

        var boxClass = mrb.ClassOf(Exec("$b"u8));
        CompileAndBind(boxClass, "at"u8, "At");
        CompileAndBind(boxClass, "via_send"u8, "ViaSend");

        Assert.That(Exec("$b.at"u8), Is.EqualTo(new MRubyValue(20)), "@arr[@i] = @arr[1] = 20");
        Assert.That(Exec("$b.via_send"u8), Is.EqualTo(new MRubyValue(2)), "helper(@i) = 1*2 = 2");
    }

    [Test]
    public void GeneratedFloatArithmeticCompilesAndRuns()
    {
        // All-float receivers/args exercise the dual-path's float branch (not the fixnum
        // branch, not deopt). Results returned (boxed) so the chains stay boxed -> compiled.
        Exec("""
             class FVec
               def initialize; @x = 1.5; @y = 2.5; @z = 4.0; end
               def addf(b); @x + b; end
               def subf(b); @x - b; end
               def mulf(b); @y * b; end
               def divf(b); @z / b; end
               def ltf(b);  @x < b; end
               def addi;    @x + 2; end   # float + fixnum immediate -> coercion to float
             end
             $f = FVec.new
             """u8);

        var fvec = mrb.ClassOf(Exec("$f"u8));
        CompileAndBind(fvec, "addf"u8, "AddF");
        CompileAndBind(fvec, "subf"u8, "SubF");
        CompileAndBind(fvec, "mulf"u8, "MulF");
        CompileAndBind(fvec, "divf"u8, "DivF");
        CompileAndBind(fvec, "ltf"u8, "LtF");
        CompileAndBind(fvec, "addi"u8, "AddI");

        Assert.That(Exec("$f.addf(2.25)"u8).FloatValue, Is.EqualTo(3.75), "1.5 + 2.25");
        Assert.That(Exec("$f.subf(0.5)"u8).FloatValue, Is.EqualTo(1.0), "1.5 - 0.5");
        Assert.That(Exec("$f.mulf(2.0)"u8).FloatValue, Is.EqualTo(5.0), "2.5 * 2.0");
        Assert.That(Exec("$f.divf(2.0)"u8).FloatValue, Is.EqualTo(2.0), "4.0 / 2.0");
        Assert.That(Exec("$f.ltf(2.0)"u8), Is.EqualTo(MRubyValue.True), "1.5 < 2.0");
        Assert.That(Exec("$f.ltf(1.0)"u8), Is.EqualTo(MRubyValue.False), "1.5 < 1.0 false");
        Assert.That(Exec("$f.addi"u8).FloatValue, Is.EqualTo(3.5), "1.5 + 2 -> 3.5 float");

        // Mixed fixnum/float still deopts to the interpreter and stays correct (coercion).
        Assert.That(Exec("$f.addf(3)"u8).FloatValue, Is.EqualTo(4.5), "1.5 + 3 (mixed) -> 4.5");
    }

    [Test]
    public void ProvablyFloatTempsAreDoubleUnboxed()
    {
        // a,b are float literals (provably Float); x,y,z are float-arith-only temps. They are
        // held as raw `double` locals and the chain emits guard-free double arithmetic (no
        // per-op IsFloat check), re-boxing only at the returned boundary.
        Exec("""
             class Dbl
               def calc
                 a = 3.0
                 b = 2.0
                 x = a * b
                 y = a * a
                 z = b * b
                 x + y + z
               end
             end
             $d = Dbl.new
             """u8);

        var dbl = mrb.ClassOf(Exec("$d"u8));
        Mrb2CsCompiler.TryCompileMethod(mrb, MethodIrep(dbl, "calc"u8), "Calc", out var gen);
        Assert.That(gen, Is.Not.Null);
        TestContext.Out.WriteLine(gen!.Source);
        // Double-unboxing fired: raw `double` locals, and a pure-double op reads a double local
        // directly (` = d<n> ` with a double operand) rather than the boxed `.IsFloat` dual-path.
        Assert.That(gen.Source, Does.Contain("double d"), "float-arith temps held as raw double");
        Assert.That(gen.Source, Does.Match(@"= d\d+ [*/+-] "), "guard-free pure-double arithmetic emitted");

        CompileAndBind(dbl, "calc"u8, "Calc2");
        Assert.That(Exec("$d.calc"u8).FloatValue, Is.EqualTo(19.0), "3*2 + 3*3 + 2*2 = 19.0");
    }

    [Test]
    public void GeneratedToFFastPathCompilesAndFallsBack()
    {
        Exec("""
             class Conv
               def add_half(x); x.to_f + 0.5; end
               def convert(x); x.to_f; end
             end
             $conv = Conv.new
             """u8);

        var conv = mrb.ClassOf(Exec("$conv"u8));
        Mrb2CsCompiler.TryCompileMethod(mrb, MethodIrep(conv, "add_half"u8), "AddHalf", out var gen);
        Assert.That(gen, Is.Not.Null, "to_f fast path method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.Source, Does.Contain(".IsFixnum"), "Integer#to_f emits an immediate fast path");
        Assert.That(gen.Source, Does.Contain("state.Send"), "non-numeric receivers still fall back to Ruby dispatch");

        CompileAndBind(conv, "add_half"u8, "AddHalf2");
        CompileAndBind(conv, "convert"u8, "Convert");

        Assert.That(Exec("$conv.add_half(2)"u8).FloatValue, Is.EqualTo(2.5));
        Assert.That(Exec("$conv.add_half(2.25)"u8).FloatValue, Is.EqualTo(2.75));
        Assert.That(Exec("$conv.convert('3.5')"u8).FloatValue, Is.EqualTo(3.5), "String#to_f uses Send fallback");
    }

    [Test]
    public void GeneratedPureUnarySendFastPathFallsBackAfterRedefinition()
    {
        mrb.DefineModule(mrb.Intern("FastMath"u8), mod =>
        {
            mod.DefineClassMethod(mrb.Intern("sqrt"u8), new MRubyMethod(
                (s, self) => System.Math.Sqrt(s.GetArgumentAsFloatAt(0)),
                (s, self, argument) => System.Math.Sqrt(s.AsFloat(argument)),
                (_, _, argument) => System.Math.Sqrt(argument)));
        });
        Exec("""
             class UsesFastMath
               def root_plus_one(x)
                 FastMath.sqrt(x) + 1.0
               end
             end
             $ufm = UsesFastMath.new
             """u8);

        var usesFastMath = mrb.ClassOf(Exec("$ufm"u8));
        Mrb2CsCompiler.TryCompileMethod(mrb, MethodIrep(usesFastMath, "root_plus_one"u8), "RootPlusOne", out var gen);
        Assert.That(gen, Is.Not.Null, "pure-unary send method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.Source, Does.Contain("PureUnarySendUnsafe"));
        Assert.That(gen.Source, Does.Contain("RootPlusOne__pu0method"), "pure-unary send uses a per-site method cache");

        CompileAndBind(usesFastMath, "root_plus_one"u8, "RootPlusOne2");
        Assert.That(Exec("$ufm.root_plus_one(9.0)"u8).FloatValue, Is.EqualTo(4.0));

        Exec("""
             module FastMath
               def self.sqrt(x)
                 100.0
               end
             end
             """u8);
        Assert.That(Exec("$ufm.root_plus_one(9.0)"u8).FloatValue, Is.EqualTo(101.0), "redefined method uses Send fallback");
    }

    [Test]
    public void NonEscapingNewIsScalarReplaced()
    {
        // V.new is a temporary that never escapes d2/bump — it must be scalar-replaced:
        // no allocation, accessor sends become field-local reads/writes.
        Exec("""
             class V
               def initialize(x, y); @x = x; @y = y; end
               def x; @x; end
               def y; @y; end
               def x=(v); @x = v; end
             end
             class Calc
               def initialize; @ox = 0.5; @oy = 0.25; end
               def d2(px, py)
                 v = V.new(px - @ox, py - @oy)
                 v.x * v.x + v.y * v.y
               end
               def bump(px)
                 v = V.new(px, 0.5)
                 v.x = v.x + v.y
                 v.x
               end
               def isum(a, b)
                 v = V.new(a, b)
                 v.x + v.y
               end
             end
             $c = Calc.new
             """u8);

        var calc = mrb.ClassOf(Exec("$c"u8));

        // Inspect the generated C# for d2: the V.new must be gone (no Send/VirtualNew dispatch),
        // replaced by field locals + a validity guard.
        var d2Irep = MethodIrep(calc, "d2"u8);
        Mrb2CsCompiler.TryCompileMethod(mrb, d2Irep, "D2", out var gen);
        Assert.That(gen, Is.Not.Null, "non-escaping new method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.Source, Does.Contain("ClassMethodGuardUnsafe"), "scalar new emits a validity guard");
        Assert.That(gen.Source, Does.Contain("so"), "the Vec fields live as scalar locals (soNN_*), not a heap object");

        CompileAndBind(calc, "d2"u8, "D2b");
        CompileAndBind(calc, "bump"u8, "Bump");
        CompileAndBind(calc, "isum"u8, "ISum");

        // d2(1.5, 2.0): vx=1.0, vy=1.75 -> 1.0 + 3.0625 = 4.0625
        Assert.That(Exec("$c.d2(1.5, 2.0)"u8).FloatValue, Is.EqualTo(4.0625));
        // bump(2.0): v.x = 2.0 + 0.5 = 2.5  (setter writes the field local, then read back)
        Assert.That(Exec("$c.bump(2.0)"u8).FloatValue, Is.EqualTo(2.5));
        // Pure-integer non-escaping new stays scalar (no deopt): isum(3,4) = 7 fixnum.
        Assert.That(Exec("$c.isum(3, 4)"u8), Is.EqualTo(new MRubyValue(7)));
    }

    [Test]
    public void SelfSendIrSpliceEnablesScalarReplacementAcrossCall()
    {
        Exec("""
             class VSplice
               def initialize(x, y); @x = x; @y = y; end
               def x; @x; end
               def y; @y; end
               def x=(v); @x = v; end
             end
             class DriverSplice
               def touch(v)
                 v.x = v.x + 1.0
               end
               def run(a)
                 v = VSplice.new(a, 0.5)
                 touch(v)
                 v.x + v.y
               end
             end
             $ds = DriverSplice.new
             """u8);

        var driver = mrb.ClassOf(Exec("$ds"u8));
        var touchIrep = MethodIrep(driver, "touch"u8);
        var inlineRegistry = new Dictionary<ulong, int>
        {
            [mrb.ComputeIrepFingerprint(touchIrep)] = 1
        };

        Mrb2CsCompiler.TryCompileMethod(
            mrb,
            MethodIrep(driver, "run"u8),
            "RunSplice",
            driver,
            inlineRegistry,
            out var gen);
        Assert.That(gen, Is.Not.Null, "self-send splice method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.Source, Does.Contain("InlineGuardUnsafe"), "spliced self-send is guarded");
        Assert.That(gen.Source, Does.Contain("so"), "object passed through the spliced call is scalar-replaced");

        var aux = string.Concat(gen.AuxiliaryMethods);
        var asm = CompileToAssembly("public sealed class RunSpliceHolder : global::ChibiRuby.AotGeneratedMethods\n{\n" + gen.Source + aux + "\n}\n");
        var method = asm.GetType("RunSpliceHolder")!.GetMethod("RunSplice", BindingFlags.Public | BindingFlags.Static)!;
        MethodIrep(driver, "run"u8).CompiledBody = (CompiledRubyMethodBody)method.CreateDelegate(typeof(CompiledRubyMethodBody));

        Assert.That(Exec("$ds.run(2.0)"u8).FloatValue, Is.EqualTo(3.5));

        Exec("""
             class DriverSplice
               def touch(v)
                 v.x = 100.0
               end
             end
             """u8);
        Assert.That(Exec("$ds.run(2.0)"u8).FloatValue, Is.EqualTo(100.5));
    }

    [Test]
    public void CrossObjectIrSpliceScalarReplacesReturnedObject()
    {
        Exec("""
             class VCross
               def initialize(x, y); @x = x; @y = y; end
               def x; @x; end
               def y; @y; end
               def vadd(b)
                 VCross.new(@x + b.x, @y + b.y)
               end
               def dot(b)
                 @x * b.x + @y * b.y
               end
             end
             class CrossDriver
               def run(a, b)
                 p = VCross.new(a, b)
                 q = VCross.new(1.0, 2.0)
                 r = p.vadd(q)
                 r.dot(q)
               end
             end
             $cd = CrossDriver.new
             """u8);

        var driver = mrb.ClassOf(Exec("$cd"u8));
        var vec = mrb.ClassOf(Exec("VCross.new(0.0, 0.0)"u8));
        var inlineRegistry = new Dictionary<ulong, int>
        {
            [mrb.ComputeIrepFingerprint(MethodIrep(vec, "vadd"u8))] = 1,
            [mrb.ComputeIrepFingerprint(MethodIrep(vec, "dot"u8))] = 1
        };
        var selectorRegistry = Analyzer.BuildInlineSelectorRegistry(mrb, inlineRegistry);

        Mrb2CsCompiler.TryCompileMethod(
            mrb,
            MethodIrep(driver, "run"u8),
            "CrossRun",
            driver,
            inlineRegistry,
            accessorRegistry: null,
            inlineSelectorRegistry: selectorRegistry,
            method: out var gen);
        Assert.That(gen, Is.Not.Null, "cross-object splice method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.Source, Does.Contain("ClassMethodGuardUnsafe"), "cross-object sends are guarded");
        Assert.That(gen.Source, Does.Contain("so"), "returned VCross object is scalar-replaced through aliases");

        var asm = CompileToAssembly("public sealed class CrossRunHolder : global::ChibiRuby.AotGeneratedMethods\n{\n" + gen.Source + "\n}\n");
        var method = asm.GetType("CrossRunHolder")!.GetMethod("CrossRun", BindingFlags.Public | BindingFlags.Static)!;
        MethodIrep(driver, "run"u8).CompiledBody = (CompiledRubyMethodBody)method.CreateDelegate(typeof(CompiledRubyMethodBody));

        Assert.That(Exec("$cd.run(3.0, 4.0)"u8).FloatValue, Is.EqualTo(16.0));
    }

    void CompileAndBind(RClass owner, ReadOnlySpan<byte> rubyName, string genName)
    {
        var irep = MethodIrep(owner, rubyName);
        Mrb2CsCompiler.TryCompileMethod(mrb, irep, genName, out var gen);
        Assert.That(gen, Is.Not.Null, $"{genName} should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        var aux = string.Concat(gen.AuxiliaryMethods);
        var asm = CompileToAssembly("public sealed class " + genName + "Holder : global::ChibiRuby.AotGeneratedMethods\n{\n" + gen.Source + aux + "\n}\n");
        var method = asm.GetType(genName + "Holder")!.GetMethod(genName, BindingFlags.Public | BindingFlags.Static)!;
        irep.CompiledBody = (CompiledRubyMethodBody)method.CreateDelegate(typeof(CompiledRubyMethodBody));
    }

    [Test]
    public void SingleLevelTimesBlockIsInlinedAsLoop()
    {
        // sum_to captures `s` (written by the block via an upvar); the times block must inline
        // as a C# for loop with `s` passed by ref. No allocation, no dispatch into a block proc.
        Exec("""
             class Adder
               def sum_to(n)
                 s = 0
                 n.times { |i| s = s + i }
                 s
               end
               def sum_floats(n)
                 s = 0.0
                 n.times { |i| s = s + 0.5 }
                 s
               end
             end
             $a = Adder.new
             """u8);

        var adder = mrb.ClassOf(Exec("$a"u8));
        var irep = MethodIrep(adder, "sum_to"u8);
        Mrb2CsCompiler.TryCompileMethod(mrb, irep, "SumTo", out var gen);
        Assert.That(gen, Is.Not.Null, "times-block method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.Source, Does.Contain("for (long"), "the times block lowered to a C# for loop");
        Assert.That(gen.AuxiliaryMethods, Is.Not.Empty, "the block body is emitted as an auxiliary __blk method");

        CompileAndBind(adder, "sum_to"u8, "SumTo2");
        CompileAndBind(adder, "sum_floats"u8, "SumFloats");

        Assert.That(Exec("$a.sum_to(100)"u8), Is.EqualTo(new MRubyValue(4950)), "0+1+...+99");
        Assert.That(Exec("$a.sum_to(0)"u8), Is.EqualTo(new MRubyValue(0)), "empty loop");
        Assert.That(Exec("$a.sum_floats(4)"u8).FloatValue, Is.EqualTo(2.0), "0.5 * 4 via float upvar");
    }

    [Test]
    public void NestedTimesBlocksInlineWithUpvarPassThrough()
    {
        // The inner block writes `s` (a method local — a depth-1 upvar) and reads `i` (the outer
        // block's param — depth-0). C2 must pass `s`'s cell through the outer __blk to the inner.
        Exec("""
             class Grid
               def sum(n)
                 s = 0
                 n.times do |i|
                   n.times do |j|
                     s = s + i * j
                   end
                 end
                 s
               end
             end
             $g = Grid.new
             """u8);

        var grid = mrb.ClassOf(Exec("$g"u8));
        Mrb2CsCompiler.TryCompileMethod(mrb, MethodIrep(grid, "sum"u8), "GridSum", out var gen);
        Assert.That(gen, Is.Not.Null, "nested times-block method should be codegen-able");
        TestContext.Out.WriteLine(gen!.Source);
        Assert.That(gen.AuxiliaryMethods, Has.Count.EqualTo(2), "outer + inner block bodies");

        CompileAndBind(grid, "sum"u8, "GridSum2");
        // sum(n) = (sum 0..n-1)^2. sum(20) = 190*190 = 36100.
        Assert.That(Exec("$g.sum(20)"u8), Is.EqualTo(new MRubyValue(36100)));
        Assert.That(Exec("$g.sum(1)"u8), Is.EqualTo(new MRubyValue(0)));
    }

    static Assembly CompileToAssembly(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        trusted.Add(MetadataReference.CreateFromFile(typeof(MRubyState).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "AotGeneratedTest",
            new[] { tree },
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Generated C# failed to compile:\n{errors}\n--- source ---\n{source}");
        }

        ms.Position = 0;
        return Assembly.Load(ms.ToArray());
    }
}
