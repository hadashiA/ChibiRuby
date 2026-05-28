namespace ChibiRuby.Tests;

[TestFixture]
public class SymbolTest
{
    [Test]
    public void InlinePack()
    {
        var symbolTable = new SymbolTable();

        var sym = symbolTable.Intern("call"u8);
        var name = symbolTable.NameOf(sym);

        Assert.That(name.SequenceEqual("call"u8), Is.True);
        // Assert.That(symbolTable.Intern("call"u8), Is.EqualTo(Names.Call));
    }
}
