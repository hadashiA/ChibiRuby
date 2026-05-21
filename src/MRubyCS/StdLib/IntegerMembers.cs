using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utf8StringInterpolation;

namespace MRubyCS.StdLib;

[RubyClass("Integer", Superclass = "Numeric")]
static class IntegerMembers
{
    [DoesNotReturn]
    internal static void RaiseIntegerOverflowError(MRubyState state, ReadOnlySpan<byte> message)
    {
        state.Raise(Names.RangeError, Utf8String.Format($"Integer overflow in {message}"));
    }

    [DoesNotReturn]
    internal static void RaiseDivideByZeroError(MRubyState state)
    {
        state.Raise(Names.RangeError, "divided by 0"u8);
    }

    [DoesNotReturn]
    internal static void RaiseIntegerNoConversionError(MRubyState state, MRubyValue value)
    {
        state.Raise(Names.TypeError, Utf8String.Format($"can't convert {state.TypeNameOf(value)} into Integer"));
    }

    internal static MRubyValue IntPow(MRubyState state, MRubyValue x, MRubyValue y)
    {
        var a = state.AsInteger(x);
        if (a == 0) return x;
        var other = state.GetArgumentAt(0);
        if (other.IsFloat)
        {
            return Math.Pow(a, other.FloatValue);
        }
        var exp = state.AsInteger(other);
        if (exp < 0)
        {
            return Math.Pow(a, other.FloatValue);
        }
        try
        {
            var result = 1L;
            while (true)
            {
                if ((exp & 1) != 0)
                {
                    result = checked(result * a);
                }
                exp >>= 1;
                if (exp == 0) break;
                a = checked(a * a);
            }
            return result;
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "power"u8);
            return default;
        }
    }

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue OpPow(MRubyState state, MRubyValue self) => IntPow(state, self, state.GetArgumentAt(0));

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue OpAdd(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            try
            {
                return checked(a + other.IntegerValue);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "addition"u8);
                return default;
            }
        }
        if (other.IsFloat)
        {
            return a + other.FloatValue;
        }
        state.Raise(Names.TypeError, "non integer addition"u8);
        return default;
    }

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue OpSub(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            try
            {
                return checked(a - other.IntegerValue);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "subtraction"u8);
                return default;
            }
        }
        if (other.IsFloat)
        {
            return a - other.FloatValue;
        }
        state.Raise(Names.TypeError, "non integer subtraction"u8);
        return default;
    }

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue OpMul(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            try
            {
                return checked(a * other.IntegerValue);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "multiplication"u8);
                return default;
            }
        }
        if (other.IsFloat)
        {
            return a - other.FloatValue;
        }
        state.Raise(Names.TypeError, Utf8String.Format($"can't convert {state.TypeNameOf(other)} into Integer"));
        return default;
    }

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue OpDiv(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            if (other.IntegerValue == 0)
            {
                RaiseDivideByZeroError(state);
                return default;
            }
            return a / other.IntegerValue;
        }
        if (other.IsFloat)
        {
            if (other.FloatValue == 0)
            {
                RaiseDivideByZeroError(state);
                return default;
            }
            return a / other.FloatValue;
        }
        RaiseIntegerNoConversionError(state, other);
        return default;
    }

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue Quo(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        var f = state.AsFloat(other);
        if (f == 0)
        {
            RaiseDivideByZeroError(state);
            return default;
        }
        return state.AsInteger(self) / f;
    }

    [RubyDef("(Numeric) -> Integer")]
    public static MRubyValue IntDiv(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        if (b == 0)
        {
            RaiseDivideByZeroError(state);
            return default;
        }
        return a / b;
    }

    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue FDiv(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsFloat(other);
        if (b == 0)
        {
            RaiseDivideByZeroError(state);
            return default;
        }
        return a / b;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpAnd(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        return a & b;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpOr(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        return a | b;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpXor(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        return a ^ b;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpLShift(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var width = state.AsInteger(other);
        if (a == 0 || width == 0) return self;
        if (NumShift(state, a, width, out var num))
        {
            return num;
        }
        RaiseIntegerOverflowError(state, "bit  shift"u8);
        return default;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpRShift(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var width = state.AsInteger(other);
        if (a == 0 || width == 0) return self;
        if (NumShift(state, a, -width, out var num))
        {
            return num;
        }
        RaiseIntegerOverflowError(state, "bit  shift"u8);
        return default;
    }


    [RubyDef("(?Integer) -> String")]


    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var basis = 10;
        if (state.GetArgumentCount() > 0)
        {
            basis = (int)state.GetArgumentAsIntegerAt(0);
        }

        return state.StringifyInteger(self, basis);
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue OpPlus(MRubyState state, MRubyValue self) => +self.IntegerValue;

    [RubyDef("() -> Integer")]
    public static MRubyValue OpMinus(MRubyState state, MRubyValue self) => -self.IntegerValue;

    [RubyDef("() -> Integer")]
    public static MRubyValue Abs(MRubyState state, MRubyValue self) => Math.Abs(self.IntegerValue);

    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue Mod(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        if (a == 0) return self;

        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            var b = other.IntegerValue;
            if (b == 0)
            {
                state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
            }

            var mod = a % b;
            if ((a < 0) != (b < 0) && mod != 0)
            {
                mod += b;
            }
            return mod;
        }
        return FloatMembers.Mod(state, self);
    }

    [RubyDef("(?Integer) -> Integer")]

    public static MRubyValue Ceil(MRubyState state, MRubyValue self)
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return 0;
        }
        if (f.IsNil)
        {
            return self.IntegerValue;
        }
        var a = state.AsInteger(self);
        var b = state.AsInteger(f);
        var c = a % b;
        var neg = a < 0;
        a -= c;
        if (!neg)
        {
            try
            {
                a = checked(a + b);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "ceiling"u8);
                return default;
            }
        }
        return a;
    }

    [RubyDef("(?Integer) -> Integer")]

    public static MRubyValue Floor(MRubyState state, MRubyValue self)
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return 0;
        }
        if (f.IsNil)
        {
            return self.IntegerValue;
        }
        var a = state.AsInteger(self);
        var b = state.AsInteger(f);
        var c = a % b;
        var neg = a < 0;
        a -= c;
        if (!neg)
        {
            try
            {
                a = checked(a - b);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "floor"u8);
                return default;
            }
        }
        return a;
    }

    [RubyDef("(?Integer) -> Integer")]

    public static MRubyValue Round(MRubyState state, MRubyValue self)
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return 0;
        }
        if (f.IsNil)
        {
            return self.IntegerValue;
        }
        var a = state.AsInteger(self);
        var b = state.AsInteger(f);
        var c = a % b;
        a -= c;

        try
        {
            if (c < 0)
            {
                c = -c;
                if (b / 2 < c)
                {
                    c = checked(a - b);
                }
                a = c;
            }
            else
            {
                if (b / 2 < c)
                {
                    c = checked(a + b);
                }
                a = c;
            }

            return a;
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "round"u8);
            return default;
        }
    }


    [RubyDef("() -> Integer")]


    public static MRubyValue Next(MRubyState state, MRubyValue self)
    {
        try
        {
            return checked(self.IntegerValue + 1);
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "next"u8);
            return default;
        }
    }

    [RubyDef("(?Integer) -> Integer")]

    public static MRubyValue Truncate(MRubyState state, MRubyValue self)
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return 0;
        }
        if (f.IsNil)
        {
            return self.IntegerValue;
        }
        var a = state.AsInteger(self);
        var b = state.AsInteger(f);
        return a - (a % b);
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        var n = state.AsInteger(self);
        return RString.GetHashCode(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref n), sizeof(long)));
    }

    [RubyDef("(Numeric) -> Array[Numeric]")]

    public static MRubyValue DivMod(MRubyState state, MRubyValue self)
    {
        var n = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            IntDivMod(state, n, other.IntegerValue, out var div, out var mod);
            return state.NewArray(div, mod);
        }
        return FloatMembers.DivMod(state, self);
    }

    [RubyDef("() -> Float")]

    public static MRubyValue ToF(MRubyState state, MRubyValue self) => (double)state.AsInteger(self);

    [RubyDef("() -> Integer")]
    public static MRubyValue BitLength(MRubyState state, MRubyValue self)
    {
        var v = state.AsInteger(self);
        // Two's-complement bit_length: negatives use ~v.
        var x = (ulong)(v < 0 ? ~v : v);
        long bits = 0;
        while (x != 0)
        {
            bits++;
            x >>= 1;
        }
        return bits;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue Gcd(MRubyState state, MRubyValue self)
    {
        var a = Math.Abs(state.AsInteger(self));
        var b = Math.Abs(state.GetArgumentAsIntegerAt(0));
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue Lcm(MRubyState state, MRubyValue self)
    {
        var a = Math.Abs(state.AsInteger(self));
        var b = Math.Abs(state.GetArgumentAsIntegerAt(0));
        if (a == 0 || b == 0) return 0L;
        // gcd then a/gcd*b, with overflow check.
        long ga = a, gb = b;
        while (gb != 0)
        {
            (ga, gb) = (gb, ga % gb);
        }
        try
        {
            return checked((a / ga) * b);
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "lcm"u8);
            return default;
        }
    }

    internal static bool NumShift(MRubyState state, long val, long width, out long num)
    {
        const int numericShiftWidthMax = 8 * sizeof(long) - 1;
        if (width < 0)
        {
            /* rshift */
            if (width == long.MinValue || -width >= (sizeof(long) - 1))
            {
                if (val < 0)
                {
                    num = -1;
                }
                else
                {
                    num = 0;
                }
            }
            else
            {
                num = val >> -(int)width;
            }
        }
        else if (val > 0)
        {
            if ((width > numericShiftWidthMax) ||
                (val > (long.MaxValue >> (int)width)))
            {
                num = default;
                return false;
            }
            num = val << (int)width;
        }
        else
        {
            if ((width > numericShiftWidthMax) ||
                (val < (long.MinValue >> (int)width)))
            {
                num = default;
                return false;
            }
            if (width == numericShiftWidthMax)
            {
                num = long.MinValue;
            }
            else
            {
                num = val * (1L << (int)width);
            }
        }
        return true;
    }

    internal static MRubyValue PrepareIntRounding(MRubyState state, MRubyValue x)
    {
        if (state.GetArgumentCount() <= 1)
        {
            return MRubyValue.Nil;
        }

        var other = state.GetArgumentAsFloatAt(0);
        if (-0.415241 * other - 0.125 > sizeof(long))
        {
            return MRubyValue.Undef;
        }
        return IntPow(state, 10, -other);
    }

    internal static void IntDivMod(MRubyState state, long x, long y, out long divp, out long modp)
    {
        if (y == 0)
        {
            RaiseDivideByZeroError(state);
            Unsafe.SkipInit(out divp);
            Unsafe.SkipInit(out modp);
            return;
        }
        else if (x == int.MinValue && y == -1)
        {
            RaiseIntegerOverflowError(state, "division"u8);
            Unsafe.SkipInit(out divp);
            Unsafe.SkipInit(out modp);
            return;
        }
        else
        {
            long div = x / y;
            long mod = x - div * y;

            if ((x ^ y) < 0 && x != div * y)
            {
                mod += y;
                div -= 1;
            }
            divp = div;
            modp = mod;
        }
    }
}