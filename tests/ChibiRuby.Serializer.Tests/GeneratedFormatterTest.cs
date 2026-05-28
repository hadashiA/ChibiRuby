namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class GeneratedFormatterTest
{
    MRubyState state = default!;

    [SetUp]
    public void Setup()
    {
        state = MRubyState.Create();
    }

    [TearDown]
    public void TearDown()
    {
        state.Dispose();
    }

    [Test]
    public void Serialize()
    {
        var result = MRubyValueSerializer.Serialize(new NestedFieldObject
        {
            IntField = 123,
            Y = 456,
            ArrayField = ["a", "bbb", "ccc"],
            DictField = new Dictionary<string, Struct1>
            {
                ["AAA"] = new() { Id = 99999 }
            }
        }, state);

        var props = result.As<RHash>();
        Assert.That(props[state.Intern("int_field"u8)], Is.EqualTo(new MRubyValue(123)));
        Assert.That(props[state.Intern("alias_of_y"u8)], Is.EqualTo(new MRubyValue(456)));

        var arrayField = props[state.Intern("array_field"u8)].As<RArray>();
        Assert.That(arrayField.Length, Is.EqualTo(3));
        Assert.That(state.ValueEquals(arrayField[0], state.NewString("a"u8)), Is.True);
        Assert.That(state.ValueEquals(arrayField[1], state.NewString("bbb"u8)), Is.True);
        Assert.That(state.ValueEquals(arrayField[2], state.NewString("ccc"u8)), Is.True);

        var dictField = props[state.Intern("dict_field"u8)].As<RHash>();
        Assert.That(dictField.Length, Is.EqualTo(1));

        var nestedProps = dictField[state.NewString("AAA"u8)].As<RHash>();
        Assert.That(nestedProps[state.Intern("id"u8)],  Is.EqualTo(new MRubyValue(99999)));
    }

    [Test]
    public void Deserialize()
    {
        var props = state.NewHash();

        props.Add(state.Intern("int_field"u8), new MRubyValue(12345));

        var array = state.NewArray();
        array.Push(state.NewString("aaa"u8));
        array.Push(state.NewString("bbb"u8));
        array.Push(state.NewString("ccc"u8));

        props.Add(state.Intern("array_field"u8), array);

        var hash = state.NewHash();

        var nestedProps = state.NewHash();
        nestedProps.Add(state.Intern("id"u8), 99999);

        hash.Add(state.Intern("Hoge"u8), nestedProps);

        props.Add(state.Intern("dict_field"u8), hash);

        var result = MRubyValueSerializer.Deserialize<NestedFieldObject>(props, state)!;

        Assert.That(result.IntField, Is.EqualTo(12345));
        Assert.That(result.ArrayField.Length, Is.EqualTo(3));
        Assert.That(result.ArrayField[0], Is.EqualTo("aaa"));
        Assert.That(result.ArrayField[1], Is.EqualTo("bbb"));
        Assert.That(result.ArrayField[2], Is.EqualTo("ccc"));
        Assert.That(result.DictField.Count, Is.EqualTo(1));
        Assert.That(result.DictField["Hoge"].Id, Is.EqualTo(99999));
    }

    [Test]
    public void DeserializeWithCtor()
    {
        var props = state.NewHash();
        props.Add(state.Intern("x"u8), 123);
        props.Add(state.Intern("y"u8), 456);
        props.Add(state.Intern("hoge"u8), state.NewString("hello hello"u8));

        var result = MRubyValueSerializer.Deserialize<MRubyConstructorClass>(props, state)!;
        Assert.That(result.X, Is.EqualTo(123));
        Assert.That(result.Y, Is.EqualTo(456));
        Assert.That(result.Hoge, Is.EqualTo("hello hello"));
    }
}
