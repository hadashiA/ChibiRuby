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

    [Test]
    public void SyntaxError_ThrowsCompileExceptionWithDiagnostics()
    {
        // A bare endless range in a `when` clause is a syntax error (in CRuby too).
        // The compiler must surface the diagnostic, not hand back empty bytecode that
        // later blows up in the RiteParser with an opaque "Binary size is too short".
        const string source = """
                              case x
                              when 10..
                                puts "hi"
                              end
                              """;

        var ex = Assert.Throws<MRubyCompileException>(() => compiler.LoadSourceCode(source));
        Assert.That(ex!.Message, Does.Contain("when"));
        Assert.That(ex.Message, Does.Not.Contain("Binary size is too short"));
    }

    [Test]
    public void SyntaxError_CompilationResultReportsError()
    {
        using var compilation = compiler.Compile("when 10..\n  puts 1\n"u8);
        Assert.That(compilation.HasError, Is.True);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
        Assert.Throws<MRubyCompileException>(() => _ = compilation.AsBytecode().Length);
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
