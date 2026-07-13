using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utf8StringInterpolation;

namespace ChibiRuby.StdLib;

/// <summary>
/// Arbitrary-precision-style integer (backed by a 64-bit value in ChibiRuby).
/// Created by integer literals like <c>42</c> or <c>0xff</c>, and used for
/// counting, indexing, and bit manipulation. <c>Integer</c> is immutable,
/// includes <c>Comparable</c>, and supports the full set of numeric and
/// bitwise operators.
/// </summary>
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

    /// <summary>
    /// Returns <c>self</c> raised to the power of the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 2 ** 10    # => 1024
    /// 3 ** 0     # => 1
    /// 2 ** -1    # => 0.5
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Numeric")]
    public static MRubyValue OpPow(MRubyState state, MRubyValue self) => IntPow(state, self, state.GetArgumentAt(0));

    /// <summary>
    /// Returns the sum of <c>self</c> and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 1 + 2      # => 3
    /// 1 + 2.5    # => 3.5
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the difference of <c>self</c> minus the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 5 - 2      # => 3
    /// 1 - 0.5    # => 0.5
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the product of <c>self</c> and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 3 * 4      # => 12
    /// 2 * 1.5    # => 3.0
    /// </code>
    /// </example>
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
            return a * other.FloatValue;
        }
        state.Raise(Names.TypeError, Utf8String.Format($"can't convert {state.TypeNameOf(other)} into Integer"));
        return default;
    }

    /// <summary>
    /// Divides <c>self</c> by the argument. Integer division truncates toward zero.
    /// Raises <c>ZeroDivisionError</c> when the argument is zero.
    /// </summary>
    /// <example>
    /// <code>
    /// 10 / 3     # => 3
    /// 10 / 3.0   # => 3.3333333333333335
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the quotient of <c>self</c> divided by the argument as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 10.quo(3)    # => 3.3333333333333335
    /// 7.quo(2)     # => 3.5
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the integer quotient of <c>self</c> divided by the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 11.div(3)    # => 3
    /// (-11).div(3) # => -4
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the floating-point quotient of <c>self</c> divided by the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 10.fdiv(3)    # => 3.3333333333333335
    /// 4.fdiv(2)     # => 2.0
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the bitwise AND of <c>self</c> and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 0b1100 &amp; 0b1010    # => 8
    /// 5 &amp; 3              # => 1
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpAnd(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        return a & b;
    }

    /// <summary>
    /// Returns the bitwise OR of <c>self</c> and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 0b1100 | 0b1010    # => 14
    /// 5 | 3              # => 7
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpOr(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        return a | b;
    }

    /// <summary>
    /// Returns the bitwise exclusive OR of <c>self</c> and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 0b1100 ^ 0b1010    # => 6
    /// 5 ^ 3              # => 6
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpXor(MRubyState state, MRubyValue self)
    {
        var a = state.AsInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.AsInteger(other);
        return a ^ b;
    }

    /// <summary>
    /// Returns <c>self</c> shifted left by the given number of bits.
    /// Negative shifts are equivalent to right shifts.
    /// </summary>
    /// <example>
    /// <code>
    /// 1 &lt;&lt; 4    # => 16
    /// 5 &lt;&lt; 2    # => 20
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>self</c> shifted right (arithmetically) by the given number of bits.
    /// Negative shifts are equivalent to left shifts.
    /// </summary>
    /// <example>
    /// <code>
    /// 16 &gt;&gt; 2    # => 4
    /// 20 &gt;&gt; 2    # => 5
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the bit at the given offset, using Ruby's infinite two's-complement integer semantics.
    /// </summary>
    /// <example>
    /// <code>
    /// 5[0]     # => 1
    /// 5[1]     # => 0
    /// (-1)[64] # => 1
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Integer")]
    public static MRubyValue OpAref(MRubyState state, MRubyValue self)
    {
        var value = self.IsFixnum ? self.FixnumValue : state.AsInteger(self);
        var offsetValue = state.GetArgumentAt(0);
        var offset = offsetValue.IsFixnum ? offsetValue.FixnumValue : state.AsInteger(offsetValue);
        if (offset < 0)
        {
            return 0;
        }
        if (offset > 8 * sizeof(long) - 1)
        {
            return value < 0 ? 1 : 0;
        }
        return (value >> (int)offset) & 1;
    }


    /// <summary>
    /// Returns the string representation of <c>self</c> in the given base (default 10).
    /// </summary>
    /// <example>
    /// <code>
    /// 12345.to_s        # => "12345"
    /// 255.to_s(16)      # => "ff"
    /// 10.to_s(2)        # => "1010"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Unary plus; returns <c>self</c> unchanged.
    /// </summary>
    /// <example>
    /// <code>
    /// +5     # => 5
    /// +(-3)  # => -3
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue OpPlus(MRubyState state, MRubyValue self) => +self.IntegerValue;

    /// <summary>
    /// Unary minus; returns <c>self</c> negated.
    /// </summary>
    /// <example>
    /// <code>
    /// -5     # => -5
    /// -(-3)  # => 3
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue OpMinus(MRubyState state, MRubyValue self) => -self.IntegerValue;

    /// <summary>
    /// Returns the bitwise complement of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// ~0 # => -1
    /// ~2 # => -3
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue OpNeg(MRubyState state, MRubyValue self) => ~state.AsInteger(self);

    /// <summary>
    /// Returns the absolute value of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 5.abs       # => 5
    /// (-7).abs    # => 7
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Abs(MRubyState state, MRubyValue self) => Math.Abs(self.IntegerValue);

    /// <summary>
    /// Returns <c>self</c> modulo the argument. The sign of the result follows the divisor.
    /// </summary>
    /// <example>
    /// <code>
    /// 10 % 3      # => 1
    /// (-10) % 3   # => 2
    /// 10 % -3     # => -2
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>self</c> rounded up to the nearest multiple of <c>10**-ndigits</c>.
    /// With no argument or non-negative <c>ndigits</c>, returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1234.ceil(-2)    # => 1300
    /// 1234.ceil        # => 1234
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>self</c> rounded down to the nearest multiple of <c>10**-ndigits</c>.
    /// With no argument or non-negative <c>ndigits</c>, returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1234.floor(-2)   # => 1200
    /// 1234.floor       # => 1234
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>self</c> rounded to the nearest multiple of <c>10**-ndigits</c>.
    /// With no argument or non-negative <c>ndigits</c>, returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1234.round(-2)   # => 1200
    /// 1250.round(-2)   # => 1300
    /// </code>
    /// </example>
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


    /// <summary>
    /// Returns the integer that is one greater than <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.next     # => 2
    /// (-3).next  # => -2
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>self</c> truncated toward zero to a multiple of <c>10**-ndigits</c>.
    /// With no argument or non-negative <c>ndigits</c>, returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1234.truncate(-2)    # => 1200
    /// 1234.truncate        # => 1234
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns a hash code for <c>self</c>, suitable for use as a Hash key.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.hash == 1.hash    # => true
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        var n = state.AsInteger(self);
        return RString.GetHashCode(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref n), sizeof(long)));
    }

    /// <summary>
    /// Returns a two-element array containing the integer quotient and modulus.
    /// </summary>
    /// <example>
    /// <code>
    /// 11.divmod(3)     # => [3, 2]
    /// (-11).divmod(3)  # => [-4, 1]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the value of <c>self</c> as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.to_f       # => 1.0
    /// (-5).to_f    # => -5.0
    /// </code>
    /// </example>
    [RubyDef("() -> Float")]

    public static MRubyValue ToF(MRubyState state, MRubyValue self) => (double)state.AsInteger(self);

    /// <summary>
    /// Returns the number of bits in the two's-complement representation of <c>self</c>,
    /// excluding the sign bit.
    /// </summary>
    /// <example>
    /// <code>
    /// 0.bit_length     # => 0
    /// 255.bit_length   # => 8
    /// (-256).bit_length # => 8
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the greatest common divisor of <c>self</c> and the argument (always non-negative).
    /// </summary>
    /// <example>
    /// <code>
    /// 12.gcd(8)      # => 4
    /// 18.gcd(24)     # => 6
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the least common multiple of <c>self</c> and the argument (always non-negative).
    /// Returns 0 if either operand is 0.
    /// </summary>
    /// <example>
    /// <code>
    /// 4.lcm(6)      # => 12
    /// 18.lcm(24)    # => 72
    /// </code>
    /// </example>
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
            if (width == long.MinValue || -width > numericShiftWidthMax)
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
            if (width > numericShiftWidthMax ||
                val > long.MaxValue >> (int)width)
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

    static MRubyValue PrepareIntRounding(MRubyState state, MRubyValue x)
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

    static void IntDivMod(MRubyState state, long x, long y, out long divp, out long modp)
    {
        if (y == 0)
        {
            RaiseDivideByZeroError(state);
            Unsafe.SkipInit(out divp);
            Unsafe.SkipInit(out modp);
            return;
        }

        if (x == int.MinValue && y == -1)
        {
            RaiseIntegerOverflowError(state, "division"u8);
            Unsafe.SkipInit(out divp);
            Unsafe.SkipInit(out modp);
            return;
        }

        var div = x / y;
        var mod = x - div * y;

        if ((x ^ y) < 0 && x != div * y)
        {
            mod += y;
            div -= 1;
        }
        divp = div;
        modp = mod;
    }
}
