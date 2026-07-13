using System;
using System.Diagnostics;

namespace ChibiRuby.Benchmark;

static class VariableTableAllocationCounter
{
    public static void Run(int objectCount)
    {
        objectCount = objectCount <= 0 ? 100_000 : objectCount;

        VariableTableAllocationWorkload.FourIvarsInline(1_000);
        VariableTableAllocationWorkload.FiveIvarsPromoted(1_000);

        var inline = Measure(() => VariableTableAllocationWorkload.FourIvarsInline(objectCount));
        var promoted = Measure(() => VariableTableAllocationWorkload.FiveIvarsPromoted(objectCount));

        Console.WriteLine($"VariableTable allocation counter, objects={objectCount}");
        Print("FourIvarsInline", inline, objectCount);
        Print("FiveIvarsPromoted", promoted, objectCount);
        Console.WriteLine(
            $"Delta: {promoted.AllocatedBytes - inline.AllocatedBytes:N0} B total, " +
            $"{(double)(promoted.AllocatedBytes - inline.AllocatedBytes) / objectCount:N2} B/object");
    }

    static (long Result, long AllocatedBytes, long ElapsedTicks) Measure(Func<long> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        var result = action();
        var elapsedTicks = Stopwatch.GetTimestamp() - timestamp;
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);
        return (result, allocatedBytes, elapsedTicks);
    }

    static void Print(
        string name,
        (long Result, long AllocatedBytes, long ElapsedTicks) measurement,
        int objectCount)
    {
        var elapsed = TimeSpan.FromSeconds((double)measurement.ElapsedTicks / Stopwatch.Frequency);
        Console.WriteLine(
            $"{name}: allocated={measurement.AllocatedBytes:N0} B " +
            $"({(double)measurement.AllocatedBytes / objectCount:N2} B/object), " +
            $"elapsed={elapsed.TotalMilliseconds:N2} ms, checksum={measurement.Result}");
    }
}
