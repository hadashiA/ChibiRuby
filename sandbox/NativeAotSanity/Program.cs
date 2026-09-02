// NativeAOT sanity check for ChibiRuby.Serializer.
// Published with PublishAot=true and executed in CI: every check exercises a path that
// used to require runtime reflection or MakeGenericType and now must be satisfied by
// the source generator's eager registrations alone.
using ChibiRuby;
using ChibiRuby.Serializer;

// Call-site-only root type: appears in no [MRubyObject] member, registered via the
// assembly-level declaration below.
[assembly: MRubyFormattable(typeof(List<double>))]
[assembly: MRubyFormattable(typeof(int[]))]

var failures = 0;
var state = MRubyState.Create();

Check("[MRubyObject] round-trip (collection/enum/nullable members)", () =>
{
    var original = new Command
    {
        Kind = CommandKind.FooBar,
        Ids = [1, 2, 3],
        Names = ["a", "b"],
        Table = new Dictionary<string, Inner> { ["x"] = new() { Id = 42 } },
        MaybeCount = 7,
    };
    var value = MRubyValueSerializer.Serialize(original, state);
    var restored = MRubyValueSerializer.Deserialize<Command>(value, state)!;

    Require(restored.Kind == CommandKind.FooBar, "enum member");
    Require(restored.Ids.SequenceEqual([1, 2, 3]), "List<int> member");
    Require(restored.Names.SequenceEqual(["a", "b"]), "string[] member");
    Require(restored.Table["x"].Id == 42, "Dictionary<string, struct> member");
    Require(restored.MaybeCount == 7, "int? member");
});

Check("int[] at a call site", () =>
{
    var value = MRubyValueSerializer.Serialize(new[] { 10, 20 }, state);
    var restored = MRubyValueSerializer.Deserialize<int[]>(value, state)!;
    Require(restored.SequenceEqual([10, 20]), "int[] round-trip");
});

Check("[assembly: MRubyFormattable] root type List<double>", () =>
{
    var value = MRubyValueSerializer.Serialize(new List<double> { 1.5, 2.5 }, state);
    var restored = MRubyValueSerializer.Deserialize<List<double>>(value, state)!;
    Require(restored.SequenceEqual([1.5, 2.5]), "List<double> round-trip");
});

if (failures > 0)
{
    Console.WriteLine($"NativeAOT sanity: {failures} check(s) FAILED");
    return 1;
}
Console.WriteLine("NativeAOT sanity: all checks passed");
return 0;

void Check(string label, Action action)
{
    try
    {
        action();
        Console.WriteLine($"PASS {label}");
    }
    catch (Exception ex)
    {
        failures++;
        while (ex.InnerException is { } inner) ex = inner;
        Console.WriteLine($"FAIL {label}: {ex.GetType().Name}: {ex.Message}");
    }
}

static void Require(bool condition, string what)
{
    if (!condition) throw new Exception($"assertion failed: {what}");
}

enum CommandKind
{
    None,
    FooBar,
}

[MRubyObject]
partial class Command
{
    public CommandKind Kind { get; set; }
    public List<int> Ids { get; set; } = [];
    public string[] Names { get; set; } = [];
    public Dictionary<string, Inner> Table { get; set; } = new();
    public int? MaybeCount { get; set; }
}

[MRubyObject]
partial struct Inner
{
    public long Id { get; set; }
}
