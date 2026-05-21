using System;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public delegate MRubyValue MRubyFunc(MRubyState state, MRubyValue self);

public enum MRubyMethodKind
{
    RProc,
    CSharpFunc,
}

public enum MRubyMethodVisibility
{
    Default,
    Public,
    Private,
    Protected,
}

public readonly struct MRubyMethod : IEquatable<MRubyMethod>
{
    public static readonly MRubyMethod Nop = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod Undef = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod True = new((_, _) => MRubyValue.True);
    public static readonly MRubyMethod False = new((_, _) => MRubyValue.False);
    public static readonly MRubyMethod Identity = new((_, self) => self);

    readonly object? body;
    public readonly MRubyMethodVisibility Visibility;
    public readonly MRubyMethodKind Kind;

    /// <summary>
    /// When non-default, this method is a trivial ivar getter (no-arg method that returns this ivar).
    /// Used by Send fast path to skip full dispatch.
    /// Works for both RProc (bytecode def x; @x; end) and CSharpFunc (attr_reader :x).
    /// </summary>
    public readonly Symbol TrivialGetterIVarSymbol;

    public RProc? Proc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == MRubyMethodKind.RProc ? Unsafe.As<RProc>(body!) : null;
    }

    public MRubyMethod(RProc proc, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        body = proc;
        Kind = MRubyMethodKind.RProc;
        Visibility = visibility;
    }

    public MRubyMethod(MRubyFunc? func, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        body = func;
        Kind = MRubyMethodKind.CSharpFunc;
        Visibility = visibility;
    }

    MRubyMethod(object body, MRubyMethodKind kind, MRubyMethodVisibility visibility, Symbol trivialGetterIVarSymbol)
    {
        this.body = body;
        Kind = kind;
        Visibility = visibility;
        TrivialGetterIVarSymbol = trivialGetterIVarSymbol;
    }

    public MRubyMethod WithVisibility(MRubyMethodVisibility visibility)
    {
        if (TrivialGetterIVarSymbol.Value != 0)
            return new MRubyMethod(body!, Kind, visibility, TrivialGetterIVarSymbol);
        return Kind == MRubyMethodKind.RProc
            ? new MRubyMethod(Unsafe.As<RProc>(body!), visibility)
            : new MRubyMethod(Unsafe.As<MRubyFunc>(body!), visibility);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Invoke(MRubyState state, MRubyValue self)
    {
        return Unsafe.As<MRubyFunc>(body!).Invoke(state, self);
    }

    public bool Equals(MRubyMethod other)
    {
        return body == other.body;
    }

    public override bool Equals(object? obj)
    {
        return obj is MRubyMethod other && Equals(other);
    }

    public override int GetHashCode()
    {
        return body?.GetHashCode() ?? 0;
    }

    public static bool operator ==(MRubyMethod left, MRubyMethod right)
    {
        return left.Equals(right);
    }

    // Compare against a raw MRubyFunc. C# caches static method-group conversions
    // for the same target method, so `method == ArrayMembers.Push` works when
    // the right side is a non-generic static method.
    public static bool operator ==(MRubyMethod left, MRubyFunc right)
    {
        return ReferenceEquals(left.body, right);
    }

    public static bool operator !=(MRubyMethod left, MRubyFunc right)
    {
        return !(left == right);
    }

    public static bool operator !=(MRubyMethod left, MRubyMethod right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Create method from RProc, detecting trivial getter pattern.
    /// Patterns: Enter(BBB) + GetIV(BB) + Return(B) = 9 bytes, or GetIV(BB) + Return(B) = 5 bytes
    /// </summary>
    internal static MRubyMethod CreateFromProc(RProc proc, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        var irep = proc.Irep;
        var seq = irep.Sequence;

        // Enter(BBB=4) + GetIV(BB=3) + Return(B=2) = 9 bytes
        if (seq.Length == 9 &&
            seq[0] == (byte)OpCode.Enter &&
            seq[4] == (byte)OpCode.GetIV &&
            seq[7] == (byte)OpCode.Return &&
            seq[5] == seq[8]) // GetIV target register == Return register
        {
            var symIdx = seq[6];
            if (symIdx < irep.Symbols.Length)
            {
                return new MRubyMethod(proc, MRubyMethodKind.RProc, visibility, irep.Symbols[symIdx]);
            }
        }

        // GetIV(BB=3) + Return(B=2) = 5 bytes (no Enter)
        if (seq.Length == 5 &&
            seq[0] == (byte)OpCode.GetIV &&
            seq[3] == (byte)OpCode.Return &&
            seq[1] == seq[4])
        {
            var symIdx = seq[2];
            if (symIdx < irep.Symbols.Length)
            {
                return new MRubyMethod(proc, MRubyMethodKind.RProc, visibility, irep.Symbols[symIdx]);
            }
        }

        return new MRubyMethod(proc, visibility);
    }

    /// <summary>
    /// Create a CSharpFunc method marked as a trivial getter for the given ivar symbol.
    /// </summary>
    internal static MRubyMethod CreateTrivialGetter(MRubyFunc func, Symbol ivarSymbol, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        return new MRubyMethod(func, MRubyMethodKind.CSharpFunc, visibility, ivarSymbol);
    }
}
