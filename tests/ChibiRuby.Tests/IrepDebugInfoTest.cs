using System.Text;
using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class IrepDebugInfoTest
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
    public void CompileWithDebugInfo_PopulatesIrepDebugInfo()
    {
        using var c = compiler.Compile("""
            x = 1
            y = 2
            x + y
            """u8, filename: "test.rb");
        var irep = c.ToIrep();

        Assert.That(irep.DebugInfo, Is.Not.Null, "DebugInfo should be populated when debugInfo=true (default)");
        Assert.That(irep.DebugInfo!.Files.Length, Is.GreaterThan(0));
        Assert.That(irep.DebugInfo.Files[0].Filename, Is.EqualTo("test.rb"));
    }

    [Test]
    public void CompileWithoutDebugInfo_DoesNotProduceIrepDebugInfo()
    {
        using var c = compiler.Compile("x = 1\nx"u8, filename: "test.rb", options: new MRubyCompileOptions { EnableDebugInfo = false });
        var irep = c.ToIrep();
        Assert.That(irep.DebugInfo, Is.Null, "DebugInfo should be omitted when debugInfo=false");
    }

    [Test]
    public void FindLine_ReturnsSourceLineForEachPc()
    {
        using var c = compiler.Compile("""
            a = 10
            b = 20
            c = a + b
            """u8, filename: "calc.rb");
        var irep = c.ToIrep();
        Assert.That(irep.DebugInfo, Is.Not.Null);

        // Sample lines we expect to see across the pc range. Exact pc->line mapping
        // depends on the compiler, so we just check at least one pc resolves to each
        // source line.
        var seenLines = new System.Collections.Generic.HashSet<int>();
        for (var pc = 0; pc < irep.Sequence.Length; pc++)
        {
            var line = irep.DebugInfo!.FindLine(pc);
            if (line > 0) seenLines.Add(line);
        }
        Assert.That(seenLines, Does.Contain(1));
        Assert.That(seenLines, Does.Contain(2));
        Assert.That(seenLines, Does.Contain(3));
    }

    [Test]
    public void FindFilename_ResolvesToSuppliedFilename()
    {
        using var c = compiler.Compile("1 + 2"u8, filename: "/abs/path/to/script.rb");
        var irep = c.ToIrep();
        var filename = irep.DebugInfo?.FindFilename(0);
        Assert.That(filename, Is.EqualTo("/abs/path/to/script.rb"));
    }

    [Test]
    public void ChildIreps_AlsoCarryDebugInfo()
    {
        // Defining a method creates a child Irep for the method body.
        using var c = compiler.Compile("""
            def foo
              1 + 1
            end
            """u8, filename: "method.rb");
        var irep = c.ToIrep();
        Assert.That(irep.DebugInfo, Is.Not.Null);
        Assert.That(irep.Children.Length, Is.GreaterThan(0));

        var fooIrep = irep.Children[0];
        Assert.That(fooIrep.DebugInfo, Is.Not.Null, "Method body child irep should also have DebugInfo");
        // The method body's first instruction maps to line 2 (the `1 + 1` expression).
        bool sawLine2 = false;
        for (var pc = 0; pc < fooIrep.Sequence.Length; pc++)
        {
            if (fooIrep.DebugInfo!.FindLine(pc) == 2) { sawLine2 = true; break; }
        }
        Assert.That(sawLine2, Is.True, "Method body should map at least one pc to line 2");
    }
}
