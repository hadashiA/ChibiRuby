using System.Text;
using ChibiRuby.Internals;

namespace ChibiRuby.Tests;

[TestFixture]
public class NamingRuleTest
{
    [Test]
    [TestCase("foo", ExpectedResult = true)]
    public bool IsSymbolName(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        return NamingRule.IsSymbolName(utf8);
    }

    [Test]
    [TestCase("", "\"\"")]
    [TestCase("abcde123", "\"abcde123\"")]
    [TestCase("\"", "\"\\\"\"")]
    [TestCase("\n", "\"\\n\"")]
    [TestCase("る", "\"る\"")]
    [TestCase("あい", "\"あい\"")]
    [TestCase("木の棒", "\"木の棒\"")]
    public void Escape(string input, string expected)
    {
        var src = Encoding.UTF8.GetBytes(input);
        var dst = new byte[Encoding.UTF8.GetMaxByteCount(expected.Length)];
        var result = NamingRule.TryEscape(src, true, dst, out var written);

        Assert.That(result, Is.True);
        Assert.That(written, Is.EqualTo(Encoding.UTF8.GetBytes(expected).Length));
        Assert.That(Encoding.UTF8.GetString(dst[..written]), Is.EqualTo(expected));
    }
}
