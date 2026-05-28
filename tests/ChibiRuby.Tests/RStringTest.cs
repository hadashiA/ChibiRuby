using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

public class RStringTest
{
    [Test]
    public void Equals()
    {
        var a1 = new RString("a"u8, null!);
        var a2 = new RString("a"u8, null!);
        Assert.That(a1 == a2, Is.True);
    }

    [Test]
    [TestCase("hello world", "HELLO WORLD")]
    [TestCase("abc-xyz", "ABC-XYZ")]
    [TestCase("123 abc", "123 ABC")]
    [TestCase("Hello_World", "HELLO_WORLD")]
    public void Upcase_PreservesNonLetterBytes(string input, string expected)
    {
        using var mrb = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(mrb);

        var result = compiler.LoadSourceCode($"'{input}'.upcase").As<RString>();
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase("HELLO WORLD", "hello world")]
    [TestCase("ABC-XYZ", "abc-xyz")]
    [TestCase("123 ABC", "123 abc")]
    [TestCase("Hello_World", "hello_world")]
    public void Downcase_PreservesNonLetterBytes(string input, string expected)
    {
        using var mrb = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(mrb);

        var result = compiler.LoadSourceCode($"'{input}'.downcase").As<RString>();
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase("a b-c_1", "A b-c_1")]
    [TestCase("hello_world", "Hello_world")]
    [TestCase("123abc", "123abc")]
    public void Capitalize_PreservesNonLetterBytes(string input, string expected)
    {
        using var mrb = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(mrb);

        var result = compiler.LoadSourceCode($"'{input}'.capitalize").As<RString>();
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }
}
