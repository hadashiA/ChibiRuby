namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class CompositeResolverTest
{
    record A(int X);

    class CustomAFormatter : IMRubyValueFormatter<A>
    {
        public static readonly CustomAFormatter Instance = new();

        public MRubyValue Serialize(A value, MRubyState state, MRubyValueSerializerOptions options)
        {
            return value.X;
        }

        public A Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Integer, state: state);
            return new A((int)value.IntegerValue);
        }
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
    public void CreateWithCustomFormatter()
    {
        var options = MRubyValueSerializerOptions.Default
            .WithResolver(CompositeResolver.Create(
                [
                    CustomAFormatter.Instance
                ],
                [
                    StandardResolver.Instance
                ]));
        var value1 = MRubyValueSerializer.Serialize(new A(12345), mrb, options);
        Assert.That(value1.IsInteger, Is.True);
        Assert.That(value1.IntegerValue, Is.EqualTo(12345));

        var value2 = MRubyValueSerializer.Serialize(new List<int> { 134 }, mrb, options);
        Assert.That(value2.IsObject, Is.True);
        Assert.That(value2.As<RArray>()[0].IntegerValue, Is.EqualTo(134));
    }
}