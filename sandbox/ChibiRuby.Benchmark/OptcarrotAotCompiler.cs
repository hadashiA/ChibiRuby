using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiRuby;
using ChibiRuby.JetPack;
using ChibiRuby.JetPack.Mrb2Cs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ChibiRuby.Benchmark;

// Runtime host for mrb2cs: generate the C# for an irep tree (Mrb2CsCompiler.Compile), Roslyn-compile it
// into an assembly, and register each body by fingerprint so a (re)parse of the same bytecode
// binds it. The source generation itself lives in ChibiRuby.JetPack (Mrb2Cs); this is just the
// "load it into the running process" half (the build-time path would csc + reference instead).
static class OptcarrotAotCompiler
{
    public static int CompileAndRegister(MRubyState state, Irep root)
    {
        var result = Mrb2CsCompiler.Compile(state, root, "OptcarrotAotGenerated");
        if (result.Methods.Count == 0)
        {
            return 0;
        }

        if (Environment.GetEnvironmentVariable("AOT_DUMP") is { Length: > 0 } dumpPath)
        {
            File.WriteAllText(dumpPath, result.Source);
        }

        var asm = Compile(result.Source);
        var type = asm.GetType("OptcarrotAotGenerated")!;
        foreach (var (name, fingerprint) in result.Methods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
            var body = (CompiledRubyMethodBody)method.CreateDelegate(typeof(CompiledRubyMethodBody));
            state.RegisterCompiledMethod(fingerprint, body);
        }

        return result.Methods.Count;
    }

    static Assembly Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(MRubyState).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "OptcarrotAotGenerated",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
        {
            File.WriteAllText("/tmp/aot_fail.cs", source);
            var errors = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(20)
                .Select(d => d.ToString()));
            throw new InvalidOperationException("mrb2cs generated code failed to compile:\n" + errors);
        }

        ms.Position = 0;
        return Assembly.Load(ms.ToArray());
    }
}
