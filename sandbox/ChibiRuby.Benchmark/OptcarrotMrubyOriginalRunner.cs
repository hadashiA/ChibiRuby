using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace ChibiRuby.Benchmark;

sealed class OptcarrotMrubyOriginalRunner(int frames = 180, bool printResult = false)
{
    const string MrubyPathEnvironmentVariable = "CHIBIRUBY_BENCH_MRUBY";

    public void Run()
    {
        var mrubyPath = ResolveMrubyPath();
        if (!File.Exists(mrubyPath))
        {
            throw new InvalidOperationException(
                "mruby original executable was not found. " +
                $"Set {MrubyPathEnvironmentVariable}=/path/to/mruby, or build sandbox/ChibiRuby.Benchmark/mruby with " +
                "MRUBY_CONFIG=../mruby_optcarrot_config.rb ./minirake.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = mrubyPath,
            WorkingDirectory = GetBenchmarkPath(Path.Join("ruby", "optcarrot")),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("tools/shim.rb");
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--quiet");
        if (printResult)
        {
            startInfo.ArgumentList.Add("--print-fps");
            startInfo.ArgumentList.Add("--print-video-checksum");
        }
        startInfo.ArgumentList.Add("--frames");
        startInfo.ArgumentList.Add(frames.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("examples/Lan_Master.nes");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start mruby original executable: {mrubyPath}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (printResult)
        {
            Console.Write(stdout);
            Console.Error.Write(stderr);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"mruby original optcarrot failed with exit code {process.ExitCode}.\n" +
                stdout +
                stderr);
        }
    }

    static string ResolveMrubyPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(MrubyPathEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : GetBenchmarkPath(Path.Join("mruby", "bin", "mruby"));
    }

    static string GetBenchmarkPath(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
    }
}
