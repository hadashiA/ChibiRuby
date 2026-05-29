namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class BuiltinClassFormatterTest
{
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
    public void Serialize_Guid()
    {
        var guid = Guid.NewGuid();
        var result = MRubyValueSerializer.Serialize(guid, mrb);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String));
        Assert.That(result.As<RString>().ToString(), Is.EqualTo(guid.ToString()));
    }

    [Test]
    public void Deserialize_Guid()
    {
        var guid = Guid.NewGuid();
        var mrbValue = mrb.NewString(guid.ToString());
        var result = MRubyValueSerializer.Deserialize<Guid>(mrbValue, mrb);
        Assert.That(result, Is.EqualTo(guid));
    }

    [Test]
    public void Serialize_Uri()
    {
        var uri = new Uri("https://www.example.com/hoge/fuga?k1=v1");
        var result = MRubyValueSerializer.Serialize(uri, mrb);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String));
        Assert.That(result.As<RString>().ToString(), Is.EqualTo(uri.ToString()));
    }

    [Test]
    public void Deserialize_Uri()
    {
        var uri = new Uri("https://www.example.com/hoge/fuga?k1=v1");
        var mrbValue = mrb.NewString(uri.ToString());
        var result = MRubyValueSerializer.Deserialize<Uri>(mrbValue, mrb);
        Assert.That(result, Is.EqualTo(uri));
    }

    [Test]
    public void Serialize_Version()
    {
        var version = new Version(1, 2, 3, 4);
        var result = MRubyValueSerializer.Serialize(version, mrb);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String));
        Assert.That(result.As<RString>().ToString(), Is.EqualTo(version.ToString()));
    }

    [Test]
    public void Deserialize_Version()
    {
        var version = new Version(1, 2, 3, 4);
        var mrbValue = mrb.NewString(version.ToString());
        var result = MRubyValueSerializer.Deserialize<Version>(mrbValue, mrb);
        Assert.That(result, Is.EqualTo(version));
    }

    [Test]
    public void Serialize_DateTime()
    {
        var dateTime = new DateTime(2025, 5, 15, 10, 30, 45, DateTimeKind.Utc);
        var result = MRubyValueSerializer.Serialize(dateTime, mrb);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.CSharpData));
    }

    [Test]
    public void Deserialize_DateTime()
    {
        var dateTime = new DateTime(2025, 5, 15, 10, 30, 45, DateTimeKind.Utc);
        var mrbValue = MRubyValueSerializer.Serialize(dateTime, mrb);
        var result = MRubyValueSerializer.Deserialize<DateTime>(mrbValue, mrb);
        Assert.That(result, Is.EqualTo(dateTime));
    }

    [Test]
    public void Serialize_DateTimeOffset()
    {
        var dateTimeOffset = new DateTimeOffset(2025, 5, 15, 10, 30, 45, TimeSpan.FromHours(9));
        var result = MRubyValueSerializer.Serialize(dateTimeOffset, mrb);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.CSharpData));
    }

    [Test]
    public void Deserialize_DateTimeOffset()
    {
        var dateTimeOffset = new DateTimeOffset(2025, 5, 15, 10, 30, 45, TimeSpan.FromHours(9));
        var mrbValue = MRubyValueSerializer.Serialize(dateTimeOffset, mrb);
        var result = MRubyValueSerializer.Deserialize<DateTimeOffset>(mrbValue, mrb);
        Assert.That(result.DateTime, Is.EqualTo(dateTimeOffset.DateTime));
    }

    [Test]
    public void Serialize_TimeSpan()
    {
        var timeSpan = new TimeSpan(1, 2, 3, 4, 5);
        var result = MRubyValueSerializer.Serialize(timeSpan, mrb);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String));
        Assert.That(result.As<RString>().ToString(), Is.EqualTo(timeSpan.ToString()));
    }

    [Test]
    public void Deserialize_TimeSpan()
    {
        var timeSpan = new TimeSpan(1, 2, 3, 4, 5);
        var mrbValue = mrb.NewString(timeSpan.ToString());
        var result = MRubyValueSerializer.Deserialize<TimeSpan>(mrbValue, mrb);
        Assert.That(result, Is.EqualTo(timeSpan));
    }
}