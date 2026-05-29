using System;
using System.Collections.Generic;
using System.Text;
using ChibiRuby.Internals;

namespace ChibiRuby;

public struct BacktraceLocation
{
    public Symbol MethodId;
    public Irep? Irep;
    public int Index;
}

public class Backtrace
{
    public IReadOnlyList<BacktraceLocation> Entries => entries;
    readonly List<BacktraceLocation> entries;

    Backtrace(List<BacktraceLocation> entries)
    {
        this.entries = entries;
    }

    public RArray ToRArray(MRubyState state)
    {
        var array = state.NewArray(64);
        foreach (var entry in entries)
        {
            // Top-level / fiber-root frames have MethodId == default but still carry an
            // Irep and a useful pc. Show them as "<main>" so the user sees their script's
            // file:line in the trace.
            var methodName = entry.MethodId == default
                ? "<main>"u8
                : state.NameOf(entry.MethodId).AsSpan();

            RString line;
            if (entry.Irep?.DebugInfo is { } dbg &&
                dbg.TryFindPosition(entry.Index, out var file, out var lineNo))
            {
                line = state.NewString($"{file}:{lineNo}:in `{methodName}'");
            }
            else if (entry.Irep is not null)
            {
                line = state.NewString($"in `{methodName}' (no debug info, byte sequence: {entry.Index})");
            }
            else if (entry.MethodId != default)
            {
                // C# method frame (Proc=null). No source position.
                line = state.NewString($"in `{methodName}'");
            }
            else
            {
                continue; // truly empty frame; skip
            }
            array.Push(line);
        }
        return array;
    }

    public string ToString(MRubyState state)
    {
        var array = ToRArray(state);
        var result = "";
        foreach (var line in array.AsSpan())
        {
            result += Encoding.UTF8.GetString(line.As<RString>().AsSpan());
            result += Environment.NewLine;
        }
        return result;
    }

    internal static Backtrace Capture(MRubyContext context)
    {
        var entries = new List<BacktraceLocation>();

        for (var i = context.CallDepth; i >= 0; i--)
        {
            ref var callInfo = ref context.CallStack[i];

            var location = new BacktraceLocation
            {
                MethodId = callInfo.MethodId,
            };

            if (callInfo.Proc is { } proc)
            {
                location.Irep = proc.Irep;
                // Use the call frame's current pc rather than proc.ProgramCounter
                // (which is just the proc's starting pc, the same for every frame
                // and therefore useless for backtrace). For the innermost frame this is
                // where execution is right now; for ancestors it points just past the
                // Send opcode that pushed the child, i.e. the call site.
                location.Index = callInfo.ProgramCounter;
            }
            entries.Add(location);
        }
        return new Backtrace(entries);
    }
}