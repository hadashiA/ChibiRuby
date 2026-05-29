namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class PrimitiveObjectFormatterTest
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
    public void Deserialize()
    {
        var array = mrb.NewArray();
        var hash0 = mrb.NewHash();
        hash0.Add(mrb.Intern("hoge"), 123);
        array.Push(hash0);

        var result = MRubyValueSerializer.Deserialize<dynamic>(array, mrb);
        Assert.That((object[])result!, Has.Length.EqualTo(1));

        var item = (Dictionary<object, object>)((object[])result!)[0];
        Assert.That(item["hoge"], Is.EqualTo(123));
    }
}
