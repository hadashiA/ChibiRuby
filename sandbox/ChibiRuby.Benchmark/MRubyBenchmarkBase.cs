using BenchmarkDotNet.Attributes;

namespace ChibiRuby.Benchmark;

[Config(typeof(BenchmarkConfig))]
public abstract class MRubyBenchmarkBase(string filename)
{
    readonly RubyScriptLoader scriptLoader = new();

    [GlobalSetup]
    public void LoadScript()
    {
        scriptLoader.PreloadScriptFromFile(filename);
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
    public unsafe void MRubyNative()
    {
        scriptLoader.RunMRubyNative();
    }
}
