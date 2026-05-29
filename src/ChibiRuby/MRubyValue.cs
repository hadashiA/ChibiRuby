using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Utf8StringInterpolation;

namespace ChibiRuby;

public enum MRubyVType
{
    Nil = 0,
    False,
    True,
    Symbol,
    Undef,
    Free,
    Float,
    Integer,
    CPtr,
    Object,
    Class,
    Module,
    IClass, // Include class
    SClass, // Singleton class
    Proc,
    Array,
    Hash,
    String,
    Range,
    Exception,
    Env,
    CSharpData,
    Fiber,
    Struct,
    Break,
    Complex,
    Rational,
    BigInt,
}

public static class MRubyVTypeExtensions
{
    public static ReadOnlySpan<byte> ToUtf8String(this MRubyVType vType)
    {
        return Utf8String.Format($"{vType}");
    }

    public static bool IsClass(this MRubyVType vType) => vType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
}

// mrb_value representation:
//
// 64-bit word with inline float:
//   nil   : ...0000 0000 (all bits are 0)
//   false : ...0000 0100 (mrb_fixnum(v) != 0)
//   true  : ...0000 1100
//   undef : ...0001 0100
//   symbol: ...0001 1100 (use only upper 32-bit as symbol value with MRB_64BIT)
//   fixnum: ...IIII III1
//   float : ...FFFF FF10 (51 bit significands; require MRB_64BIT)
//   object: ...PPPP P000
public readonly struct MRubyValue : IEquatable<MRubyValue>
{
    public static MRubyValue Nil => default;
    public static MRubyValue False => new(FalseBits, null);
    public static MRubyValue True => new(TrueBits, null);
    public static MRubyValue Undef => new(UndefBits, null);

    const long FalseBits = 0b0100;
    const long TrueBits = 0b1100;
    const long UndefBits = 0b0001_0100;
    const int SymbolShift = 32;

    internal const long FixnumMin = long.MinValue >> 1;
    internal const long FixnumMax = long.MaxValue >> 1;

    // Tagged 64-bit immediate. Exposed as internal so the VM hot path
    // (MRubyState.Vm.cs) can perform tagged-bits arithmetic without going
    // through getters that the JIT may decline to inline.
    internal readonly long bits;

    public RObject? Object
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    public MRubyVType VType => this switch
    {
        { Object: { } obj } => obj.VType,
        { IsTrue: true } => MRubyVType.True,
        { IsFalse: true } => MRubyVType.False,
        { IsUndef: true } => MRubyVType.Undef,
        { IsSymbol: true } => MRubyVType.Symbol,
        { IsFixnum: true } => MRubyVType.Integer,
        { IsFloat: true } => MRubyVType.Float,
        _ => default
    };

    public bool IsNil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == 0 && Object == null;
    }

    public bool IsFalse
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == FalseBits;
    }

    public bool IsTrue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == TrueBits;
    }

    public bool IsUndef
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == UndefBits;
    }

    public bool IsSymbol
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & 0b1_1111) == 0b1_1100;
    }

    public bool IsFixnum
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & 1) == 1;
    }

    public bool IsObject
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object != null;
    }

    public bool IsImmediate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object == null;
    }

    public bool Truthy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsFalse && !IsNil;
    }

    public bool Falsy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsNil || IsFalse;
    }

    public bool IsInteger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFixnum || Object?.VType == MRubyVType.Integer;
    }

    public bool IsFloat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & 0b11) == 0b10;
    }

    internal bool IsNumeric
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFixnum || IsFloat || Object?.VType == MRubyVType.Integer;
    }

    public bool IsBreak
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType == MRubyVType.Break;
    }

    public bool IsProc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType == MRubyVType.Proc;
    }

    public bool IsClass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
    }

    public bool IsNamespace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType is MRubyVType.Class or MRubyVType.Module;
    }

    public bool BoolValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & ~False.bits) != 0;
    }

    public long FixnumValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits >> 1;
    }

    public Symbol SymbolValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new((uint)(bits >> SymbolShift));
    }

    public long IntegerValue
    {
        get
        {
            if (Object?.VType == MRubyVType.Integer)
            {
                return As<RInteger>().Value;
            }
            return bits >> 1;
        }
    }

    public double FloatValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var fbits = bits & ~0b11;
            return Unsafe.As<long, double>(ref fbits);
        }
    }

    public unsafe long ObjectId
    {
        get
        {
            if (Object is { } obj)
            {
                return (nint)Unsafe.AsPointer(ref obj);
            }
            if (IsInteger) return IntegerValue;
            if (IsFloat) return (long)FloatValue;
            return bits;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(bool value)
    {
        bits = value ? TrueBits : FalseBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(int value)
    {
        bits = (value << 1) | 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(long value)
    {
        if (value > FixnumMax || value < FixnumMin)
        {
            ThrowIntegerOveflow(value);
        }
        bits = (value << 1) | 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(Symbol symbol)
    {
        bits = ((long)symbol.Value << SymbolShift) | 0b1_1100;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(double value)
    {
        var n = Unsafe.As<double, long>(ref value);
        bits = (n & ~0b11) | 0b10;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(RObject value)
    {
        Object = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MRubyValue(long bits, RObject? obj)
    {
        this.bits = bits;
        Object = obj;
    }

    // Implicit conversion operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(bool value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(RObject obj) => new(obj);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(long value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(int value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(Symbol symbol) => new(symbol);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(double value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(float value) => new(value);

    // Obsolete factory methods (kept for backward compatibility)
    [Obsolete("Use constructor instead: new MRubyValue(value)")]
    public static MRubyValue From(bool value) => new(value);

    [Obsolete("Use constructor instead: new MRubyValue(obj)")]
    public static MRubyValue From(RObject obj) => new(obj);

    [Obsolete("Use constructor instead: new MRubyValue(value)")]
    public static MRubyValue From(long value) => new(value);

    [Obsolete("Use constructor instead: new MRubyValue(symbol)")]
    public static MRubyValue From(Symbol symbol) => new(symbol);

    [Obsolete("Use constructor instead: new MRubyValue(value)")]
    public static MRubyValue From(double value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T As<T>() where T : RObject => (T)Object!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(MRubyValue other) => bits == other.bits &&
                                            Object == other.Object;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MRubyValue a, MRubyValue b) => a.Equals(b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MRubyValue a, MRubyValue b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        return obj is MRubyValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Object?.GetHashCode() ?? bits.GetHashCode();
    }

    public override string ToString()
    {
        if (Object is { } x) return x.ToString()!;
        if (IsNil) return "nil";

        return VType switch
        {
            MRubyVType.False => "false",
            MRubyVType.True => "true",
            MRubyVType.Undef => "undef",
            MRubyVType.Symbol => SymbolValue.ToString()!,
            MRubyVType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
            MRubyVType.Integer => IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => VType.ToString()
        };
    }

    static void ThrowIntegerOveflow(long intValue)
    {
        throw new ArgumentException(
            $"MRubyValue integers only support values up to 63 bits ({FixnumMax}). To hold larger values, use MRubyState.NewInteger.");
    }
}