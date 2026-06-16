using System;
using ChibiRuby.Internals;

namespace ChibiRuby;

public interface ICallScope
{
    RClass TargetClass { get; }
}

/// <summary>
/// Closure captured context
/// </summary>
class REnv() : RBasic(MRubyVType.Env, default!), ICallScope
{
    public required int StackPointer { get; init; }
    public required int StackSize { get; init; }
    public required int BlockArgumentOffset { get; init; }
    public required MRubyContext? Context { get; init; }
    public required RClass TargetClass { get; init; }
    public required Symbol MethodId { get; init; }
    public required MRubyMethodVisibility Visibility { get; internal set; }
    public required bool VisibilityBreak { get; init; }

    public bool OnStack => Context != null;

    public Span<MRubyValue> Stack
    {
        get
        {
            if (capturedStack.HasValue) return capturedStack.Value.Span;
            if (Context is null) return Span<MRubyValue>.Empty;
            return Context.Stack.AsSpan(StackPointer, StackSize);
        }
    }

    Memory<MRubyValue>? capturedStack;

    public void CaptureStack()
    {
        if (StackSize == 0)
        {
            capturedStack = Memory<MRubyValue>.Empty;
            return;
        }

        capturedStack = new Memory<MRubyValue>(Stack.ToArray());
    }
}

public class RProc(Irep irep, int programCounter, RClass procClass) : RObject(MRubyVType.Proc, procClass), IEquatable<RProc>
{
    public required RProc? Upper { get; init; }
    public required ICallScope? Scope
    {
        get => scope;
        init => scope = value;
    }

    public Irep Irep => irep;
    public int ProgramCounter => programCounter;

    ICallScope? scope;

    internal RProc FindReturningDestination(out REnv? env)
    {
        var p = this;
        env = p.Scope as REnv;
        while (p.Upper != null)
        {
            if (p.HasFlag(MRubyObjectFlags.ProcScope | MRubyObjectFlags.ProcStrict))
            {
                return p;
            }
            env = p.Scope as REnv;
            p = p.Upper;
        }
        return p;
    }

    internal REnv? FindUpperEnvTo(int up)
    {
        RProc? proc = this;
        while (up-- > 0)
        {
            proc = proc.Upper;
            if (proc is null) return null;
        }
        return proc.Scope as REnv;
    }

    internal void UpdateScope(ICallScope scope)
    {
        this.scope = scope;
    }

    public RProc Dup()
    {
        var clone = new RProc(Irep, ProgramCounter, Class)
        {
            Upper = Upper,
            Scope = Scope,
        };
        clone.SetFlag(Flags);
        return clone;
    }

    public bool Equals(RProc? other)
    {
        if (other is { } otherProc)
        {
            return Irep == otherProc.Irep && ProgramCounter == otherProc.ProgramCounter;
        }
        return false;
    }

    internal override RObject Clone()
    {
        var clone = Dup();
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public static bool operator ==(RProc? a, RProc? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
            return false;
        return a.Equals(b);
    }

    public static bool operator !=(RProc? a, RProc? b) => !(a == b);

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RProc)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Upper, Scope);
    }
}
