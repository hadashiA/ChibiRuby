using System.Text;
using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class CompilerTest
{
    MRubyCompiler compiler;
    MRubyState mrb = default!;

    [SetUp]
    public void BeforeAll()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void AfterAll()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    [Test]
    public void EmptySourceCode()
    {
        Assert.DoesNotThrow(() => compiler.LoadSourceCode(""u8));
    }

    [Test]
    public void BomValidation()
    {
        var sourceCode = "123 + 456";

        var utf8 = Encode(sourceCode, new UTF8Encoding(false));
        var utf8WithBom = Encode(sourceCode, new UTF8Encoding(true));
        var utf16WithBom = Encode(sourceCode, Encoding.Unicode);
        var utf16BEWithBom = Encode(sourceCode, Encoding.BigEndianUnicode);
        var utf32WithBom = Encode(sourceCode, Encoding.UTF32);

        var result = compiler.LoadSourceCode(utf8);
        Assert.That(result.IntegerValue, Is.EqualTo(579));

        var resultWithBom = compiler.LoadSourceCode(utf8WithBom);
        Assert.That(resultWithBom.IntegerValue, Is.EqualTo(579));

        Assert.Throws<MRubyCompileException>(() => compiler.LoadSourceCode(utf16WithBom));
        Assert.Throws<MRubyCompileException>(() => compiler.LoadSourceCode(utf16BEWithBom));
        Assert.Throws<MRubyCompileException>(() => compiler.LoadSourceCode(utf32WithBom));
    }

    static byte[] Encode(string sourceCode, Encoding encoding)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, encoding, leaveOpen: true);
        writer.Write(sourceCode);
        writer.Flush();
        return ms.ToArray();
    }
}
