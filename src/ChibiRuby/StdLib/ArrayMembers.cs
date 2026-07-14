using System;
using System.Collections.Generic;

namespace ChibiRuby.StdLib;

/// <summary>
/// Ordered, integer-indexed collection of objects. The base concrete container
/// in Ruby -- supports indexing, slicing, iteration via <c>each</c>, and
/// conversion via <c>to_a</c>. Mutable; many in-place methods end in <c>!</c>.
/// </summary>
[RubyClass("Array", TypeParameters = "Elem")]
static class ArrayMembers
{
    /// <summary>
    /// Creates a new <c>Array</c> from the given elements.
    /// </summary>
    /// <example>
    /// <code>
    /// Array.new           # => []
    /// Array[1, 2, 3]      # => [1, 2, 3]
    /// </code>
    /// </example>
    [RubyDef("(*Elem) -> Array[Elem]")]
    public static MRubyValue Create(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        var array = state.NewArray(args);
        array.Class = self.As<RClass>();
        return array;
    }

    /// <summary>
    /// Element reference <c>[]</c>. Returns the element at the given index, a subarray for <c>(start, length)</c> or a range, or <c>nil</c> when out of range.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3, 4]
    /// a[0]        # => 1
    /// a[-1]       # => 4
    /// a[1, 2]     # => [2, 3]
    /// a[0..1]     # => [1, 2]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Element assignment <c>[]=</c>. Sets the element at the given index, or replaces a range of elements.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a[0] = 9        # => 9, a == [9, 2, 3]
    /// a[1, 2] = [0]   # a == [9, 0]
    /// a[0..1] = [:x]  # a == [:x]
    /// </code>
    /// </example>
    [RubyDef("(int, Elem) -> Elem | (int, int, Elem) -> Elem | (Range[int], Elem) -> Elem")]
    public static MRubyValue OpAset(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        var args = state.GetArgumentsSpan();
        switch (args.Length)
        {
            case 2:
                var key = args[0];
                var val = args[1];
                if (key.IsFixnum)
                {
                    array.Set((int)key.FixnumValue, val);
                    return val;
                }
                if (key.Object is RRange range)
                {
                    switch (range.Calculate(array.Length, false, out var calculatedIndex, out var calculatedLength))
                    {
                        case RangeCalculateResult.TypeMismatch:
                            array.Set((int)state.AsInteger(key), val);
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
                    array.Set((int)state.AsInteger(key), val);
                }
                return val;
            case 3:
                // a[n,m] = v
                var nArg = args[0];
                var mArg = args[1];
                var n = nArg.IsFixnum ? nArg.FixnumValue : state.AsInteger(nArg);
                var m = mArg.IsFixnum ? mArg.FixnumValue : state.AsInteger(mArg);
                var v = args[2];
                state.SpliceArray(array, (int)n, (int)m, v);
                return v;
            default:
                state.RaiseArgumentNumberError(args.Length, 2, 3);
                return default;
        }
    }

    /// <summary>
    /// Replaces the contents of <c>self</c> with the contents of the given array and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.replace([10, 20])   # => [10, 20]
    /// </code>
    /// </example>
    [RubyDef("(Array[Elem]) -> self")]
    public static MRubyValue Replace(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var other = state.GetArgumentAsArrayAt(0);

        other.ReplaceTo(array);
        return self;
    }

    /// <summary>
    /// Appends the given element(s) to <c>self</c> and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2]
    /// a.push(3)        # => [1, 2, 3]
    /// a.push(4, 5)     # => [1, 2, 3, 4, 5]
    /// </code>
    /// </example>
    [RubyDef("(*Elem) -> self")]
    public static MRubyValue Push(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        var argc = state.GetArgumentCount();
        if (argc == 0)
        {
            return self;
        }
        if (argc == 1)
        {
            array.Push(state.GetArgumentAt(0));
            return self;
        }

        var args = state.GetRestArgumentsAfter(0);

        var start = array.Length;
        array.MakeModifiable(start + args.Length, true);

        var span = array.AsSpan(start, args.Length);
        args.CopyTo(span);
        return self;
    }

    /// <summary>
    /// Removes and returns the last element of <c>self</c>, or <c>nil</c> when empty.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.pop        # => 3
    /// a            # => [1, 2]
    /// [].pop       # => nil
    /// </code>
    /// </example>
    [RubyDef("() -> Elem")]
    public static MRubyValue Pop(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        array.TryPop(out var result);
        return result;
    }

    /// <summary>
    /// Returns a new array built by concatenating <c>self</c> with the given array.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2].plus([3, 4])    # => [1, 2, 3, 4]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the number of elements in <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].size       # => 3
    /// [].size              # => 0
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        return array.Length;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> contains no elements, <c>false</c> otherwise.
    /// </summary>
    /// <example>
    /// <code>
    /// [].empty?        # => true
    /// [1].empty?       # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue Empty(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        return array.Length <= 0;
    }

    /// <summary>
    /// Returns the first element of <c>self</c>, or the first <c>n</c> elements when an argument is given.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].first       # => 1
    /// [1, 2, 3].first(2)    # => [1, 2]
    /// [].first              # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the last element of <c>self</c>, or the last <c>n</c> elements when an argument is given.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].last        # => 3
    /// [1, 2, 3].last(2)     # => [2, 3]
    /// [].last               # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Equality <c>==</c>. Returns <c>true</c> when both arrays have the same length and corresponding elements compare equal with <c>==</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2] == [1, 2]    # => true
    /// [1, 2] == [1, 2, 3] # => false
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> when both arrays have the same length and corresponding elements are <c>eql?</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2].eql?([1, 2])     # => true
    /// [1, 2].eql?([1.0, 2.0]) # => false
    /// </code>
    /// </example>
    /// <summary>
    /// Returns a content-based hash code; equal (<c>eql?</c>) arrays have equal hashes.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2].hash == [1, 2].hash    # => true
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        // Same algorithm as Enumerable#hash in lib.rb (12347 seed + __update_hash),
        // implemented natively because array keys make Hash probe this per operation.
        var span = self.As<RArray>().AsSpan();
        var h = 12347;
        for (var i = 0; i < span.Length; i++)
        {
            var e = span[i];
            int hv;
            if (e.IsInteger)
            {
                // Matches IntegerMembers.Hash
                var n = e.IntegerValue;
                hv = unchecked((int)RString.GetHashCode(
                    System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                        ref System.Runtime.CompilerServices.Unsafe.As<long, byte>(ref n), sizeof(long))));
            }
            else
            {
                var hashValue = state.Send(e, Names.Hash);
                hv = hashValue.IsInteger
                    ? unchecked((int)hashValue.IntegerValue)
                    : hashValue.GetHashCode();
            }
            h ^= hv << (i % 16);
        }
        return new MRubyValue(h);
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

    /// <summary>
    /// Concatenation <c>+</c>. Returns a new array built by concatenating <c>self</c> with the given array.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2] + [3, 4]     # => [1, 2, 3, 4]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Repetition <c>*</c>. With an integer returns a new array with the contents of <c>self</c> repeated; with a string joins the elements using it as the separator.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2] * 3        # => [1, 2, 1, 2, 1, 2]
    /// [1, 2, 3] * ","   # => "1,2,3"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns a new array containing the elements of <c>self</c> in reverse order.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].reverse    # => [3, 2, 1]
    /// </code>
    /// </example>
    [RubyDef("() -> Array[Elem]")]
    public static MRubyValue Reverse(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var result = state.NewArray(array.Length);
        array.CopyTo(result);
        result.AsSpan().Reverse();
        return result;
    }

    /// <summary>
    /// Reverses the elements of <c>self</c> in place and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.reverse!     # => [3, 2, 1]
    /// a              # => [3, 2, 1]
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue ReverseBang(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);
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

    /// <summary>
    /// Rotates <c>self</c> in place and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3, 4]
    /// a.rotate!      # => [2, 3, 4, 1]
    /// a.rotate!(-1)  # => [1, 2, 3, 4]
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> self")]
    public static MRubyValue RotateBang(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        var length = array.Length;
        if (length <= 1)
        {
            return self;
        }

        var count = state.TryGetArgumentAt(0, out var arg0)
            ? state.AsInteger(arg0)
            : 1;
        count %= length;
        if (count < 0)
        {
            count += length;
        }
        if (count == 0)
        {
            return self;
        }

        array.MakeModifiable(length);
        var span = array.AsSpan();
        span[..(int)count].Reverse();
        span[(int)count..].Reverse();
        span.Reverse();
        return self;
    }

    /// <summary>
    /// Returns <c>true</c> when an element compares equal to the argument using <c>==</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].include?(2)  # => true
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Include(MRubyState state, MRubyValue self)
    {
        var item = state.GetArgumentAt(0);
        foreach (var value in self.As<RArray>().AsSpan())
        {
            if (state.ValueEquals(value, item))
            {
                return MRubyValue.True;
            }
        }
        return MRubyValue.False;
    }

    /// <summary>
    /// Removes and returns the element at the given index, or <c>nil</c> when the index is out of range.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.delete_at(1)   # => 2
    /// a                # => [1, 3]
    /// </code>
    /// </example>
    [RubyDef("(int) -> Elem")]
    public static MRubyValue DeleteAt(MRubyState state, MRubyValue self)
    {
        var array = self.As<RArray>();
        var arg = state.GetArgumentAt(0);
        var index = state.AsInteger(arg);
        return array.DeleteAt((int)index);
    }

    /// <summary>
    /// Returns a string by concatenating the <c>to_s</c> of each element, formatted like <c>"[a, b, c]"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].to_s     # => "[1, 2, 3]"
    /// </code>
    /// </example>
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


    /// <summary>
    /// Returns a human-readable string by concatenating the <c>inspect</c> of each element, formatted like <c>"[a, b, c]"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, "x"].inspect     # => "[1, \"x\"]"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the index of the first element equal to the given value, or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3, 2].index(2)    # => 1
    /// [1, 2, 3].index(9)       # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the index of the last element equal to the given value, or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3, 2].rindex(2)   # => 3
    /// [1, 2, 3].rindex(9)      # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns a string created by converting each element to a string and joining them with the given separator (default empty).
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].join          # => "123"
    /// [1, 2, 3].join("-")     # => "1-2-3"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Removes all elements from <c>self</c> and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.clear      # => []
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue Clear(MRubyState state, MRubyValue self)
    {
        self.As<RArray>().Clear();
        return self;
    }

    /// <summary>
    /// Appends the elements of each given array to <c>self</c> and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2]
    /// a.concat([3, 4], [5])   # => [1, 2, 3, 4, 5]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Removes and returns the first element of <c>self</c>, or the first <c>n</c> elements when an argument is given.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.shift     # => 1
    /// a           # => [2, 3]
    /// a.shift(2)  # => [2, 3]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Prepends the given element(s) to the front of <c>self</c> and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [3, 4]
    /// a.unshift(1, 2)   # => [1, 2, 3, 4]
    /// </code>
    /// </example>
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

    /// <summary>Internal helper used by <c>Array#==</c> to short-circuit common cases.</summary>
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

    /// <summary>Internal helper used by <c>Array#&lt;=&gt;</c> to short-circuit common cases.</summary>
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
    /// <summary>Internal helper used to convert a multi-value array into a single value (used by destructuring assignment).</summary>
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

    /// <summary>
    /// Returns <c>self</c>. Used by pattern matching to deconstruct an array.
    /// </summary>
    /// <example>
    /// <code>
    /// case [1, 2, 3]
    /// in [a, b, c] then [a, b, c]   # => [1, 2, 3]
    /// end
    /// </code>
    /// </example>
    [RubyDef("() -> Array[Elem]")]
    public static MRubyValue Deconstruct(MRubyState state, MRubyValue self) => self;
}
