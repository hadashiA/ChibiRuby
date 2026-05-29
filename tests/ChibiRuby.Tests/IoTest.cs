using System.Text;
using ChibiRuby.Compiler;

// Helper to write u8 byte arrays from interpolated raw strings.
// Interpolated + raw + u8 isn't legal C#, so we explicit-encode.

namespace ChibiRuby.Tests;

[TestFixture]
public class IoTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;
    string tempDir = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        mrb.DefineIO();
        compiler = MRubyCompiler.Create(mrb);
        tempDir = Path.Combine(Path.GetTempPath(), $"chibiruby-io-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Test]
    public void File_Write_Then_Read_AtRoot()
    {
        // Sync path: no scheduler, root fiber. File.write + File.read
        // round-trip a String literal.
        var path = Path.Combine(tempDir, "a.txt");
        var script = Encoding.UTF8.GetBytes($$"""
                       File.write("{{path}}", "hello world")
                       File.read("{{path}}")
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.As<RString>().AsSpan().SequenceEqual("hello world"u8), Is.True);
    }

    [Test]
    public async Task File_Write_Then_Read_InsideFiber_WithScheduler()
    {
        // Async path: with a scheduler installed, File.write / File.read
        // route through MRubyFiberScheduler.Await so the host thread isn't
        // blocked on stream I/O. Verifies end-to-end fiber + I/O.
        mrb.UseFiberScheduler();

        var path = Path.Combine(tempDir, "b.txt");
        var script = Encoding.UTF8.GetBytes($$"""
                       File.write("{{path}}", "fiber I/O")
                       File.read("{{path}}")
                       """);

        var fiber = compiler.LoadSourceCodeAsFiber(script);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync();

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.As<RString>().AsSpan().SequenceEqual("fiber I/O"u8), Is.True);
    }

    [Test]
    public void File_Open_NonBlock_ReturnsOpenIO()
    {
        var path = Path.Combine(tempDir, "d.txt");
        File.WriteAllText(path, "no-block");

        var script = Encoding.UTF8.GetBytes($$"""
                       File.open("{{path}}")
                       """);

        var value = compiler.LoadSourceCode(script);
        var rfile = value.As<RFile>();
        Assert.That(rfile.Closed, Is.False);
        rfile.Close();
    }

    [Test]
    public void IO_Closed_RaisesIOError()
    {
        var path = Path.Combine(tempDir, "e.txt");
        File.WriteAllText(path, "x");

        var script = Encoding.UTF8.GetBytes($$"""
                       f = File.open("{{path}}")
                       f.close
                       begin
                         f.read
                         :no_raise
                       rescue IOError
                         :raised
                       end
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }
}
