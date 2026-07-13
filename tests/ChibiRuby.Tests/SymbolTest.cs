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

    [Test]
    public void InternFnv32CollidingNames()
    {
        // "costarring" and "liquid" are a known FNV-1a 32-bit collision pair;
        // they must intern to distinct symbols with round-tripping names.
        var symbolTable = new SymbolTable();

        var costarring = symbolTable.Intern("costarring"u8);
        var liquid = symbolTable.Intern("liquid"u8);

        Assert.That(costarring, Is.Not.EqualTo(liquid));
        Assert.That(symbolTable.NameOf(costarring).SequenceEqual("costarring"u8), Is.True);
        Assert.That(symbolTable.NameOf(liquid).SequenceEqual("liquid"u8), Is.True);

        // both remain findable after the collision chain forms
        Assert.That(symbolTable.Intern("costarring"u8), Is.EqualTo(costarring));
        Assert.That(symbolTable.Intern("liquid"u8), Is.EqualTo(liquid));

        // another documented pair, interned in reverse order
        var zinke = symbolTable.Intern("zinke"u8);
        var altarage = symbolTable.Intern("altarage"u8);
        Assert.That(altarage, Is.Not.EqualTo(zinke));
        Assert.That(symbolTable.NameOf(altarage).SequenceEqual("altarage"u8), Is.True);
    }

    [Test]
    public void PoolSymbolMemoAcrossStates()
    {
        // OP_SYMBOL (emitted by other mruby toolchains; the bundled compiler
        // prefers OP_LOADSYM) interns a pool string and memoizes the result on
        // the Irep. Symbols are per-state, so a second state executing the same
        // Irep instance must not observe the first state's memo.
        using var state1 = MRubyState.Create();
        using var state2 = MRubyState.Create();

        // hand-assembled: SYMBOL R1, Pool[0]; RETURN R1; STOP
        var irep = new Irep
        {
            RegisterVariableCount = 2,
            Sequence =
            [
                (byte)OpCode.Symbol, 1, 0,
                (byte)OpCode.Return, 1,
                (byte)OpCode.Stop,
            ],
            PoolValues = [new MRubyValue(state1.NewString("pool_symbol_memo_test"u8))],
        };

        var r1a = state1.Execute(irep);
        var r1b = state1.Execute(irep); // memoized in state1
        var r2a = state2.Execute(irep); // state2 must re-intern, not reuse state1's memo
        var r2b = state2.Execute(irep); // memoized in state2

        Assert.That(r1a.IsSymbol, Is.True);
        Assert.That(r1b, Is.EqualTo(r1a));
        Assert.That(r2b, Is.EqualTo(r2a));
        Assert.That(state1.NameOf(r1a.SymbolValue).ToString(), Is.EqualTo("pool_symbol_memo_test"));
        Assert.That(state2.NameOf(r2a.SymbolValue).ToString(), Is.EqualTo("pool_symbol_memo_test"));
    }
}
