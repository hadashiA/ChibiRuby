using ChibiRuby.Serializer;

// Call-site-only root declaration: exercises the [assembly: MRubyFormattable] AOT registration path.
[assembly: MRubyFormattable(typeof(List<double>))]

namespace ChibiRuby.Serializer.Tests;

[MRubyObject]
partial class NestedFieldObject
{
    public int IntField { get; set; }
    public string[] ArrayField { get; set; } = [];
    public Dictionary<string, Struct1> DictField { get; set; } = new();

    [MRubyMember("alias_of_y")]
    public int Y { get; set; }
}

[MRubyObject]
[method: MRubyConstructor]
partial class MRubyConstructorClass(int x, int y, string hoge)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public string Hoge { get; } = hoge;
}

[MRubyObject]
partial struct Struct1
{
    public long Id { get; set; }
}

// Simulates a source-generated [MRubyObject] type after Unity 6000.5's linker has stripped the
// attribute instance (#199): the generated registration method exists, but the attribute does not.
class AttributeStrippedObject
{
    public int Value { get; set; }

    public static void __RegisterMRubyValueFormatter()
    {
        GeneratedResolver.Register(new AttributeStrippedObjectFormatter());
    }

    class AttributeStrippedObjectFormatter : IMRubyValueFormatter<AttributeStrippedObject?>
    {
        public MRubyValue Serialize(AttributeStrippedObject? value, MRubyState mrb, MRubyValueSerializerOptions options)
            => value is null ? default : new MRubyValue(value.Value);

        public AttributeStrippedObject? Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
            => new() { Value = checked((int)value.IntegerValue) };
    }
}

enum SampleKind
{
    None,
    FooBar,
}

// Exercises the eager member-formatter registrations (enum / nullable / collections / multi-dim
// array) that the generator emits for AOT builds.
[MRubyObject]
partial class AotMemberObject
{
    public SampleKind Kind { get; set; }
    public int? MaybeCount { get; set; }
    public List<int> Ids { get; set; } = [];
    public Dictionary<string, Struct1> DictField { get; set; } = new();
    public int[,] Grid { get; set; } = new int[0, 0];
}
