using System;
using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ChibiRuby;
using ChibiRuby.Benchmark;

// Profiling-friendly mode: warm up the VM, then run the measured optcarrot
// workload many times in a tight loop so a sampling profiler (dotnet-trace)
// captures mostly steady-state execution rather than startup / JIT / warmup.
//
//   --profile-optcarrot [frames] [warmupRuns] [iterations]
//
// Recommended capture (run against the built dll so dotnet-trace traces the
// app process directly, not the `dotnet run` launcher):
//
//   dotnet build -c Release sandbox/ChibiRuby.Benchmark
//   dotnet-trace collect --format speedscope \
//     -- dotnet sandbox/ChibiRuby.Benchmark/bin/Release/net10.0/ChibiRuby.Benchmark.dll \
//        --profile-optcarrot 180 3 30
//
// Open the resulting .speedscope.json at https://www.speedscope.app/ for a flamegraph.
// if (args is ["--profile-optcarrot", ..])
// {
//     var frames = args.Length >= 2 && int.TryParse(args[1], out var parsedFrames)
//         ? parsedFrames
//         : 180;
//     var warmupRuns = args.Length >= 3 && int.TryParse(args[2], out var parsedWarmupRuns)
//         ? parsedWarmupRuns
//         : 3;
//     var iterations = args.Length >= 4 && int.TryParse(args[3], out var parsedIterations)
//         ? parsedIterations
//         : 30;
//
//     using var loader = new RubyScriptLoader();
//     loader.PreloadOptcarrotBenchmark(frames, printResult: false);
//
//     Console.WriteLine($"[profile] warming up: {warmupRuns} run(s) x {frames} frames");
//     for (var i = 0; i < warmupRuns; i++)
//     {
//         loader.RunChibiRuby();
//     }
//
//     Console.WriteLine($"[profile] measuring: {iterations} run(s) x {frames} frames");
//     loader.ResetDispatchProfile();
//     var gc0 = GC.CollectionCount(0);
//     var gc1 = GC.CollectionCount(1);
//     var gc2 = GC.CollectionCount(2);
//     var alloc0 = GC.GetTotalAllocatedBytes(precise: true);
//     var pause0 = GC.GetTotalPauseDuration();
//     var sw = Stopwatch.StartNew();
//     for (var i = 0; i < iterations; i++)
//     {
//         loader.RunChibiRuby();
//     }
//     sw.Stop();
//     var allocated = GC.GetTotalAllocatedBytes(precise: true) - alloc0;
//     var pause = GC.GetTotalPauseDuration() - pause0;
//
//     var totalFrames = (long)frames * iterations;
//     var fps = totalFrames / sw.Elapsed.TotalSeconds;
//     Console.WriteLine(
//         $"[profile] done: {totalFrames} frames in {sw.Elapsed.TotalSeconds:F3}s => {fps:F2} fps " +
//         $"({sw.Elapsed.TotalMilliseconds / iterations:F2} ms/run)");
//     Console.WriteLine(
//         $"[profile] GC: gen0={GC.CollectionCount(0) - gc0} gen1={GC.CollectionCount(1) - gc1} " +
//         $"gen2={GC.CollectionCount(2) - gc2}  pause={pause.TotalMilliseconds:F1}ms " +
//         $"({pause.TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100:F1}% of wall)");
//     Console.WriteLine(
//         $"[profile] alloc: {allocated / 1024.0 / 1024.0:F1} MB total, " +
//         $"{allocated / (double)totalFrames / 1024.0:F1} KB/frame");
//     Console.Write(loader.DumpDispatchProfile());
//     return;
// }

// Quick wall-clock runner for a single ruby script (no BenchmarkDotNet):
//   --quick-script <file.rb> [iterations] [warmups]
// Prints per-iteration milliseconds and the median.
if (args is ["--quick-script", var scriptFile, ..])
{
    var iterations = args.Length >= 3 && int.TryParse(args[2], out var it) ? it : 10;
    var warmups = args.Length >= 4 && int.TryParse(args[3], out var w) ? w : 3;

    using var loader = new RubyScriptLoader();
    loader.PreloadScriptFromFile(scriptFile);
    for (var i = 0; i < warmups; i++)
    {
        loader.RunChibiRuby();
    }
    var times = new double[iterations];
    MRubyValue lastResult = default;
    for (var i = 0; i < iterations; i++)
    {
        var sw = Stopwatch.StartNew();
        lastResult = loader.RunChibiRuby();
        sw.Stop();
        times[i] = sw.Elapsed.TotalMilliseconds;
    }
    Array.Sort(times);
    var median = times[iterations / 2];
    Console.WriteLine($"result: {lastResult}");
    Console.WriteLine($"median: {median:F3} ms  (min {times[0]:F3} / max {times[^1]:F3}, n={iterations})");
    return;
}

if (args is ["--quick-optcarrot", ..])
{
    var frames = args.Length >= 2 && int.TryParse(args[1], out var parsedFrames)
        ? parsedFrames
        : 180;
    var warmupRuns = args.Length >= 3 && int.TryParse(args[2], out var parsedWarmupRuns)
        ? parsedWarmupRuns
        : 0;
    using var loader = new RubyScriptLoader();
    loader.PreloadOptcarrotBenchmark(frames, printResult: warmupRuns == 0);
    for (var i = 0; i < warmupRuns; i++)
    {
        loader.RunChibiRuby();
    }
    if (warmupRuns > 0)
    {
        loader.PreloadOptcarrotRun(frames);
    }
    loader.RunChibiRuby();
    return;
}

if (args is ["--quick-optcarrot-mruby", ..])
{
    var frames = args.Length >= 2 && int.TryParse(args[1], out var parsedFrames)
        ? parsedFrames
        : 180;
    new OptcarrotMrubyOriginalRunner(frames, printResult: true).Run();
    return;
}

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

[Config(typeof(BenchmarkConfig))]
public class NumericOperationBenchmark() : MRubyBenchmarkBase("bm_numeric_op.rb");

[Config(typeof(BenchmarkConfig))]
public class FibBenchmark() : MRubyBenchmarkBase("bm_fib.rb");

[Config(typeof(BenchmarkConfig))]
public class MandelbrotBenchmark() : MRubyBenchmarkBase("bm_so_mandelbrot.rb");

[Config(typeof(BenchmarkConfig))]
public class AoRenderBenchmark() : MRubyBenchmarkBase("bm_ao_render.rb");

[Config(typeof(BenchmarkConfig))]
public class OptcarrotBenchmark
{
    readonly RubyScriptLoader scriptLoader = new();
    readonly OptcarrotMrubyOriginalRunner mrubyOriginalRunner = new();

    [GlobalSetup]
    public void LoadScript()
    {
        scriptLoader.PreloadOptcarrotBenchmark(printResult: false);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        scriptLoader.Dispose();
    }

    [Benchmark]
    public void ChibiRuby()
    {
        scriptLoader.RunChibiRuby();
    }

    [Benchmark]
    public void MRubyOriginal()
    {
        mrubyOriginalRunner.Run();
    }
}
