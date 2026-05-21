using System;
using System.Collections.Generic;

namespace MRubyCS.StdLib;

[RubyClass("Array", TypeParameters = "Elem")]
static class ArrayMembers
{
    [RubyDef("(*Elem) -> Array[Elem]")]
    public static MRubyValue Create(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        var array = state.NewArray(args);
        array.Class = self.As<RClass>();
        return array;
    }

    [RubyDef("(int) -> Elem | (int, int) -> Array[Elem]? | (Range[int]) -> Array[Elem]?")]
    public static MRubyValue OpAref(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        // if (argc )

        var index = state.GetArgumentAt(0);
        switch (argc)
        {
            case 1:
                switch (index.VType)
                {
                    case MRubyVType.Range:
                        if (index.As<RRange>().Calculate(
                                array.Length,
                                true,
                                out var calculatedIndex,
                                out var calculatedLength) == RangeCalculateResult.Ok)
                        {
                            return array.SubSequence(calculatedIndex, calculatedLength);
                        }
                        return MRubyValue.Nil;
                    case MRubyVType.Float:
                        return array[(int)index.FloatValue];
                    default:
                        return array[(int)state.AsInteger(index)];
                }
            case 2:
                var i = (int)state.AsInteger(index);
                var length = state.GetArgumentAsIntegerAt(1);
                if (i < 0) i += array.Length;
                if (i < 0 || array.Length < i) return MRubyValue.Nil;
                if (length < 0) return MRubyValue.Nil;
                if (array.Length == i) return state.NewArray(0);
                if (length > array.Length - i) length = array.Length - i;
                return array.SubSequence(i, (int)length);
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                return default;
        }
    }

    [RubyDef("(int, Elem) -> Elem | (int, int, Elem) -> Elem | (Range[int], Elem) -> Elem")]
    public static MRubyValue OpAset(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 2:
                var key = state.GetArgumentAt(0);
                var val = state.GetArgumentAt(1);
                if (key.Object is RRange range)
                {
                    switch (range.Calculate(array.Length, false, out var calculatedIndex, out var calculatedLength))
                    {
                        case RangeCalculateResult.TypeMismatch:
                            array[(int)state.AsInteger(key)] = val;
                            break;
                        case RangeCalculateResult.Ok:
                            state.SpliceArray(array, calculatedIndex, calculatedLength, val);
                            break;
                        case RangeCalculateResult.Out:
                            state.Raise(Names.RangeError, $"`{state.Stringify(key)}` out of range");
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    array[(int)state.AsInteger(key)] = val;
                }
                return val;
            case 3:
                // a[n,m] = v
                var n = state.GetArgumentAsIntegerAt(0);
                var m = state.GetArgumentAsIntegerAt(1);
                var v = state.GetArgumentAt(2);
                state.SpliceArray(array, (int)n, (int)m, v);
                return v;
            default:
                state.RaiseArgumentNumberError(argc, 2, 3);
                return default;
        }
    }

    [RubyDef("(Array[Elem]) -> self")]
    public static MRubyValue Replace(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var other = state.GetArgumentAsArrayAt(0);

        other.ReplaceTo(array);
        return self;
    }

    [RubyDef("(*Elem) -> self")]
    public static MRubyValue Push(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        var args = state.GetRestArgumentsAfter(0);

        var start = array.Length;
        array.MakeModifiable(start + args.Length, true);

        var span = array.AsSpan(start, args.Length);
        args.CopyTo(span);
        return self;
    }

    [RubyDef("() -> Elem")]
    public static MRubyValue Pop(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        array.TryPop(out var result);
        return result;
    }

    [RubyDef("(Array[Elem]) -> Array[Elem]")]
    public static MRubyValue Plus(MRubyState state, MRubyValue self)
    {
        var a1 = self.As<RArray>();
        var a2 = state.GetArgumentAsArrayAt(0);

        var result = state.NewArray(a1.Length + a2.Length);
        result.MakeModifiable(a1.Length + a2.Length, true);

        a1.AsSpan().CopyTo(result.AsSpan());
        a2.AsSpan().CopyTo(result.AsSpan(a1.Length));
        return result;
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        return array.Length;
    }

    [RubyDef("() -> bool")]
    public static MRubyValue Empty(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        return array.Length <= 0;
    }

    [RubyDef("() -> Elem | (int) -> Array[Elem]")]
    public static MRubyValue First(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case <= 0:
                return array.Length <= 0 ? MRubyValue.Nil : array[0];
            case > 1:
                state.RaiseArgumentNumberError(argc, 0, 1);
                break;
        }

        var size = state.GetArgumentAsIntegerAt(0);
        if (size < 0)
        {
            state.Raise(Names.ArgumentError, "nagative array size"u8);
        }

        var subSequence = array.SubSequence(0, (int)size);
        return subSequence;
    }

    [RubyDef("() -> Elem | (int) -> Array[Elem]")]
    public static MRubyValue Last(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case <= 0:
                return array.Length <= 0 ? MRubyValue.Nil : array[^1];
            case > 1:
                state.RaiseArgumentNumberError(argc, 0, 1);
                break;
        }

        var size = state.GetArgumentAsIntegerAt(0);
        if (size < 0)
        {
            state.Raise(Names.ArgumentError, "nagative array size"u8);
        }
        var subSequence = array.SubSequence(array.Length - (int)size, (int)size);
        return subSequence;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);
        if (arg.Object is not RArray other ||
            array.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (array == other)
        {
            return MRubyValue.True;
        }

        var span1 = array.AsSpan();
        var span2 = other.AsSpan();
        for (var i = 0; i < span1.Length; i++)
        {
            var elementEquals = state.Send(span1[i], Names.OpEq, span2[i]);
            if (elementEquals.Falsy)
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eql(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);
        if (arg.Object is not RArray other ||
            array.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (array == other)
        {
            return MRubyValue.True;
        }

        var span1 = array.AsSpan();
        var span2 = other.AsSpan();
        for (var i = 0; i < span1.Length; i++)
        {
            var elementEquals = state.Send(span1[i], Names.QEql, span2[i]);
            if (elementEquals.Falsy)
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    }

    [RubyDef("(Array[Elem]) -> Array[Elem]")]
    public static MRubyValue OpAdd(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var other = state.GetArgumentAt(0);
        state.EnsureValueType(other, MRubyVType.Array);

        var otherArray = other.As<RArray>();

        var newLength = array.Length + otherArray.Length;
        var newArray = state.NewArray(newLength);
        newArray.MakeModifiable(newLength, true);

        var span = newArray.AsSpan();
        array.AsSpan().CopyTo(span);
        otherArray.AsSpan().CopyTo(span[array.Length..]);
        return newArray;
    }

    [RubyDef("(Integer) -> Array[Elem] | (String) -> String")]
    public static MRubyValue Times(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);

        if (arg.Object is RString separator)
        {
            return JoinArray(state, array, separator, new Stack<RArray>());
        }

        var times = state.AsInteger(arg);
        if (times == 0)
        {
            return state.NewArray();
        }
        if (times < 0)
        {
            state.Raise(Names.ArgumentError, "nagative argument"u8);
        }
        else if (RArray.MaxLength / times < array.Length)
        {
            state.Raise(Names.ArgumentError, "array size too big"u8);
        }

        var source = array.AsSpan();
        var newLength = array.Length * (int)times;
        var result = state.NewArray(newLength);
        result.MakeModifiable(newLength, true);
        for (var i = 0; i < times; i++)
        {
            source.CopyTo(result.AsSpan(array.Length * i));
        }
        return result;
    }

    [RubyDef("() -> Array[Elem]")]
    public static MRubyValue Reverse(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var result = state.NewArray(array.Length);
        array.CopyTo(result);
        result.AsSpan().Reverse();
        return result;
    }

    [RubyDef("() -> self")]
    public static MRubyValue ReverseBang(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var span = array.AsSpan();

        var left = 0;
        var right = span.Length - 1;
        while (left < right)
        {
            (span[left], span[right]) = (span[right], span[left]);
            left++;
            right--;
        }
        return self;
    }

    [RubyDef("(int) -> Elem")]
    public static MRubyValue DeleteAt(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);
        var index = state.AsInteger(arg);
        return array.DeleteAt((int)index);
    }

    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var result = state.NewString("["u8);
        if (state.IsRecursiveCalling(Names.ToS, self))
        {
            result.Concat("...]"u8);
        }
        else
        {
            var first = true;
            foreach (var x in array.AsSpan())
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }
                first = false;

                var value = state.Stringify(state.Send(x, Names.ToS));
                result.Concat(value);
            }
            result.Concat("]"u8);
        }
        return result;
    }


    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var result = state.NewString("["u8);
        if (state.IsRecursiveCalling(Names.Inspect, self))
        {
            result.Concat("...]"u8);
        }
        else
        {
            var first = true;
            foreach (var x in array.AsSpan())
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }
                first = false;

                var value = state.Stringify(state.Send(x, Names.Inspect));
                result.Concat(value);
            }
            result.Concat("]"u8);
        }
        return result;
    }

    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue Index(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);
        var span = array.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            if (state.ValueEquals(span[i], arg))
            {
                return i;
            }
        }
        return MRubyValue.Nil;
    }

    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue RIndex(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);
        var span = array.AsSpan();
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (state.ValueEquals(span[i], arg))
            {
                return i;
            }
        }
        return MRubyValue.Nil;
    }

    [RubyDef("(?String) -> String")]
    public static MRubyValue Join(MRubyState state, MRubyValue self)
    {
        RString? separator = null;
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            state.EnsureValueType(arg0, MRubyVType.String);
            separator = arg0.As<RString>();
        }

        var array = self.As<RArray>();
        var result = JoinArray(state, array, separator!, new Stack<RArray>());
        return result;
    }

    [RubyDef("() -> self")]
    public static MRubyValue Clear(MRubyState state, MRubyValue self)
    {
        self.As<RArray>().Clear();
        return self;
    }

    [RubyDef("(*Array[Elem]) -> self")]
    public static MRubyValue Concat(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            state.EnsureValueType(arg, MRubyVType.Array);
        }
        foreach (var arg in args)
        {
            array.Concat(arg.As<RArray>());
        }
        return self;
    }

    [RubyDef("() -> Elem | (Integer) -> Array[Elem]")]
    public static MRubyValue Shift(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            var result = array.Shift((int)state.AsInteger(arg0));
            return result;
        }
        return array.Shift();
    }

    [RubyDef("(*Elem) -> self")]
    public static MRubyValue Unshift(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);
        var newItems = state.GetRestArgumentsAfter(0);
        array.Unshift(newItems);
        return self;
    }


    static int AsIndex(MRubyState state, MRubyValue index)
    {
        if (index.IsInteger)
        {
            return (int)index.IntegerValue;
        }
        return (int)state.GetArgumentAsIntegerAt(0);
    }

    static RString JoinArray(MRubyState state, RArray array, RString separator, Stack<RArray> stack)
    {
        var span = array.AsSpan();

        // check recursive
        foreach (var x in stack)
        {
            if (x == array)
            {
                state.Raise(Names.ArgumentError, "recursive array join"u8);
            }
        }

        stack.Push(array);

        var result = state.NewString(array.Length * 2);
        var first = true;
        foreach (var x in span)
        {
            if (!first && separator != null)
            {
                result.Concat(separator);
            }
            first = false;

            if (x.Object is RString str)
            {
                result.Concat(str);
            }
            else if (x.Object is RArray nested)
            {
                var joinedValue = JoinArray(state, nested, separator!, stack);
                result.Concat(joinedValue);
            }
            else
            {
                result.Concat(state.Stringify(x));
            }
        }

        stack.Pop();
        return result;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue InternalEq(MRubyState state, MRubyValue self)
    {
        var arg = state.GetArgumentAt(0);
        if (self == arg)
        {
            return MRubyValue.True;
        }

        var array = self.As<RArray>();
        if (arg.VType != MRubyVType.Array)
        {
            return MRubyValue.False;
        }

        if (arg.Object is RArray other && other.Length != array.Length)
        {
            return MRubyValue.False;
        }
        return arg;
    }

    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue InternalCmp(MRubyState state, MRubyValue self)
    {
        var arg = state.GetArgumentAt(0);
        if (self == arg) return 0;
        if (arg.VType != MRubyVType.Array)
        {
            return MRubyValue.Nil;
        }
        return arg;
    }

    // internal method to convert multi-value to single value
    [RubyDef("() -> Elem")]
    public static MRubyValue InternalSValue(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        return array.Length switch
        {
            0 => MRubyValue.Nil,
            1 => array[0],
            _ => self
        };
    }

    [RubyDef("() -> Array[Elem]")]
    public static MRubyValue Deconstruct(MRubyState state, MRubyValue self) => self;
}
