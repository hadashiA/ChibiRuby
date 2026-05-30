using System.Text;
using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class JsonTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        mrb.DefineJson();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    [Test]
    public void Parse_Primitives()
    {
        var script = """
                     [JSON.parse("null"), JSON.parse("true"), JSON.parse("false"),
                      JSON.parse("42"), JSON.parse("3.14"), JSON.parse("\"hi\"")]
                     """;
        var result = Eval(script).As<RArray>();
        Assert.That(result[0].VType, Is.EqualTo(MRubyVType.Nil));
        Assert.That(result[1].VType, Is.EqualTo(MRubyVType.True));
        Assert.That(result[2].VType, Is.EqualTo(MRubyVType.False));
        Assert.That(result[3].IntegerValue, Is.EqualTo(42));
        Assert.That(result[4].FloatValue, Is.EqualTo(3.14).Within(1e-9));
        Assert.That(result[5].As<RString>().ToString(), Is.EqualTo("hi"));
    }

    [Test]
    public void Parse_Object_DefaultStringKeys()
    {
        var script = """JSON.parse('{"a":1,"b":"two","c":[1,2,3]}')""";
        var hash = Eval(script).As<RHash>();
        Assert.That(hash.TryGetValue(new MRubyValue(mrb.NewString("a")), out var a), Is.True);
        Assert.That(a.IntegerValue, Is.EqualTo(1));
        Assert.That(hash.TryGetValue(new MRubyValue(mrb.NewString("b")), out var b), Is.True);
        Assert.That(b.As<RString>().ToString(), Is.EqualTo("two"));
        Assert.That(hash.TryGetValue(new MRubyValue(mrb.NewString("c")), out var c), Is.True);
        Assert.That(c.As<RArray>().Length, Is.EqualTo(3));
    }

    [Test]
    public void Parse_Object_SymbolizeNames()
    {
        var script = """JSON.parse('{"a":1}', symbolize_names: true)""";
        var hash = Eval(script).As<RHash>();
        Assert.That(hash.TryGetValue(new MRubyValue(mrb.Intern("a"u8)), out var a), Is.True);
        Assert.That(a.IntegerValue, Is.EqualTo(1));
    }

    [Test]
    public void Parse_NestedStructures()
    {
        var script = """JSON.parse('{"users":[{"name":"alice"},{"name":"bob"}]}')""";
        var hash = Eval(script).As<RHash>();
        hash.TryGetValue(new MRubyValue(mrb.NewString("users")), out var users);
        var arr = users.As<RArray>();
        Assert.That(arr.Length, Is.EqualTo(2));
        var alice = arr[0].As<RHash>();
        alice.TryGetValue(new MRubyValue(mrb.NewString("name")), out var name);
        Assert.That(name.As<RString>().ToString(), Is.EqualTo("alice"));
    }

    [Test]
    public void Parse_LargeInteger_OverflowsToFloat()
    {
        // 2^63 + 1 — just past Int64.MaxValue.
        var script = """JSON.parse("9223372036854775808")""";
        var result = Eval(script);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.Float));
        Assert.That(result.FloatValue, Is.GreaterThan(9.2e18));
    }

    [Test]
    public void Parse_Malformed_RaisesParserError()
    {
        var script = """
                     begin
                       JSON.parse("{ broken")
                       :no_raise
                     rescue JSON::ParserError
                       :raised
                     end
                     """;
        Assert.That(Eval(script).SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Parse_TrailingGarbage_RaisesParserError()
    {
        var script = """
                     begin
                       JSON.parse('{"a":1}garbage')
                       :no_raise
                     rescue JSON::ParserError
                       :raised
                     end
                     """;
        Assert.That(Eval(script).SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Parse_DeepNesting_RaisesNestingError()
    {
        // 150 levels of '[' — well past the default 100.
        var deep = new string('[', 150) + new string(']', 150);
        var script = $$"""
                       begin
                         JSON.parse('{{deep}}')
                         :no_raise
                       rescue JSON::NestingError
                         :raised
                       end
                       """;
        Assert.That(Eval(script).SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Generate_Primitives()
    {
        Assert.That(EvalString("JSON.generate(nil)"), Is.EqualTo("null"));
        Assert.That(EvalString("JSON.generate(true)"), Is.EqualTo("true"));
        Assert.That(EvalString("JSON.generate(false)"), Is.EqualTo("false"));
        Assert.That(EvalString("JSON.generate(42)"), Is.EqualTo("42"));
        Assert.That(EvalString("JSON.generate(3.5)"), Is.EqualTo("3.5"));
        Assert.That(EvalString("JSON.generate(\"hi\")"), Is.EqualTo("\"hi\""));
    }

    [Test]
    public void Generate_Symbol_AsString()
    {
        Assert.That(EvalString("JSON.generate(:foo)"), Is.EqualTo("\"foo\""));
    }

    [Test]
    public void Generate_Array_AndHash()
    {
        Assert.That(EvalString("JSON.generate([1, 2, \"x\", nil])"),
            Is.EqualTo("[1,2,\"x\",null]"));
        Assert.That(EvalString("""JSON.generate({"a" => 1, "b" => [true, false]})"""),
            Is.EqualTo("""{"a":1,"b":[true,false]}"""));
    }

    [Test]
    public void Generate_SymbolKeys_StringifiedInOutput()
    {
        Assert.That(EvalString("JSON.generate({a: 1, b: 2})"),
            Is.EqualTo("""{"a":1,"b":2}"""));
    }

    [Test]
    public void Generate_NaN_DefaultRaisesGeneratorError()
    {
        var script = """
                     begin
                       JSON.generate(0.0/0.0)
                       :no_raise
                     rescue JSON::GeneratorError
                       :raised
                     end
                     """;
        Assert.That(Eval(script).SymbolValue, Is.EqualTo(mrb.Intern("raised"u8)));
    }

    [Test]
    public void Generate_NaN_WithAllowNan_EmitsToken()
    {
        // flori/json emits the non-standard literal NaN; we mirror that.
        Assert.That(EvalString("JSON.generate(0.0/0.0, allow_nan: true)"),
            Is.EqualTo("NaN"));
        Assert.That(EvalString("JSON.generate(1.0/0.0, allow_nan: true)"),
            Is.EqualTo("Infinity"));
        Assert.That(EvalString("JSON.generate(-1.0/0.0, allow_nan: true)"),
            Is.EqualTo("-Infinity"));
    }

    [Test]
    public void PrettyGenerate_HasIndentation()
    {
        var result = EvalString("""JSON.pretty_generate({"a" => 1, "b" => 2})""");
        Assert.That(result, Does.Contain("\n"));
        Assert.That(result, Does.Contain("  "));
        // Round-trip: pretty output parses back to the same Hash.
        var roundTrip = EvalString($$"""JSON.generate(JSON.parse({{"\"" + result.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\""}}))""");
        Assert.That(roundTrip, Is.EqualTo("""{"a":1,"b":2}"""));
    }

    [Test]
    public void RoundTrip_ComplexStructure()
    {
        var script = """
                     obj = {
                       "name" => "Alice",
                       "age" => 30,
                       "tags" => ["admin", "user"],
                       "meta" => { "active" => true, "score" => 9.5 },
                       "nothing" => nil
                     }
                     JSON.parse(JSON.generate(obj)) == obj
                     """;
        Assert.That(Eval(script).VType, Is.EqualTo(MRubyVType.True));
    }

    [Test]
    public void Dump_And_Load_AreAliases()
    {
        Assert.That(EvalString("JSON.dump([1, 2])"), Is.EqualTo("[1,2]"));
        var arr = Eval("JSON.load(\"[1,2]\")").As<RArray>();
        Assert.That(arr.Length, Is.EqualTo(2));
    }

    [Test]
    public void ToJson_OnEachBuiltinType()
    {
        Assert.That(EvalString("nil.to_json"), Is.EqualTo("null"));
        Assert.That(EvalString("true.to_json"), Is.EqualTo("true"));
        Assert.That(EvalString("false.to_json"), Is.EqualTo("false"));
        Assert.That(EvalString("42.to_json"), Is.EqualTo("42"));
        Assert.That(EvalString("3.5.to_json"), Is.EqualTo("3.5"));
        Assert.That(EvalString("\"hi\".to_json"), Is.EqualTo("\"hi\""));
        Assert.That(EvalString(":foo.to_json"), Is.EqualTo("\"foo\""));
        Assert.That(EvalString("[1, 2].to_json"), Is.EqualTo("[1,2]"));
        Assert.That(EvalString("{a: 1}.to_json"), Is.EqualTo("""{"a":1}"""));
    }

    [Test]
    public void Generate_UserClass_DispatchesToJson()
    {
        // User-defined class with a custom to_json — JSON.generate delegates
        // and splices the result in as raw JSON.
        var script = """
                     class Point
                       def initialize(x, y); @x, @y = x, y; end
                       def to_json
                         "{\"x\":#{@x},\"y\":#{@y}}"
                       end
                     end
                     JSON.generate([Point.new(1, 2), Point.new(3, 4)])
                     """;
        Assert.That(EvalString(script),
            Is.EqualTo("""[{"x":1,"y":2},{"x":3,"y":4}]"""));
    }

    // ── HTTP integration ────────────────────────────────────────────────

    [Test]
    public void Http_JsonOption_EncodesBody_AndSetsContentType()
    {
        mrb.DefineHttp();
        using var server = LocalServer.Start();
        string? receivedBody = null;
        string? receivedType = null;
        server.OnRequest = async ctx =>
        {
            receivedType = ctx.Request.ContentType;
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            receivedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            await server.WriteAsync(ctx, "ok");
        };

        var script = Encoding.UTF8.GetBytes($$"""
                       HTTP.post("{{server.BaseUrl}}/", json: { "x" => 1, "y" => [true, nil] }).status
                       """);

        var result = compiler.LoadSourceCode(script);
        Assert.That(result.IntegerValue, Is.EqualTo(200));
        Assert.That(receivedType, Does.StartWith("application/json"));
        Assert.That(receivedBody, Is.EqualTo("""{"x":1,"y":[true,null]}"""));
    }

    [Test]
    public void Http_RespJson_ParsesResponseBody()
    {
        mrb.DefineHttp();
        using var server = LocalServer.Start();
        server.OnRequest = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await server.WriteAsync(ctx, """{"hello":"world","n":42}""");
        };

        var script = Encoding.UTF8.GetBytes($$"""
                       resp = HTTP.get("{{server.BaseUrl}}/")
                       parsed = resp.json
                       [parsed["hello"], parsed["n"]]
                       """);

        var result = compiler.LoadSourceCode(script).As<RArray>();
        Assert.That(result[0].As<RString>().ToString(), Is.EqualTo("world"));
        Assert.That(result[1].IntegerValue, Is.EqualTo(42));
    }

    [Test]
    public void Http_JsonOption_RaisesWhenJsonNotLoaded()
    {
        // Fresh state with HTTP but no JSON — json: should be a hard error.
        using var state = MRubyState.Create();
        state.DefineHttp();
        using var localCompiler = MRubyCompiler.Create(state);

        using var server = LocalServer.Start();
        server.OnRequest = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await server.WriteAsync(ctx, "ok");
        };

        var script = Encoding.UTF8.GetBytes($$"""
                       begin
                         HTTP.post("{{server.BaseUrl}}/", json: { "a" => 1 })
                         :no_raise
                       rescue NotImplementedError
                         :raised
                       end
                       """);
        var result = localCompiler.LoadSourceCode(script);
        Assert.That(result.SymbolValue, Is.EqualTo(state.Intern("raised"u8)));
    }

    // ── helpers ─────────────────────────────────────────────────────────

    MRubyValue Eval(string ruby) =>
        compiler.LoadSourceCode(Encoding.UTF8.GetBytes(ruby));

    string EvalString(string ruby) =>
        Eval(ruby).As<RString>().ToString();
}
