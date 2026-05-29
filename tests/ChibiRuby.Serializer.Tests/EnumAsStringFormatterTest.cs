namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class EnumAsStringFormatterTest
{
    enum TestEnum
    {
        Hoge,
        BarBuz,
        MotionType10,
        MotionType01,
    }

    MRubyState mrb = default!;

    [SetUp]
    public void SetUp()
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
        var result1 = MRubyValueSerializer.Serialize(TestEnum.Hoge, mrb);
        Assert.That(result1.IsSymbol, Is.True);
        Assert.That(result1.SymbolValue, Is.EqualTo(mrb.Intern("hoge"u8)));

        var result2 = MRubyValueSerializer.Serialize(TestEnum.BarBuz, mrb);
        Assert.That(result2.IsSymbol, Is.True);
        Assert.That(result2.SymbolValue, Is.EqualTo(mrb.Intern("bar_buz"u8)));

        var result3 = MRubyValueSerializer.Serialize(TestEnum.MotionType10, mrb);
        Assert.That(result3.IsSymbol, Is.True);
        Assert.That(result3.SymbolValue, Is.EqualTo(mrb.Intern("motion_type10"u8)));

        var result4 = MRubyValueSerializer.Serialize(TestEnum.MotionType01, mrb);
        Assert.That(result4.IsSymbol, Is.True);
        Assert.That(result4.SymbolValue, Is.EqualTo(mrb.Intern("motion_type01"u8)));
    }

    [Test]
    public void Deserialize()
    {
        var result1 = MRubyValueSerializer.Deserialize<TestEnum>(mrb.Intern("hoge"), mrb);
        Assert.That(result1, Is.EqualTo(TestEnum.Hoge));

        var result2 = MRubyValueSerializer.Deserialize<TestEnum>(mrb.Intern("bar_buz"), mrb);
        Assert.That(result2, Is.EqualTo(TestEnum.BarBuz));

        Assert.Throws<MRubySerializationException>(() => MRubyValueSerializer.Deserialize<TestEnum>(1, mrb));
    }
}