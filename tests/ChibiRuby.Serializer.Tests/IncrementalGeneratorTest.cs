using System.Collections.Immutable;
using ChibiRuby.Serializer.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ChibiRuby.Serializer.Tests;

[TestFixture]
public class IncrementalGeneratorTest
{
    // A `[MRubyObject]` target plus an unrelated method, all in one syntax tree.
    const string BaseSource = """
using ChibiRuby.Serializer;

namespace TestApp;

[MRubyObject]
public partial class Person
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

public static class Unrelated
{
    public static int Compute() => 1;
}
""";

    // Identical `[MRubyObject]` target; only the unrelated method body changed.
    const string ModifiedUnrelatedSource = """
using ChibiRuby.Serializer;

namespace TestApp;

[MRubyObject]
public partial class Person
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

public static class Unrelated
{
    public static int Compute() => 2 + 3 + 4;
}
""";

    static CSharpCompilation CreateCompilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append<MetadataReference>(MetadataReference.CreateFromFile(typeof(MRubyObjectAttribute).Assembly.Location))
            .Append<MetadataReference>(MetadataReference.CreateFromFile(typeof(MRubyState).Assembly.Location));

        return CSharpCompilation.Create(
            "IncrementalTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Test]
    public void Regeneration_IsSkipped_WhenUnrelatedCodeChanges()
    {
        var generator = new ChibiRubySerializerSourceGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        // First run establishes the cache.
        var compilation1 = CreateCompilation(BaseSource);
        GeneratorDriver runDriver = driver.RunGenerators(compilation1);

        var firstResult = runDriver.GetRunResult().Results.Single();
        Assert.That(firstResult.TrackedSteps["MRubyObjectModels"]
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason), Is.All.EqualTo(IncrementalStepRunReason.New),
            "first run should produce a fresh model");

        // Second run: only the unrelated method body changed. The `[MRubyObject]` model is
        // value-equal, so the transform output is Unchanged and the source output is skipped.
        var compilation2 = CreateCompilation(ModifiedUnrelatedSource);
        runDriver = runDriver.RunGenerators(compilation2);

        var secondResult = runDriver.GetRunResult().Results.Single();

        var modelReasons = secondResult.TrackedSteps["MRubyObjectModels"]
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToArray();
        Assert.That(modelReasons, Is.All.EqualTo(IncrementalStepRunReason.Unchanged),
            "the equatable model must compare equal so downstream steps can be skipped");

        // The actual code-generation output step must be skipped (Cached), not re-run.
        var outputReasons = secondResult.TrackedOutputSteps
            .SelectMany(kv => kv.Value)
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToArray();
        Assert.That(outputReasons, Is.Not.Empty);
        Assert.That(outputReasons, Is.All.EqualTo(IncrementalStepRunReason.Cached),
            "source output should be served from cache when the model is unchanged");
    }
}
