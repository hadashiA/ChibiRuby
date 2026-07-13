using System;
using System.Runtime.CompilerServices;

namespace ChibiRuby;

public delegate MRubyValue MRubyFunc(MRubyState state, MRubyValue self);
public delegate MRubyValue MRubyPureUnaryFunc(MRubyState state, MRubyValue self, MRubyValue argument);
public delegate MRubyValue MRubyPureUnaryFloatFunc(MRubyState state, MRubyValue self, double argument);

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

[Flags]
public enum MRubyMethodFlags
{
    None = 0,
    Pure = 1,
}

public readonly struct MRubyMethod : IEquatable<MRubyMethod>
{
    public static readonly MRubyMethod Nop = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod Undef = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod True = new((_, _) => MRubyValue.True);
    public static readonly MRubyMethod False = new((_, _) => MRubyValue.False);
    public static readonly MRubyMethod Identity = new((_, self) => self);

    readonly object? body;
    readonly MRubyPureUnaryFunc? pureUnaryFunc;
    readonly MRubyPureUnaryFloatFunc? pureUnaryFloatFunc;
    public readonly MRubyMethodVisibility Visibility;
    public readonly MRubyMethodKind Kind;
    public readonly MRubyMethodFlags Flags;

    /// <summary>
    /// When non-default, this method is a trivial ivar getter (no-arg method that returns this ivar).
    /// Used by Send fast path to skip full dispatch.
    /// Works for both RProc (bytecode def x; @x; end) and CSharpFunc (attr_reader :x).
    /// </summary>
    public readonly Symbol TrivialGetterIVarSymbol;

    /// <summary>
    /// When non-default, this method is a trivial ivar setter (one-arg method that writes this ivar).
    /// Used by Send fast paths and scalar replacement to skip full dispatch.
    /// </summary>
    public readonly Symbol TrivialSetterIVarSymbol;

    public RProc? Proc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == MRubyMethodKind.RProc ? Unsafe.As<RProc>(body!) : null;
    }

    public MRubyMethod(RProc proc, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        body = proc;
        pureUnaryFunc = null;
        pureUnaryFloatFunc = null;
        Kind = MRubyMethodKind.RProc;
        Visibility = visibility;
        Flags = MRubyMethodFlags.None;
        TrivialGetterIVarSymbol = default;
        TrivialSetterIVarSymbol = default;
    }

    public MRubyMethod(
        MRubyFunc? func,
        MRubyMethodVisibility visibility = MRubyMethodVisibility.Default,
        MRubyMethodFlags flags = MRubyMethodFlags.None)
    {
        body = func;
        pureUnaryFunc = null;
        pureUnaryFloatFunc = null;
        Kind = MRubyMethodKind.CSharpFunc;
        Visibility = visibility;
        Flags = flags;
        TrivialGetterIVarSymbol = default;
        TrivialSetterIVarSymbol = default;
    }

    public MRubyMethod(
        MRubyFunc? func,
        MRubyPureUnaryFunc pureUnaryFunc,
        MRubyMethodVisibility visibility = MRubyMethodVisibility.Default,
        MRubyMethodFlags flags = MRubyMethodFlags.Pure)
    {
        body = func;
        this.pureUnaryFunc = pureUnaryFunc;
        pureUnaryFloatFunc = null;
        Kind = MRubyMethodKind.CSharpFunc;
        Visibility = visibility;
        Flags = flags | MRubyMethodFlags.Pure;
        TrivialGetterIVarSymbol = default;
        TrivialSetterIVarSymbol = default;
    }

    public MRubyMethod(
        MRubyFunc? func,
        MRubyPureUnaryFunc pureUnaryFunc,
        MRubyPureUnaryFloatFunc pureUnaryFloatFunc,
        MRubyMethodVisibility visibility = MRubyMethodVisibility.Default,
        MRubyMethodFlags flags = MRubyMethodFlags.Pure)
    {
        body = func;
        this.pureUnaryFunc = pureUnaryFunc;
        this.pureUnaryFloatFunc = pureUnaryFloatFunc;
        Kind = MRubyMethodKind.CSharpFunc;
        Visibility = visibility;
        Flags = flags | MRubyMethodFlags.Pure;
        TrivialGetterIVarSymbol = default;
        TrivialSetterIVarSymbol = default;
    }

    MRubyMethod(
        object body,
        MRubyMethodKind kind,
        MRubyMethodVisibility visibility,
        Symbol trivialGetterIVarSymbol,
        Symbol trivialSetterIVarSymbol,
        MRubyMethodFlags flags,
        MRubyPureUnaryFunc? pureUnaryFunc = null,
        MRubyPureUnaryFloatFunc? pureUnaryFloatFunc = null)
    {
        this.body = body;
        this.pureUnaryFunc = pureUnaryFunc;
        this.pureUnaryFloatFunc = pureUnaryFloatFunc;
        Kind = kind;
        Visibility = visibility;
        TrivialGetterIVarSymbol = trivialGetterIVarSymbol;
        TrivialSetterIVarSymbol = trivialSetterIVarSymbol;
        Flags = flags;
    }

    public MRubyMethod WithVisibility(MRubyMethodVisibility visibility)
    {
        if (TrivialGetterIVarSymbol.Value != 0 ||
            TrivialSetterIVarSymbol.Value != 0)
        {
            return new MRubyMethod(
                body!,
                Kind,
                visibility,
                TrivialGetterIVarSymbol,
                TrivialSetterIVarSymbol,
                Flags,
                pureUnaryFunc,
                pureUnaryFloatFunc);
        }

        if (Kind == MRubyMethodKind.RProc)
        {
            return new MRubyMethod(Unsafe.As<RProc>(body!), visibility);
        }

        var func = Unsafe.As<MRubyFunc>(body!);
        return pureUnaryFunc is { } unaryFunc
            ? pureUnaryFloatFunc is { } unaryFloatFunc
                ? new MRubyMethod(func, unaryFunc, unaryFloatFunc, visibility, Flags)
                : new MRubyMethod(func, unaryFunc, visibility, Flags)
            : new MRubyMethod(func, visibility, Flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Invoke(MRubyState state, MRubyValue self)
    {
        return Unsafe.As<MRubyFunc>(body!).Invoke(state, self);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvokePureUnary(MRubyState state, MRubyValue self, MRubyValue argument, out MRubyValue result)
    {
        if (pureUnaryFunc is { } func &&
            (Flags & MRubyMethodFlags.Pure) != 0)
        {
            result = func(state, self, argument);
            return true;
        }

        result = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvokePureUnaryNumeric(MRubyState state, MRubyValue self, MRubyValue argument, out MRubyValue result)
    {
        if (pureUnaryFloatFunc is { } func &&
            (Flags & MRubyMethodFlags.Pure) != 0)
        {
            if (argument.IsFloat)
            {
                result = func(state, self, argument.FloatValue);
                return true;
            }
            if (argument.IsFixnum)
            {
                result = func(state, self, argument.FixnumValue);
                return true;
            }
        }

        return TryInvokePureUnary(state, self, argument, out result);
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
        return left.body is MRubyFunc func && func.Equals(right);
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
    /// Create method from RProc, detecting trivial getter/setter patterns.
    /// </summary>
    internal static MRubyMethod CreateFromProc(
        RProc proc,
        MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
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
                return new MRubyMethod(
                    proc,
                    MRubyMethodKind.RProc,
                    visibility,
                    irep.Symbols[symIdx],
                    default,
                    MRubyMethodFlags.None);
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
                return new MRubyMethod(
                    proc,
                    MRubyMethodKind.RProc,
                    visibility,
                    irep.Symbols[symIdx],
                    default,
                    MRubyMethodFlags.None);
            }
        }

        if (TryDetectTrivialSetter(proc, out var setterSymbol))
        {
            return new MRubyMethod(
                proc,
                MRubyMethodKind.RProc,
                visibility,
                default,
                setterSymbol,
                MRubyMethodFlags.None);
        }

        return new MRubyMethod(proc, visibility);
    }

    /// <summary>
    /// Create a CSharpFunc method marked as a trivial getter for the given ivar symbol.
    /// </summary>
    internal static MRubyMethod CreateTrivialGetter(MRubyFunc func, Symbol ivarSymbol, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        return new MRubyMethod(
            func,
            MRubyMethodKind.CSharpFunc,
            visibility,
            ivarSymbol,
            default,
            MRubyMethodFlags.None);
    }

    internal static MRubyMethod CreateTrivialSetter(MRubyFunc func, Symbol ivarSymbol, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        return new MRubyMethod(
            func,
            MRubyMethodKind.CSharpFunc,
            visibility,
            default,
            ivarSymbol,
            MRubyMethodFlags.None);
    }

    static bool TryDetectTrivialSetter(RProc proc, out Symbol symbol)
    {
        symbol = default;
        if (proc.ProgramCounter != 0 || !proc.HasFlag(MRubyObjectFlags.ProcStrict))
        {
            return false;
        }

        var irep = proc.Irep;
        var seq = irep.Sequence;
        if (irep.CatchHandlers.Length != 0 ||
            irep.RegisterVariableCount == 0 ||
            seq.Length < 4 ||
            (OpCode)seq[0] != OpCode.Enter ||
            !TryReadSimpleEnterArgumentCount(seq, 0, out var argumentCount) ||
            argumentCount != 1 ||
            irep.RegisterVariableCount > 16)
        {
            return false;
        }

        var pc = 4;
        Span<int> registerArguments = stackalloc int[16];
        registerArguments.Fill(-1);
        registerArguments[1] = 0;
        var foundSet = false;

        while (pc < seq.Length)
        {
            switch ((OpCode)seq[pc])
            {
                case OpCode.Nop:
                    pc++;
                    break;
                case OpCode.Move:
                {
                    if (pc + 2 >= seq.Length)
                    {
                        return false;
                    }

                    var destination = seq[pc + 1];
                    var source = seq[pc + 2];
                    if ((uint)destination >= (uint)registerArguments.Length ||
                        (uint)source >= (uint)registerArguments.Length ||
                        registerArguments[source] < 0)
                    {
                        return false;
                    }

                    registerArguments[destination] = registerArguments[source];
                    pc += 3;
                    break;
                }
                case OpCode.SetIV:
                {
                    if (pc + 2 >= seq.Length)
                    {
                        return false;
                    }

                    var source = seq[pc + 1];
                    var symbolIndex = seq[pc + 2];
                    if (foundSet ||
                        (uint)source >= (uint)registerArguments.Length ||
                        registerArguments[source] != 0 ||
                        (uint)symbolIndex >= (uint)irep.Symbols.Length)
                    {
                        return false;
                    }

                    symbol = irep.Symbols[symbolIndex];
                    foundSet = true;
                    pc += 3;
                    break;
                }
                case OpCode.Return:
                    return pc + 1 < seq.Length &&
                           foundSet &&
                           (uint)seq[pc + 1] < (uint)registerArguments.Length &&
                           registerArguments[seq[pc + 1]] == 0 &&
                           FinishOnlyNops(seq, pc + 2);
                default:
                    return false;
            }
        }

        return false;
    }

    static bool TryReadSimpleEnterArgumentCount(byte[] sequence, int pc, out int argumentCount)
    {
        argumentCount = 0;
        if (pc + 3 >= sequence.Length)
        {
            return false;
        }

        var aspec = new ArgumentSpec(ReadUInt24(sequence, pc + 1));
        if (aspec.OptionalArgumentsCount != 0 ||
            aspec.TakeRestArguments ||
            aspec.MandatoryArguments2Count != 0 ||
            aspec.KeywordArgumentsCount != 0 ||
            aspec.TakeKeywordDict ||
            aspec.TakeBlock)
        {
            return false;
        }

        argumentCount = aspec.MandatoryArguments1Count;
        return true;
    }

    static bool FinishOnlyNops(byte[] sequence, int pc)
    {
        while (pc < sequence.Length)
        {
            if ((OpCode)sequence[pc] != OpCode.Nop)
            {
                return false;
            }

            pc++;
        }

        return true;
    }

    static uint ReadUInt24(byte[] sequence, int offset) =>
        (uint)(sequence[offset] << 16 | sequence[offset + 1] << 8 | sequence[offset + 2]);
}
