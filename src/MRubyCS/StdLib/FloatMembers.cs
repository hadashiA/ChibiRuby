using System;
using System.Buffers.Text;

namespace MRubyCS.StdLib;

/// <summary>
/// IEEE 754 double-precision floating-point number. Created by literals with a
/// decimal point or exponent (e.g. <c>1.5</c>, <c>2e10</c>), or by mixed
/// arithmetic with <c>Integer</c>. Values are immutable; <c>Float</c>
/// implements <c>Comparable</c> and supports the usual arithmetic operators.
/// </summary>
[RubyClass("Float", Superclass = "Numeric")]
static class FloatMembers
{
    /// <summary>
    /// Returns <c>self</c> truncated to an <c>Integer</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 3.7.to_i      # => 3
    /// (-3.7).to_i   # => -3
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue ToI(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        state.EnsureExactValue(f);
        if (!IsFixableFloatValue(f))
        {
            state.Raise(Names.RangeError, "integer overflow in to_i"u8);
        }

        if (f > 0.0) return (long)Math.Floor(f);
        if (f < 0.0) return (long)Math.Ceiling(f);
        return state.NewIntegerFlex((long)f);
    }

    /// <summary>
    /// Returns the string representation of <c>self</c>.
    /// Special values are formatted as <c>"Infinity"</c>, <c>"-Infinity"</c>, or <c>"NaN"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5.to_s         # => "1.5"
    /// (1.0/0).to_s     # => "Infinity"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]

    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return Format(state, f);
    }

    /// <summary>
    /// Returns <c>self</c> modulo the argument as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 6.5 % 2     # => 0.5
    /// 6.0 % 2.5   # => 1.0
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue Mod(MRubyState state, MRubyValue self)
    {
        var x = state.AsFloat(self);
        var y = state.GetArgumentAsFloatAt(0);

        if (double.IsNaN(y))
        {
            return double.NaN;
        }

        if (y == 0.0)
        {
            state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
        }

        if (double.IsInfinity(y) && !double.IsInfinity(x))
        {
            return x;
        }
        return x % y;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> equals the argument numerically.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.0 == 1       # => true
    /// 1.0 == 1.0     # => true
    /// 1.0 == "1"     # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        // Console.WriteLine("Float OpEq called");
        var x = self.FloatValue;
        var y = state.GetArgumentAt(0);
        if (y.IsInteger)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return x == (double)y.IntegerValue;
        }
        ;
        if (y.IsFloat)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return x == y.FloatValue;
        }
        return MRubyValue.False;
    }

    /// <summary>
    /// Returns the sum of <c>self</c> and the argument as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5 + 2.5     # => 4.0
    /// 1.5 + 1       # => 2.5
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue OpAdd(MRubyState state, MRubyValue self)
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.AsFloat(arg)
        };
        return a + b;
    }

    /// <summary>
    /// Returns the difference of <c>self</c> minus the argument as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 2.5 - 1.0     # => 1.5
    /// 5.0 - 2       # => 3.0
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue OpSub(MRubyState state, MRubyValue self)
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.AsFloat(arg)
        };
        return a - b;
    }

    /// <summary>
    /// Returns the product of <c>self</c> and the argument as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 2.0 * 3.0     # => 6.0
    /// 1.5 * 2       # => 3.0
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue OpMul(MRubyState state, MRubyValue self)
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.AsFloat(arg)
        };
        return a * b;
    }

    /// <summary>
    /// Returns the quotient of <c>self</c> divided by the argument as a <c>Float</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 10.0 / 4      # => 2.5
    /// 1.0 / 0       # => Infinity
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue OpDiv(MRubyState state, MRubyValue self)
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.AsFloat(arg)
        };
        return a / b;
    }

    /// <summary>
    /// Returns <c>self</c> raised to the power of the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 2.0 ** 3      # => 8.0
    /// 9.0 ** 0.5    # => 3.0
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue OpPow(MRubyState state, MRubyValue self)
    {
        var a = self.FloatValue;
        var b = state.AsFloat(state.GetArgumentAt(0));
        return Math.Pow(a, b);
    }

    /// <summary>
    /// Unary minus; returns <c>self</c> negated.
    /// </summary>
    /// <example>
    /// <code>
    /// -1.5    # => -1.5
    /// -(-2.0) # => 2.0
    /// </code>
    /// </example>
    [RubyDef("() -> Float")]

    public static MRubyValue OpNeg(MRubyState state, MRubyValue self)
    {
        return -self.FloatValue;
    }

    /// <summary>
    /// Returns the bitwise AND of <c>self</c> (converted to <c>Integer</c>) and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 12.0 &amp; 10    # => 8
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpAnd(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 & v2);
    }

    /// <summary>
    /// Returns the bitwise OR of <c>self</c> (converted to <c>Integer</c>) and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 12.0 | 10    # => 14
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpOr(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 | v2);
    }

    /// <summary>
    /// Returns the bitwise exclusive OR of <c>self</c> (converted to <c>Integer</c>) and the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 12.0 ^ 10    # => 6
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpXor(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 ^ v2);
    }

    /// <summary>
    /// Returns <c>self</c> shifted left by the given number of bits, converted as needed.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.0 &lt;&lt; 3    # => 8
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpLshift(MRubyState state, MRubyValue self)
    {
        var width = state.AsInteger(state.GetArgumentAt(0));
        return FloShift(state, self, width);
    }

    /// <summary>
    /// Returns <c>self</c> shifted right by the given number of bits, converted as needed.
    /// </summary>
    /// <example>
    /// <code>
    /// 16.0 &gt;&gt; 2    # => 4
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpRshift(MRubyState state, MRubyValue self)
    {
        var width = state.AsInteger(state.GetArgumentAt(0));
        if (width == long.MinValue) return FloShift(state, self, -64);
        return FloShift(state, self, -width);
    }

    /// <summary>
    /// Returns a two-element array containing the floor quotient and the modulus.
    /// </summary>
    /// <example>
    /// <code>
    /// 11.5.divmod(3.25)    # => [3, 1.75]
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Array[Numeric]")]

    public static MRubyValue DivMod(MRubyState state, MRubyValue self)
    {
        var x = state.AsFloat(self);
        var y = state.GetArgumentAt(0);
        MRubyValue a, b;
        FloatDivMod(state, x, state.AsFloat(y), out var div, out var mod);
        if (!IsFixableFloatValue(div))
        {
            a = div;
        }
        else
        {
            a = (long)div;
        }

        b = mod;
        return state.NewArray(a, b);
    }

    /// <summary>
    /// Returns the absolute value of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 3.5.abs      # => 3.5
    /// (-3.5).abs   # => 3.5
    /// </code>
    /// </example>
    [RubyDef("() -> Float")]

    public static MRubyValue Abs(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        if (f < 0.0f)
        {
            return -f;
        }
        return self;
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> is a "Not a Number" value, otherwise <c>false</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// (0.0/0.0).nan?    # => true
    /// 1.5.nan?          # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]

    public static MRubyValue QNan(MRubyState state, MRubyValue self)
    {
        return double.IsNaN(self.FloatValue);
    }

    /// <summary>
    /// Returns <c>true</c> when the argument is also a <c>Float</c> with the same value.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.0.eql?(1.0)    # => true
    /// 1.0.eql?(1)      # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue QEql(MRubyState state, MRubyValue self)
    {
        var arg = state.GetArgumentAt(0);
        if (!arg.IsFloat)
        {
            return MRubyValue.False;
        }
        var x = self.FloatValue;
        var y = arg.FloatValue;
        return x.Equals(y);
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is neither infinite nor NaN.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5.finite?         # => true
    /// (1.0/0).finite?     # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]

    public static MRubyValue QFinite(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return !double.IsInfinity(f) && !double.IsNaN(f);
    }

    /// <summary>
    /// Returns <c>1</c> for positive infinity, <c>-1</c> for negative infinity, or <c>nil</c> otherwise.
    /// </summary>
    /// <example>
    /// <code>
    /// (1.0/0).infinite?     # => 1
    /// (-1.0/0).infinite?    # => -1
    /// 1.5.infinite?         # => nil
    /// </code>
    /// </example>
    [RubyDef("() -> Integer?")]

    public static MRubyValue QInfinite(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        if (double.IsPositiveInfinity(f))
            return 1;
        if (double.IsNegativeInfinity(f))
            return -1;
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns the smallest number greater than or equal to <c>self</c>, with optional precision.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.2.ceil           # => 2
    /// 1.234.ceil(2)      # => 1.24
    /// (-1.2).ceil        # => -1
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Ceil(MRubyState state, MRubyValue self)
    {
        return FloatRounding(state, self, Math.Ceiling);
    }

    /// <summary>
    /// Returns the largest number less than or equal to <c>self</c>, with optional precision.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.8.floor          # => 1
    /// 1.234.floor(2)     # => 1.23
    /// (-1.2).floor       # => -2
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Floor(MRubyState state, MRubyValue self)
    {
        return FloatRounding(state, self, Math.Floor);
    }

    /// <summary>
    /// Returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5.to_f    # => 1.5
    /// </code>
    /// </example>
    [RubyDef("() -> Float")]

    public static MRubyValue ToF(MRubyState state, MRubyValue self)
    {
        return self;
    }

    /// <summary>
    /// Returns a hash code for <c>self</c>, suitable for use as a Hash key.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5.hash == 1.5.hash    # => true
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return f.GetHashCode();
    }
    //
    /// <summary>
    /// Returns <c>self</c> rounded to the nearest value, with optional digits of precision.
    /// Ties round away from zero.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5.round          # => 2
    /// 1.234.round(2)     # => 1.23
    /// 1.235.round(2)     # => 1.24
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Round(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        var ndigits = 0;

        var argc = state.GetArgumentCount();
        if (argc > 0)
        {
            var arg = state.GetArgumentAt(0);
            if (arg.IsInteger)
            {
                ndigits = (int)arg.IntegerValue;
            }
            else
            {
                state.Raise(Names.TypeError, "can't convert to integer"u8);
            }
        }

        if (ndigits == 0)
        {
            state.EnsureExactValue(f);
            var result = Math.Round(f, MidpointRounding.AwayFromZero);
            if (IsFixableFloatValue(result))
            {
                return (long)result;
            }
            return result;
        }
        else if (ndigits > 0)
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                return self;
            }
            if (ndigits > 15) ndigits = 15;
            var result = Math.Round(f, ndigits, MidpointRounding.AwayFromZero);
            return result;
        }
        else
        {
            state.EnsureExactValue(f);
            var pow = Math.Pow(10, -ndigits);
            var result = Math.Round(f / pow, MidpointRounding.AwayFromZero) * pow;
            if (IsFixableFloatValue(result))
            {
                return (long)result;
            }
            return result;
        }
    }
    //
    /// <summary>
    /// Returns <c>self</c> truncated toward zero, with optional digits of precision.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.7.truncate         # => 1
    /// 1.234.truncate(2)    # => 1.23
    /// (-1.7).truncate      # => -1
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Truncate(MRubyState state, MRubyValue self)
    {
        return FloatRounding(state, self, Math.Truncate);
    }

    /// <summary>
    /// Returns the floating-point quotient of <c>self</c> divided by the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 5.0.quo(2)     # => 2.5
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue Quo(MRubyState state, MRubyValue self)
    {
        var x = self.FloatValue;
        var y = state.AsFloat(state.GetArgumentAt(0));
        return x / y;
    }

    /// <summary>
    /// Returns the integer floor of <c>self</c> divided by the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 11.5.div(3)    # => 3
    /// 11.5.div(3.5)  # => 3
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Integer")]
    public static MRubyValue Div(MRubyState state, MRubyValue self)
    {
        var x = self.FloatValue;
        var y = state.AsFloat(state.GetArgumentAt(0));
        if (y == 0.0)
        {
            state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
        }
        var result = Math.Floor(x / y);
        if (IsFixableFloatValue(result))
        {
            return (long)result;
        }
        return new MRubyValue(result);
    }

    /// <summary>
    /// Returns a string representation of <c>self</c> for debugging.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5.inspect     # => "1.5"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]

    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return Format(state, f);
    }

    /// <summary>
    /// Returns the bitwise complement of <c>self</c> after converting it to <c>Integer</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// ~1.0    # => -2
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue OpRev(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        return Int64Value(state, ~v1);
    }

    /// <summary>
    /// Returns <c>-1</c>, <c>0</c>, or <c>1</c> when <c>self</c> is less than, equal to, or greater than the argument.
    /// Returns <c>nil</c> if the argument is not comparable or either side is NaN.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.5 &lt;=&gt; 2.0    # => -1
    /// 1.5 &lt;=&gt; 1.5    # => 0
    /// 1.5 &lt;=&gt; "x"    # => nil
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue OpCmp(MRubyState state, MRubyValue self)
    {
        var x = self.FloatValue;
        var arg = state.GetArgumentAt(0);

        double y;
        if (arg.IsFloat)
        {
            y = arg.FloatValue;
        }
        else if (arg.IsInteger)
        {
            y = (double)arg.IntegerValue;
        }
        else
        {
            return MRubyValue.Nil;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return MRubyValue.Nil;
        }

        if (x < y) return -1;
        if (x > y) return 1;
        return 0;
    }

    static void FloatDivMod(MRubyState state, double x, double y, out double divp, out double modp)
    {
        double div, mod;

        if (double.IsNaN(y))
        {
            /* y is NaN so all results are NaN */
            div = mod = y;
            goto exit;
        }
        if (y == 0.0)
        {
            IntegerMembers.RaiseDivideByZeroError(state);
        }
        if (double.IsInfinity(y) && !double.IsInfinity(x))
        {
            mod = x;
        }
        else
        {
            mod = (x % y);
        }
        if (double.IsInfinity(x) && !double.IsInfinity(y))
        {
            div = x;
        }
        else
        {
            div = (x - mod) / y;
            div = Math.Round(div);
        }
        if (div == 0) div = 0.0;
        if (mod == 0) mod = 0.0;
        if (y * mod < 0)
        {
            mod += y;
            div -= 1.0;
        }
        exit:
        modp = mod;
        divp = div;
    }

    static bool IsFixableFloatValue(double f) =>
        f is >= -9223372036854775808.0 and < 9223372036854775808.0;

    static RString Format(MRubyState state, double f)
    {
        if (double.IsPositiveInfinity(f))
        {
            return state.NewString("Infinity"u8);
        }
        if (double.IsNegativeInfinity(f))
        {
            return state.NewString("-Infinity"u8);
        }

        if (double.IsNaN(f))
        {
            return state.NewString("NaN"u8);
        }

        int bytesWritten;
        Span<byte> destination = stackalloc byte[64];
        Utf8Formatter.TryFormat(f, destination, out bytesWritten);
        return state.NewString(destination.Slice(0, bytesWritten));
    }

    static long ValueInt64(MRubyState state, MRubyValue x)
    {
        switch (x.VType)
        {
            case MRubyVType.Integer:
                return x.IntegerValue;
            case MRubyVType.Float:
                var f = x.FloatValue;
                if (f is >= long.MinValue and <= long.MaxValue)
                    return (long)f;
                break;
        }
        state.Raise(Names.TypeError, "cannot convert to Integer"u8);
        return 0;
    }

    static MRubyValue Int64Value(MRubyState state, long v)
    {
        if (v >= int.MinValue && v <= int.MaxValue)
        {
            return v;
        }
        state.Raise(Names.RangeError, "bit operation"u8);
        return MRubyValue.Nil;
    }

    static MRubyValue FloShift(MRubyState state, MRubyValue x, long width)
    {
        if (width == 0)
        {
            return x;
        }

        var f = x.FloatValue;
        double result;

        if (width > 0)
        {
            if (width >= 64) result = 0.0;
            else result = f * Math.Pow(2, width);
        }
        else
        {
            if (width <= -64) result = 0.0;
            else result = f / Math.Pow(2, -width);
        }

        if (IsFixableFloatValue(result))
        {
            return (long)result;
        }
        return result;
    }

    static MRubyValue FloatRounding(MRubyState state, MRubyValue num, Func<double, double> func)
    {
        var f = num.FloatValue;
        var ndigits = 0;
        const int fprec = 15;

        if (state.TryGetArgumentAt(0, out var arg))
        {
            if (!arg.IsInteger)
            {
                state.Raise(Names.TypeError, "can't convert to integer"u8);
            }
            ndigits = (int)arg.IntegerValue;
        }

        if (ndigits == 0)
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                state.EnsureExactValue(f);
            }
            var result = func(f);
            if (IsFixableFloatValue(result))
            {
                return (long)result;
            }
            return result;
        }
        else if (ndigits > 0)
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                return num;
            }
            if (ndigits > fprec) ndigits = fprec;
            var pow = Math.Pow(10, ndigits);
            return func(f * pow) / pow;
        }
        else
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                state.EnsureExactValue(f);
            }
            if (ndigits < -fprec) ndigits = -fprec;
            var pow = Math.Pow(10, -ndigits);
            var result = func(f / pow) * pow;
            if (IsFixableFloatValue(result))
            {
                return (long)result;
            }
            return result;
        }
    }
}
