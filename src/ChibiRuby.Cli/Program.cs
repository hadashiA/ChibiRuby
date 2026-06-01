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
}

enum OutputFormat
{
    binary,
    csharp,
}
