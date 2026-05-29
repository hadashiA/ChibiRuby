using System;
using System.Collections.Generic;
using ChibiRuby.Internals;

namespace ChibiRuby;

/// <summary>A Ruby Binding: self + locals + source position of a Ruby frame.</summary>
public sealed class RBinding : RObject
{
    public MRubyValue Receiver { get; }
    RProc? CallerProc { get; }
    int CallerProgramCounter { get; }
    internal Symbol CallerMethodId { get; }

    Symbol[] names;
    MRubyValue[] values;
    readonly int irepLocalCount;

    // (FrameDepth, Slot) per captured-local. Live mode only.
    readonly (int FrameDepth, int Slot)[] liveSources;

    MRubyContext? liveContext;
    int liveCallDepth;

    public bool IsLive => liveContext is not null;

    internal RBinding(
        RClass klass,
        MRubyValue receiver,
        RProc? callerProc,
        int callerProgramCounter,
        Symbol callerMethodId,
        MRubyContext liveContext,
        int liveCallDepth)
        : base(MRubyVType.Object, klass)
    {
        Receiver = receiver;
        CallerProc = callerProc;
        CallerProgramCounter = callerProgramCounter;
        CallerMethodId = callerMethodId;
        this.liveContext = liveContext;
        this.liveCallDepth = liveCallDepth;

        // Walk caller proc → Upper, innermost first, dedupe by name (inner wins).
        var nameList = new List<Symbol>();
        var sourceList = new List<(int, int)>();
        var seen = new HashSet<Symbol>();
        var currentProc = callerProc;
        var currentDepth = liveCallDepth;
        while (currentProc is not null && currentDepth >= 0)
        {
            var irep = currentProc.Irep;
            if (irep is { LocalVariables.Length: > 1 })
            {
                var n = irep.LocalVariables.Length - 1;
                for (var slot = 0; slot < n; slot++)
                {
                    var sym = irep.LocalVariables[slot + 1];
                    if (sym == default) continue;
                    if (!seen.Add(sym)) continue;
                    nameList.Add(sym);
                    sourceList.Add((currentDepth, slot));
                }
            }
            var upper = currentProc.Upper;
            if (upper is null) break;
            var foundDepth = -1;
            for (var d = currentDepth - 1; d >= 0; d--)
            {
                if (ReferenceEquals(liveContext.CallStack[d].Proc, upper))
                {
                    foundDepth = d;
                    break;
                }
            }
            if (foundDepth < 0) break;
            currentDepth = foundDepth;
            currentProc = upper;
        }

        names = nameList.ToArray();
        values = new MRubyValue[names.Length];
        liveSources = sourceList.ToArray();
        irepLocalCount = names.Length;
    }

    internal RBinding(
        RClass klass,
        MRubyValue receiver,
        RProc? callerProc,
        int callerProgramCounter,
        Symbol callerMethodId,
        Symbol[] names,
        MRubyValue[] values)
        : base(MRubyVType.Object, klass)
    {
        Receiver = receiver;
        CallerProc = callerProc;
        CallerProgramCounter = callerProgramCounter;
        CallerMethodId = callerMethodId;
        this.names = names;
        this.values = values;
        irepLocalCount = names.Length;
        liveSources = Array.Empty<(int, int)>();
    }

    public bool TryGetLocal(Symbol name, out MRubyValue value)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] == name)
            {
                value = ReadAt(i);
                return true;
            }
        }
        value = default;
        return false;
    }

    public void SetLocal(Symbol name, MRubyValue value)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] == name)
            {
                WriteAt(i, value);
                return;
            }
        }

        var n = names.Length;
        var newNames = new Symbol[n + 1];
        var newValues = new MRubyValue[n + 1];
        Array.Copy(names, newNames, n);
        Array.Copy(values, newValues, n);
        newNames[n] = name;
        newValues[n] = value;
        names = newNames;
        values = newValues;
    }

    public ReadOnlySpan<Symbol> LocalVariableNames => names;

    public ReadOnlySpan<MRubyValue> LocalVariableValues
    {
        get
        {
            if (liveContext is null) return values;
            var snapshot = new MRubyValue[names.Length];
            for (var i = 0; i < irepLocalCount; i++)
            {
                var (depth, slot) = liveSources[i];
                var sp = liveContext.CallStack[depth].StackPointer;
                snapshot[i] = liveContext.Stack[sp + 1 + slot];
            }
            if (names.Length > irepLocalCount)
            {
                values.AsSpan(irepLocalCount).CopyTo(snapshot.AsSpan(irepLocalCount));
            }
            return snapshot;
        }
    }

    public bool TryGetSourcePosition(out string? filename, out int line)
    {
        if (CallerProc?.Irep.DebugInfo is { } dbg &&
            dbg.TryFindPosition(CallerProgramCounter, out filename, out line))
        {
            return true;
        }
        filename = null;
        line = -1;
        return false;
    }

    internal void FreezeFromFrame(MRubyValue[] stack, int stackPointer)
    {
        if (liveContext is null) return;
        for (var i = 0; i < irepLocalCount; i++)
        {
            var (depth, slot) = liveSources[i];
            if (depth == liveCallDepth)
            {
                values[i] = stack[stackPointer + 1 + slot];
            }
            else if (depth >= 0 && depth < liveContext.CallDepth)
            {
                var sp = liveContext.CallStack[depth].StackPointer;
                values[i] = liveContext.Stack[sp + 1 + slot];
            }
            else
            {
                // Outer frame already popped (closure escaped its creator); env capture not modeled.
                values[i] = MRubyValue.Nil;
            }
        }
        liveContext = null;
        liveCallDepth = -1;
    }

    MRubyValue ReadAt(int index)
    {
        if (liveContext is not null && index < irepLocalCount)
        {
            var (depth, slot) = liveSources[index];
            var sp = liveContext.CallStack[depth].StackPointer;
            return liveContext.Stack[sp + 1 + slot];
        }
        return values[index];
    }

    void WriteAt(int index, MRubyValue value)
    {
        if (liveContext is not null && index < irepLocalCount)
        {
            var (depth, slot) = liveSources[index];
            var sp = liveContext.CallStack[depth].StackPointer;
            liveContext.Stack[sp + 1 + slot] = value;
            return;
        }
        values[index] = value;
    }
}
