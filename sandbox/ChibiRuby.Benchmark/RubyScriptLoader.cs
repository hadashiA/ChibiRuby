using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ChibiRuby.Compiler;
using ChibiRuby.JetPack;

namespace ChibiRuby.Benchmark;

unsafe class RubyScriptLoader : IDisposable
{
    const string OptcarrotPreludeFile = "chibiruby/optcarrot_prelude.rb";

    static readonly string[] OptcarrotDefinitionFiles =
    [
        "lib/optcarrot.rb",
        "lib/optcarrot/opt.rb",
        "lib/optcarrot/nes.rb",
        "lib/optcarrot/palette.rb",
        "lib/optcarrot/pad.rb",
        "lib/optcarrot/driver.rb",
        "lib/optcarrot/cpu.rb",
        "lib/optcarrot/apu.rb",
        "lib/optcarrot/ppu.rb",
        "lib/optcarrot/rom.rb",
        "lib/optcarrot/mapper/mmc1.rb",
        "lib/optcarrot/mapper/uxrom.rb",
        "lib/optcarrot/mapper/cnrom.rb",
        "lib/optcarrot/mapper/mmc3.rb",
        "lib/optcarrot/config.rb",
    ];

    readonly MRubyState mrubyCSState;
    readonly MrbStateNative* mrbStateNative;

    readonly MRubyCompiler mrubyCSCompiler;
    bool disposed;

    Irep? currentChibiRubyIrep;
    RProcHandle? currentMRubyNativeProc;

    public RubyScriptLoader()
    {
        mrubyCSState = MRubyState.Create();
        mrubyCSState.DefineIO();
        mrubyCSState.DefineRegexp();
        RegisterMathModule(mrubyCSState);
        mrubyCSCompiler = MRubyCompiler.Create(mrubyCSState);

        mrbStateNative = NativeMethods.MrbOpen();
    }

    public static void RunQuickScript(string scriptName, int iterations)
    {
        RunQuickScript(scriptName, iterations, aot: false);
    }

    public static void RunQuickScript(string scriptName, int iterations, bool aot)
    {
        if (iterations < 1)
        {
            iterations = 1;
        }

        using var loader = new RubyScriptLoader();
        if (aot)
        {
            var compiled = loader.PreloadScriptFromFileAot(scriptName);
            Console.Error.WriteLine($"AOT-compiled methods: {compiled}");
        }
        else
        {
            loader.PreloadScriptFromFile(scriptName);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var pauseBefore = GC.GetTotalPauseDuration();
        var stopwatch = Stopwatch.StartNew();
        var result = MRubyValue.Nil;
        for (var i = 0; i < iterations; i++)
        {
            result = loader.RunChibiRuby();
        }
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var pauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
        var wallMs = stopwatch.Elapsed.TotalMilliseconds;

        Console.WriteLine(
            $"ChibiRuby quick script={scriptName} aot={aot} iterations={iterations} elapsedMs={wallMs:F3} allocatedBytes={allocatedBytes} allocatedBytesPerIteration={(double)allocatedBytes / iterations:F0} result={result}");
        Console.WriteLine(
            $"[gc] server={System.Runtime.GCSettings.IsServerGC} gen0={GC.CollectionCount(0) - gc0} gen1={GC.CollectionCount(1) - gc1} gen2={GC.CollectionCount(2) - gc2} pauseMs={pauseMs:F1} pausePct={(wallMs > 0 ? pauseMs / wallMs * 100 : 0):F1}%");
    }

    // AOT variant of PreloadScriptFromFile: compile the script, execute it once so the
    // class hierarchy exists (lets the codegen resolve self-sends for devirtualize+inline),
    // then AOT-compile every statically-compilable method to C#, register the bodies by
    // fingerprint and bind them to the irep tree. Subsequent RunChibiRuby() calls hit the
    // compiled bodies. Returns the number of methods compiled.
    public int PreloadScriptFromFileAot(string fileName)
    {
        var source = ReadBytes(fileName);
        currentChibiRubyIrep = CompileChibiRubySource(Encoding.UTF8.GetString(source));
        mrubyCSState.Execute(currentChibiRubyIrep);
        var compiled = OptcarrotAotCompiler.CompileAndRegister(mrubyCSState, currentChibiRubyIrep);
        mrubyCSState.BindCompiledMethods(currentChibiRubyIrep);
        return compiled;
    }

    static void RegisterMathModule(MRubyState state)
    {
        state.DefineModule(state.Intern("Math"u8), mod =>
        {
            mod.DefineClassMethod(state.Intern("sqrt"u8), new MRubyMethod(
                (s, self) =>
                {
                    var value = s.GetArgumentAsFloatAt(0);
                    return System.Math.Sqrt(value);
                },
                (s, self, argument) => System.Math.Sqrt(s.AsFloat(argument)),
                (_, _, argument) => System.Math.Sqrt(argument)));

            mod.DefineClassMethod(state.Intern("cos"u8), new MRubyMethod(
                (s, self) =>
                {
                    var value = s.GetArgumentAsFloatAt(0);
                    return System.Math.Cos(value);
                },
                (s, self, argument) => System.Math.Cos(s.AsFloat(argument)),
                (_, _, argument) => System.Math.Cos(argument)));

            mod.DefineClassMethod(state.Intern("sin"u8), new MRubyMethod(
                (s, self) =>
                {
                    var value = s.GetArgumentAsFloatAt(0);
                    return System.Math.Sin(value);
                },
                (s, self, argument) => System.Math.Sin(s.AsFloat(argument)),
                (_, _, argument) => System.Math.Sin(argument)));
        });
    }

    public void PreloadScript(ReadOnlySpan<byte> source)
    {
        using var compilation = mrubyCSCompiler.Compile(source);
        currentChibiRubyIrep = compilation.ToIrep();

        currentMRubyNativeProc?.Dispose();

        RProcNative* procPtr = null;
        byte* errorMessageCStr = null;
        fixed (byte* sourcePtr = source)
        {
            var resultCode = NativeMethods.MrbcsCompileToProc(
                mrbStateNative,
                sourcePtr,
                source.Length,
                &procPtr,
                &errorMessageCStr);

            if (resultCode != 0)
            {
                if (errorMessageCStr != null)
                {
                    var errorMessage = Marshal.PtrToStringUTF8((IntPtr)errorMessageCStr)!;
                    throw new MRubyCompileException(errorMessage);
                }
            }
        }

        currentMRubyNativeProc = new RProcHandle(mrbStateNative, procPtr);
    }

    public void PreloadScriptFromFile(string fileName)
    {
        var source = ReadBytes(fileName);
        PreloadScript(source);
    }

    public void PreloadOptcarrotBenchmark(int frames = 180, bool printResult = true)
    {
        var definitions = CompileChibiRubySource(BuildOptcarrotDefinitionsSource());
        mrubyCSState.Execute(definitions);

        PreloadOptcarrotRun(frames, printResult);
    }

    public void PreloadOptcarrotRun(int frames = 180, bool printResult = true)
    {
        currentChibiRubyIrep = CompileChibiRubySource(BuildOptcarrotRunSource(frames, printResult));
    }

    // Like PreloadOptcarrotBenchmark but AOT-compiles every statically-compilable
    // optcarrot method to C# first, registering bodies by fingerprint and binding them
    // to the definitions tree before it executes. Returns the number of methods compiled.
    public int PreloadOptcarrotBenchmarkAot(int frames = 180, bool printResult = true)
    {
        var definitions = CompileChibiRubySource(BuildOptcarrotDefinitionsSource());
        // Define the classes first so the AOT compiler can resolve self-sends against
        // the real class hierarchy (for devirtualize+inline), then compile + bind.
        mrubyCSState.Execute(definitions);
        var compiled = OptcarrotAotCompiler.CompileAndRegister(mrubyCSState, definitions);
        mrubyCSState.BindCompiledMethods(definitions);

        PreloadOptcarrotRun(frames, printResult);
        return compiled;
    }

    public MRubyValue RunChibiRuby()
    {
        return mrubyCSState.Execute(currentChibiRubyIrep!);
    }

    // public void ResetDispatchProfile() => mrubyCSState.ResetDispatchProfile();
    //
    // public string DumpDispatchProfile(int topN = 25) => mrubyCSState.DumpDispatchProfile(topN);

    public MrbValueNative RunMRubyNative()
    {
        return NativeMethods.MrbLoadProc(mrbStateNative, currentMRubyNativeProc!.DangerousGetPtr());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        currentMRubyNativeProc?.Dispose();
        NativeMethods.MrbClose(mrbStateNative);
        disposed = true;
    }

    static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
        // return Path.Join(Assembly.GetEntryAssembly()!.Location, relativePath);
    }

    byte[] ReadBytes(string fileName)
    {
        var path = GetAbsolutePath(Path.Join("ruby", fileName));
        return File.ReadAllBytes(path);
    }

    Irep CompileChibiRubySource(string source)
    {
        using var compilation = mrubyCSCompiler.Compile(Encoding.UTF8.GetBytes(source));
        return compilation.ToIrep();
    }

    static string BuildOptcarrotDefinitionsSource()
    {
        var builder = new StringBuilder();
        AppendBenchmarkRubyFile(builder, OptcarrotPreludeFile);
        foreach (var file in OptcarrotDefinitionFiles)
        {
            AppendOptcarrotFile(builder, file);
        }
        AppendOptcarrotFixups(builder);
        return builder.ToString();
    }

    static void AppendOptcarrotFixups(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("# chibiruby optcarrot fixups");
        builder.AppendLine("module Optcarrot::Palette");
        builder.AppendLine("  module_function :nestopia_palette, :defacto_palette");
        builder.AppendLine("end");
        builder.AppendLine("module Optcarrot::Driver");
        builder.AppendLine("  module_function :load, :load_each");
        builder.AppendLine("end");
    }

    static void AppendOptcarrotFile(StringBuilder builder, string fileName)
    {
        var sourcePath = GetAbsolutePath(Path.Join("ruby", "optcarrot", fileName));
        var source = File.ReadAllText(sourcePath);
        source = StripRequireLines(source);
        source = TransformOptcarrotSource(source);

        builder.AppendLine();
        builder.AppendLine($"# {fileName}");
        builder.AppendLine(source);
    }

    static void AppendBenchmarkRubyFile(StringBuilder builder, string fileName)
    {
        var sourcePath = GetAbsolutePath(Path.Join("ruby", fileName));
        var source = File.ReadAllText(sourcePath);

        builder.AppendLine();
        builder.AppendLine($"# {fileName}");
        builder.AppendLine(source);
    }

    static string StripRequireLines(string source)
    {
        using var reader = new StringReader(source);
        var builder = new StringBuilder(source.Length);

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("require ", StringComparison.Ordinal) ||
                trimmed.StartsWith("require_relative ", StringComparison.Ordinal))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    static string TransformOptcarrotSource(string source)
    {
        source = source.Replace(
            "@apu = @cpu.apu = APU.new(@conf, @cpu, *@audio.spec)",
            "audio_rate, audio_bits = @audio.spec\n      @apu = APU.new(@conf, @cpu, audio_rate, audio_bits)\n      @cpu.apu = @apu",
            StringComparison.Ordinal);
        source = source.Replace(
            "@ppu = @cpu.ppu = PPU.new(@conf, @cpu, @video.palette)",
            "@ppu = PPU.new(@conf, @cpu, @video.palette)\n      @cpu.ppu = @ppu",
            StringComparison.Ordinal);
        source = source.Replace(
            "@palette = [*0..4096]",
            "@palette = (0..4096).map { |i| i }",
            StringComparison.Ordinal);
        source = source.Replace(
            "send(*DISPATCH[@opcode])",
            "dispatch = DISPATCH[@opcode]\n" +
            "          case dispatch.size\n" +
            "          when 1\n" +
            "            __send__(dispatch[0])\n" +
            "          when 2\n" +
            "            __send__(dispatch[0], dispatch[1])\n" +
            "          when 3\n" +
            "            __send__(dispatch[0], dispatch[1], dispatch[2])\n" +
            "          else\n" +
            "            __send__(dispatch[0], dispatch[1], dispatch[2], dispatch[3])\n" +
            "          end",
            StringComparison.Ordinal);
        source = source.Replace("@buffer << @mixer.sample", "@buffer << 0", StringComparison.Ordinal);

        source = source.Replace(".pack(\"C*\").sum", ".sum & 0xffff", StringComparison.Ordinal);
        source = source.Replace(".pack('C*').sum", ".sum & 0xffff", StringComparison.Ordinal);

        return source;
    }

    static string BuildOptcarrotRunSource(int frames, bool printResult)
    {
        var romPath = EscapeRubyString(GetAbsolutePath(Path.Join(
            "ruby",
            "optcarrot",
            "examples",
            "Lan_Master.nes")));
        var profilingOptions = printResult
            ? "print_fps: true, print_video_checksum: true, "
            : "";
        return
            "Optcarrot::NES.new({ " +
            "video: :none, audio: :none, input: :none, " +
            $"frames: {frames}, " +
            profilingOptions +
            $"romfile: \"{romPath}\" " +
            "}).run\n";
    }

    static string EscapeRubyString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
