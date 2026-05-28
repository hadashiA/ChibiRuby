using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ChibiRuby.Benchmark;

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

[Config(typeof(BenchmarkConfig))]
public class NumericOperationBenchmark() : MRubyBenchmarkBase("bm_numeric_op.rb");

[Config(typeof(BenchmarkConfig))]
public class FibBenchmark() : MRubyBenchmarkBase("bm_fib.rb");

[Config(typeof(BenchmarkConfig))]
public class MandelbrotBenchmark() : MRubyBenchmarkBase("bm_so_mandelbrot.rb");

[Config(typeof(BenchmarkConfig))]
public class AoRenderBenchmark() : MRubyBenchmarkBase("bm_ao_render.rb");
