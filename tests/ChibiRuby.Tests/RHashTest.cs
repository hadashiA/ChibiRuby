namespace ChibiRuby.Tests;

public class RHashTest
{
    [Test]
    public void MRubyValueKey()
    {
        using var state = MRubyState.Create();
        var h = state.NewHash(2);

        var a1 = state.NewString("a"u8);
        var a2 = state.NewString("a"u8);

        h.Add(new MRubyValue(a1), MRubyValue.True);
        Assert.That(h.TryGetValue(new MRubyValue(a2), out _), Is.True);
    }
}
