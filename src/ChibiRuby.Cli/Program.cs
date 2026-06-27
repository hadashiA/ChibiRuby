using System.Buffers;
using System.Text;
using ConsoleAppFramework;
using ChibiRuby;
using ChibiRuby.Compiler;

var app = ConsoleApp.Create();
app.Add<Commands>();
app.Run(args);

class Commands
{
    /// <summary>
    /// Compile Ruby source file to mruby bytecode
    /// </summary>
    /// <param name="inputFile">Input Ruby source file path</param>
    /// <param name="output">-o, Output file path (default: same directory as input with .mrb/.cs extension)</param>
    /// <param name="dump">Dump bytecode in human-readable format instead of compiling (outputs to stdout)</param>
    /// <param name="format">Output format: binary or csharp</param>
    /// <param name="csharpNamespace">C# namespace for generated code</param>
    /// <param name="csharpClassName">C# class name for generated code</param>
    [Command("compile")]
    public void Compile(
        [Argument] string inputFile,
        string? output = null,
        bool dump = false,
        OutputFormat format = OutputFormat.binary,
        string? csharpNamespace = null,
        string? csharpClassName = null)
    {
        var state = MRubyState.Create();
        var inputBytes = File.ReadAllBytes(inputFile);

        try
        {
            if (dump)
            {
                Irep irep;
                if (IsBytecode(inputFile, inputBytes))
                {
                    irep = state.ParseBytecode(inputBytes);
                }
                else
                {
                    var compiler = MRubyCompiler.Create(state);
                    using var compilation = compiler.Compile(inputBytes);
                    irep = state.ParseBytecode(compilation.AsBytecode());
                }

                var bufferWriter = new ArrayBufferWriter<byte>();
                DumpIrepRecursive(state, irep, bufferWriter);

                using var outputStream = output is null or "-"
                    ? Console.OpenStandardOutput()
                    : File.Create(output);
                outputStream.Write(bufferWriter.WrittenSpan);
            }
            else
            {
                var compiler = MRubyCompiler.Create(state);
                using var compilation = compiler.Compile(inputBytes);

                // Resolve the bytecode before opening the destination so a compile error
                // doesn't leave behind a truncated/empty output file.
                var bytecode = compilation.AsBytecode().ToArray();

                using var outputStream = output == "-"
                    ? Console.OpenStandardOutput()
                    : File.Create(output ?? GetDefaultOutputPath(inputFile, format));

                switch (format)
                {
                    case OutputFormat.binary:
                        outputStream.Write(bytecode);
                        break;
                    case OutputFormat.csharp:
                        WriteCSharpOutput(outputStream, bytecode, csharpNamespace, csharpClassName);
                        break;
                }
            }
        }
        catch (MRubyCompileException ex)
        {
            Console.Error.WriteLine($"{inputFile}: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static string GetDefaultOutputPath(string inputFile, OutputFormat format)
    {
        var extension = format switch
        {
            OutputFormat.csharp => ".cs",
            _ => ".mrb"
        };
        return Path.ChangeExtension(inputFile, extension);
    }

    static bool IsBytecode(string filePath, byte[] bytes)
    {
        return filePath.EndsWith(".mrb", StringComparison.OrdinalIgnoreCase) ||
               (bytes.Length >= 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'T' && bytes[3] == 'E');
    }

    static void DumpIrepRecursive(MRubyState state, Irep irep, ArrayBufferWriter<byte> writer)
    {
        state.CodeDump(irep, writer);
        foreach (var child in irep.Children)
        {
            DumpIrepRecursive(state, child, writer);
        }
    }

    static void WriteCSharpOutput(Stream outputStream, ReadOnlySpan<byte> bytecode, string? ns, string? className)
    {
        var sb = new StringBuilder();
        if (ns != null)
        {
            sb.AppendLine($"namespace {ns} {{");
        }
        sb.AppendLine($$"""
public static class {{className ?? "MRubyBytecodeEmbedded"}}
{
    public static readonly byte[] Bytes =
    [
""");
        var i = 0;
        const string indent = "        ";
        sb.Append(indent);
        foreach (var b in bytecode)
        {
            sb.Append($"0x{b:X2}, ");
            if (++i >= 16)
            {
                sb.AppendLine();
                sb.Append(indent);
                i = 0;
            }
        }
        sb.AppendLine("""

    ];
}
""");
        if (ns != null)
        {
            sb.AppendLine("}");
        }
        using var writer = new StreamWriter(outputStream);
        writer.Write(sb.ToString());
        writer.Flush();
    }

    /// <summary>
    /// mrb2cs: ahead-of-time compile Ruby (.rb) or bytecode (.mrb) to C# source. Accepts a single
    /// file or a glob (e.g. "lib/**/*.rb"; ** = recurse). One .cs is emitted per input, with the
    /// class named after the file, and the source subdirectory structure recreated under the output
    /// directory. A .rb input is compiled to bytecode first, then converted.
    /// </summary>
    /// <param name="input">Input .rb / .mrb file, or a glob like "lib/**/*.rb".</param>
    /// <param name="outputDir">-o, Output directory; source subdirectories are recreated under it (default: the glob's base directory).</param>
    /// <param name="csharpNamespace">C# namespace to wrap each generated class in (default: none).</param>
    [Command("mrb2cs")]
    public void Mrb2CsCommand(
        [Argument] string input,
        string? outputDir = null,
        string? csharpNamespace = null)
    {
        var (baseDir, files) = ExpandGlob(input);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"mrb2cs: no .rb/.mrb input matched '{input}'");
            Environment.Exit(1);
        }
        outputDir ??= baseDir;

        var totalMethods = 0;
        foreach (var file in files)
        {
            try
            {
                // Fresh state per file so one file's class definitions don't leak into another's.
                var state = MRubyState.Create();
                var inputBytes = File.ReadAllBytes(file);
                Irep irep;
                if (IsBytecode(file, inputBytes))
                {
                    irep = state.ParseBytecode(inputBytes);
                }
                else
                {
                    // .rb: compile to mruby bytecode first, then convert to C#.
                    var compiler = MRubyCompiler.Create(state);
                    using var compilation = compiler.Compile(inputBytes);
                    irep = state.ParseBytecode(compilation.AsBytecode().ToArray());
                }

                // True AOT: statically register the program's classes/methods WITHOUT running it.
                ChibiRuby.JetPack.Mrb2Cs.DefinitionLoader.Load(state, irep);
                var className = ToClassName(Path.GetFileNameWithoutExtension(file));
                var result = ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.Compile(state, irep, className, csharpNamespace);

                // Recreate the source's subdirectory (relative to the glob base) under outputDir.
                // Auto-named output uses the `.g.cs` (generated) extension.
                var relative = Path.GetRelativePath(baseDir, file);
                var outPath = Path.Combine(outputDir, Path.ChangeExtension(relative, ".g.cs"));
                var outFolder = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outFolder)) Directory.CreateDirectory(outFolder);
                File.WriteAllText(outPath, result.Source);
                totalMethods += result.Methods.Count;
                Console.Error.WriteLine($"mrb2cs: {file} -> {outPath}  (class {className}, {result.Methods.Count} methods)");
            }
            catch (MRubyCompileException ex)
            {
                Console.Error.WriteLine($"{file}: {ex.Message}");
                Environment.Exit(1);
            }
        }
        Console.Error.WriteLine($"mrb2cs: {files.Count} file(s), {totalMethods} methods total");
    }

    // Expand an input path/glob to a (baseDir, files) pair. baseDir is the leading wildcard-free
    // prefix; output paths are made relative to it so the source subdirectory layout is preserved.
    // Supports `*` (single segment) and `**` (recursive). Only .rb / .mrb files are returned.
    static (string baseDir, List<string> files) ExpandGlob(string pattern)
    {
        static bool IsSource(string f) =>
            f.EndsWith(".rb", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".mrb", StringComparison.OrdinalIgnoreCase);

        if (!pattern.Contains('*'))
        {
            var dir = Path.GetDirectoryName(pattern);
            return (string.IsNullOrEmpty(dir) ? "." : dir, File.Exists(pattern) ? new List<string> { pattern } : new());
        }

        var segments = pattern.Replace('\\', '/').Split('/');
        var baseParts = new List<string>();
        foreach (var seg in segments)
        {
            if (seg.Contains('*')) break;
            baseParts.Add(seg);
        }
        var baseDir = baseParts.Count > 0 ? string.Join("/", baseParts) : ".";
        if (!Directory.Exists(baseDir)) return (baseDir, new());

        var fileGlob = segments[^1].Contains('*') ? segments[^1] : "*";
        var option = pattern.Contains("**") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(baseDir, fileGlob, option)
            .Where(IsSource)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        return (baseDir, files);
    }

    // A valid C# identifier from a file name (non-identifier chars -> '_', digit-leading -> '_'-prefixed).
    static string ToClassName(string fileName)
    {
        var sb = new StringBuilder();
        foreach (var c in fileName)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        var name = sb.ToString();
        return name.Length == 0 || char.IsDigit(name[0]) ? "_" + name : name;
    }

}

enum OutputFormat
{
    binary,
    csharp,
}
