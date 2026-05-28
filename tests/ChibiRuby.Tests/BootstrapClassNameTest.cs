using System.Text;
using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

/// <summary>
/// Pins down the fix for the bootstrap class-name RStrings.
///
/// During <c>InitClass</c> the runtime calls <c>NewString("Object"u8)</c> etc. to set
/// up <see cref="MRubyState.ObjectClass"/> / BasicObject / Module / Class names, but
/// <see cref="MRubyState.StringClass"/> hasn't been defined yet at that point, so the
/// resulting RStrings end up with <c>Class == null</c>. They get returned to user
/// code by <c>Class#to_s</c> and any subsequent <c>Send</c> on them would NRE when
/// the dispatcher tried <c>ClassOf(self).TryFindMethod(...)</c>.
/// </summary>
[TestFixture]
public class BootstrapClassNameTest
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

    [Test]
    public void ObjectClassToS_ResultHasStringClass()
    {
        var result = compiler.LoadSourceCode("self.class.to_s"u8);
        Assert.That(result.Object, Is.InstanceOf<RString>());
        var str = result.As<RString>();
        Assert.That(str.Class, Is.Not.Null, "Class#to_s result must carry the StringClass reference");
        Assert.That(str.Class, Is.SameAs(mrb.StringClass));
    }

    [Test]
    public void SendOnClassToSResult_DoesNotNRE()
    {
        // The original symptom: NullReferenceException in MRubyState.Send when looking up
        // a method on the string returned by Class#to_s.
        var result = compiler.LoadSourceCode("self.class.to_s.length"u8);
        Assert.That(result.IntegerValue, Is.EqualTo(6)); // "Object".length
    }

    [Test]
    public void InspectStringReturnedByClassToS()
    {
        var result = compiler.LoadSourceCode("self.class.to_s"u8);
        var inspected = mrb.Inspect(result);
        var text = Encoding.UTF8.GetString(inspected.AsSpan());
        Assert.That(text, Is.EqualTo("\"Object\""));
    }
}
