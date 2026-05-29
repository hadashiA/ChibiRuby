using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class RiteParserTest
{
    [Test]
    public void Parse()
    {
        using var mrb = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(mrb);
        using var compilation = compiler.Compile("a = 'abcdefg'"u8);
        var irep = compilation.ToIrep();
        Assert.That(irep.PoolValues.Count, Is.EqualTo(1));
    }
}
