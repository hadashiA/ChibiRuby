using System;
using System.Buffers.Text;

namespace MRubyCS.StdLib;

[RubyClass("Float", Superclass = "Numeric")]
static class FloatMembers
{
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

    [RubyDef("() -> String")]

    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return Format(state, f);
    }

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

    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue OpPow(MRubyState state, MRubyValue self)
    {
        var a = self.FloatValue;
        var b = state.AsFloat(state.GetArgumentAt(0));
        return Math.Pow(a, b);
    }

    [RubyDef("() -> Float")]

    public static MRubyValue OpNeg(MRubyState state, MRubyValue self)
    {
        return -self.FloatValue;
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpAnd(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 & v2);
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpOr(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 | v2);
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpXor(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 ^ v2);
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpLshift(MRubyState state, MRubyValue self)
    {
        var width = state.AsInteger(state.GetArgumentAt(0));
        return FloShift(state, self, width);
    }

    [RubyDef("(Integer) -> Integer")]
    public static MRubyValue OpRshift(MRubyState state, MRubyValue self)
    {
        var width = state.AsInteger(state.GetArgumentAt(0));
        if (width == long.MinValue) return FloShift(state, self, -64);
        return FloShift(state, self, -width);
    }

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

    [RubyDef("() -> bool")]

    public static MRubyValue QNan(MRubyState state, MRubyValue self)
    {
        return double.IsNaN(self.FloatValue);
    }

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

    [RubyDef("() -> bool")]

    public static MRubyValue QFinite(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return !double.IsInfinity(f) && !double.IsNaN(f);
    }

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

    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Ceil(MRubyState state, MRubyValue self)
    {
        return FloatRounding(state, self, Math.Ceiling);
    }

    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Floor(MRubyState state, MRubyValue self)
    {
        return FloatRounding(state, self, Math.Floor);
    }

    [RubyDef("() -> Float")]

    public static MRubyValue ToF(MRubyState state, MRubyValue self)
    {
        return self;
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return f.GetHashCode();
    }
    //
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
    [RubyDef("(?Integer) -> Numeric")]
    public static MRubyValue Truncate(MRubyState state, MRubyValue self)
    {
        return FloatRounding(state, self, Math.Truncate);
    }

    [RubyDef("(Numeric) -> Float")]
    public static MRubyValue Quo(MRubyState state, MRubyValue self)
    {
        var x = self.FloatValue;
        var y = state.AsFloat(state.GetArgumentAt(0));
        return x / y;
    }

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

    [RubyDef("() -> String")]

    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var f = self.FloatValue;
        return Format(state, f);
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue OpRev(MRubyState state, MRubyValue self)
    {
        var v1 = ValueInt64(state, self);
        return Int64Value(state, ~v1);
    }

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
