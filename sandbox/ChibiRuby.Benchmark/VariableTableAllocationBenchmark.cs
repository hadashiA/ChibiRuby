using BenchmarkDotNet.Attributes;

namespace ChibiRuby.Benchmark;

[Config(typeof(BenchmarkConfig))]
public class VariableTableAllocationBenchmark
{
    [Params(10_000)]
    public int ObjectCount { get; set; }

    [Benchmark(Baseline = true)]
    public long FourIvarsInline() => VariableTableAllocationWorkload.FourIvarsInline(ObjectCount);

    [Benchmark]
    public long FiveIvarsPromoted() => VariableTableAllocationWorkload.FiveIvarsPromoted(ObjectCount);
}

static class VariableTableAllocationWorkload
{
    static readonly Symbol X = new(10_001);
    static readonly Symbol Y = new(10_002);
    static readonly Symbol Z = new(10_003);
    static readonly Symbol W = new(10_004);
    static readonly Symbol Extra = new(10_005);

    public static long FourIvarsInline(int objectCount)
    {
        long sum = 0;
        for (var i = 0; i < objectCount; i++)
        {
            var table = new VariableTable();
            table.Set(X, new MRubyValue(i));
            table.Set(Y, new MRubyValue(i + 1));
            table.Set(Z, new MRubyValue(i + 2));
            table.Set(W, new MRubyValue(i + 3));
            sum += table.Get(W).IntegerValue;
        }
        return sum;
    }

    public static long FiveIvarsPromoted(int objectCount)
    {
        long sum = 0;
        for (var i = 0; i < objectCount; i++)
        {
            var table = new VariableTable();
            table.Set(X, new MRubyValue(i));
            table.Set(Y, new MRubyValue(i + 1));
            table.Set(Z, new MRubyValue(i + 2));
            table.Set(W, new MRubyValue(i + 3));
            table.Set(Extra, new MRubyValue(i + 4));
            sum += table.Get(Extra).IntegerValue;
        }
        return sum;
    }
}
