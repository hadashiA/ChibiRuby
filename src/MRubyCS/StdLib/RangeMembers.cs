namespace MRubyCS.StdLib;

[RubyClass("Range", TypeParameters = "Elem")]
static class RangeMembers
{
    [RubyDef("(Elem, Elem, ?bool) -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        var range = self.As<RRange>();
        if (range.IsFrozen)
        {
            state.Raise(Names.NameError, "'initialize' called twice"u8);
        }
        range.Begin = state.GetArgumentAt(0);
        range.End = state.GetArgumentAt(1);
        if (state.TryGetArgumentAt(2, out var exclusiveValue))
        {
            range.Exclusive = exclusiveValue.Truthy;
        }
        range.MarkAsFrozen();
        return self;
    }

    [RubyDef("(Range[Elem]) -> self")]
    public static MRubyValue InitializeCopy(MRubyState state, MRubyValue self)
    {
        var range = self.As<RRange>();
        if (range.IsFrozen)
        {
            state.Raise(Names.NameError, "'initialize_copy' called twice"u8);
        }
        var src = state.GetArgumentAsRangeAt(0);
        if (range == src)
        {
            return self;
        }

        range.Begin = src.Begin;
        range.End = src.End;
        range.Exclusive = src.Exclusive;
        range.MarkAsFrozen();
        return self;
    }

    [RubyDef("() -> Elem")]
    public static MRubyValue Begin(MRubyState state, MRubyValue self)
    {
        return self.As<RRange>().Begin;
    }

    [RubyDef("() -> Elem")]
    public static MRubyValue End(MRubyState state, MRubyValue self)
    {
        return self.As<RRange>().End;
    }

    [RubyDef("() -> bool")]
    public static MRubyValue ExcludeEnd(MRubyState state, MRubyValue self)
    {
        return self.As<RRange>().Exclusive;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        var arg0 = state.GetArgumentAt(0);
        if (self == arg0) return MRubyValue.True;

        var range = self.As<RRange>();
        if (arg0.Object is not RRange rangeOther)
        {
            return MRubyValue.False;
        }
        return state.ValueEquals(range.Begin, rangeOther.Begin) &&
            state.ValueEquals(range.End, rangeOther.End) &&
            range.Exclusive == rangeOther.Exclusive;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue IsInclude(MRubyState state, MRubyValue self)
    {
        var range = self.As<RRange>();
        var value = state.GetArgumentAt(0);

        if (range.Begin.IsNil)
        {
            var result = range.Exclusive
                // end > value
                ? state.ValueCompare(range.End, value) == 1
                // end >= value
                : state.ValueCompare(range.End, value) is 0 or 1;
            return result;
        }

        // begin <= value
        if (state.ValueCompare(range.Begin, value) is 0 or -1)
        {
            if (range.End.IsNil)
            {
                return MRubyValue.True;
            }

            var result = range.Exclusive
                // end > value
                ? state.ValueCompare(range.End, value) == 1
                // end >= value
                : state.ValueCompare(range.End, value) is 0 or 1;
            return result;
        }
        return MRubyValue.False;
    }

    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var range = self.As<RRange>();
        var b = state.Stringify(range.Begin);
        var e = state.Stringify(range.End);

        var result = range.Exclusive
            ? state.NewString($"{b}...{e}")
            : state.NewString($"{b}..{e}");
        return new MRubyValue(result);
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var range = self.As<RRange>();
        var result = state.NewString(6);
        if (!range.Begin.IsNil)
        {
            result.Concat(state.Inspect(range.Begin));
        }
        result.Concat(range.Exclusive ? "..."u8 : ".."u8);
        if (!range.End.IsNil)
        {
            result.Concat(state.Inspect(range.End));
        }
        return new MRubyValue(result);
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEql(MRubyState state, MRubyValue self)
    {
        var arg0 = state.GetArgumentAt(0);
        if (self == arg0) return MRubyValue.True;

        var range = self.As<RRange>();
        if (arg0.Object is not RRange rangeOther)
        {
            return MRubyValue.False;
        }

        // Use eql? instead of == for stricter equality
        var beginEql = state.Send(range.Begin, Names.QEql, rangeOther.Begin);
        var endEql = state.Send(range.End, Names.QEql, rangeOther.End);
        return beginEql.Truthy && endEql.Truthy && range.Exclusive == rangeOther.Exclusive;
    }

    [RubyDef("() -> Elem")]
    public static MRubyValue First(MRubyState state, MRubyValue self)
    {
        return self.As<RRange>().Begin;
    }

    [RubyDef("() -> Elem")]
    public static MRubyValue Last(MRubyState state, MRubyValue self)
    {
        return self.As<RRange>().End;
    }

    [RubyDef("() -> Array[Elem]?")]
    public static MRubyValue InternalNumToA(MRubyState state, MRubyValue self)
    {
        var range = self.As<RRange>();
        if (range.End.IsNil)
        {
            state.Raise(Names.RangeError, "cannot convert endless range to an array"u8);
        }

        if (range.Begin.IsInteger)
        {
            if (range.End.IsInteger)
            {
                var a = range.Begin.IntegerValue;
                var b = range.End.IntegerValue;
                var len = b - a;
                if (!range.Exclusive) len++;

                var array = state.NewArray((int)len);
                array.MakeModifiable((int)len, true);
                for (var i = 0; i < len; i++)
                {
                    array[i] = a + i;
                }

                return array;
            }

            if (range.End.IsFloat)
            {
                var a = (float)range.Begin.IntegerValue;
                var b = range.End.FloatValue;
                if (a > b)
                {
                    return state.NewArray(0);
                }

                var array = state.NewArray((int)(b - a) + 1);
                var i = 0;
                if (range.Exclusive)
                {
                    while (a < b)
                    {
                        array[i++] = (int)a;
                        a += 1f;
                    }
                }
                else
                {
                    while (a <= b)
                    {
                        array[i++] = (int)a;
                        a += 1f;
                    }
                }
                return array;
            }
        }
        return MRubyValue.Nil;
    }
}
