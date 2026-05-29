using System.Linq;
using System.Text;
using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class LocalVariablesParseTest
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

    string[] LocalNames(Irep irep) => irep.LocalVariables
        .Where(s => s != Symbol.Empty)
        .Select(s => Encoding.UTF8.GetString(mrb.NameOf(s).AsSpan()))
        .ToArray();

    [Test]
    public void TopLevelLocals_AreNamedFromLVAR()
    {
        using var c = compiler.Compile("a = 1\nb = 2\nc = 3\n"u8);
        var irep = c.ToIrep();
        var names = LocalNames(irep);
        Assert.That(names, Does.Contain("a"));
        Assert.That(names, Does.Contain("b"));
        Assert.That(names, Does.Contain("c"));
    }

    [Test]
    public void MethodBodyLocals_AreNamedFromLVAR()
    {
        // Pre-fix bug: ReadLocalVariablesRecursive consumed nlocals entries (instead of
        // nlocals - 1 per mruby's on-disk format), which threw the binary cursor out of
        // alignment so every child irep's LocalVariables came back empty.
        using var c = compiler.Compile("""
            def f
              aaa = 7
              bbb = "hello"
              ccc = [1, 2, 3]
              aaa
            end
            f
            """u8);
        var irep = c.ToIrep();
        Assert.That(irep.Children.Length, Is.GreaterThan(0));
        var fooIrep = irep.Children[0];
        var names = LocalNames(fooIrep);
        Assert.That(names, Does.Contain("aaa"));
        Assert.That(names, Does.Contain("bbb"));
        Assert.That(names, Does.Contain("ccc"));
    }

    [Test]
    public void SlotZero_IsAlwaysSelf_NotALocalName()
    {
        // Per mruby's wire format, slot 0 is reserved for self and is *not* serialized
        // in the LVAR section. The first named local lands at LocalVariables[1].
        using var c = compiler.Compile("foo = 1"u8);
        var irep = c.ToIrep();
        Assert.That(irep.LocalVariables.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(irep.LocalVariables[0], Is.EqualTo(Symbol.Empty),
            "slot 0 should be the self placeholder (Symbol.Empty), not the first user local");
    }
}
