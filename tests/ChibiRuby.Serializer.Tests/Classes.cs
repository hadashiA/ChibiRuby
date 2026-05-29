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
