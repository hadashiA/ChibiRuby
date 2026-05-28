namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class MRubyValueToMRubyValueTest
{
    MRubyState mrb = default!;

    [SetUp]
    public void Setup()
    {
        mrb = MRubyState.Create();
    }

    [TearDown]
    public void TearDown()
    {
        mrb.Dispose();
    }

    [Test]
    public void Serialize()
    {
        var value = mrb.Intern("abcde");
        var result = MRubyValueSerializer.Serialize(value, mrb);
        Assert.That(result, Is.EqualTo(new MRubyValue(value)));
    }
}