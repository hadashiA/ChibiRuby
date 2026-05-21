namespace MRubyCS.StdLib;

[RubyClass("Numeric")]
static class NumericMembers
{
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eql(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (self.IsFloat)
        {
            if (!other.IsFloat) return MRubyValue.False;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return self.FloatValue == other.FloatValue;
        }

        if (self.IsInteger)
        {
            if (!other.IsInteger) return MRubyValue.False;
            return self.IntegerValue == other.IntegerValue;
        }

        return self == other;
    }


    [RubyDef("(Numeric) -> bool")]
    public static MRubyValue OpLt(MRubyState state, MRubyValue self)
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
            return MRubyValue.False;
        }

        return x < y;
    }

    [RubyDef("(Numeric) -> bool")]
    public static MRubyValue OpLe(MRubyState state, MRubyValue self)
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
            return MRubyValue.False;
        }

        return x <= y;
    }

    [RubyDef("(Numeric) -> bool")]
    public static MRubyValue OpGt(MRubyState state, MRubyValue self)
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
            return MRubyValue.False;
        }

        return x > y;
    }

    [RubyDef("(Numeric) -> bool")]
    public static MRubyValue OpGe(MRubyState state, MRubyValue self)
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
            return MRubyValue.False;
        }

        return x >= y;
    }

    [RubyDef("(untyped) -> Integer")]
    public static MRubyValue OpCmp(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (self.IsInteger)
        {
            if (other.IsInteger)
            {
                return self.IntegerValue.CompareTo(other.IntegerValue);
            }
            if (other.IsFloat)
            {
                return ((double)self.IntegerValue).CompareTo(other.FloatValue);
            }
        }
        else if (self.IsFloat)
        {
            if (other.IsInteger)
            {
                return self.FloatValue.CompareTo((double)other.IntegerValue);
            }
            if (other.IsFloat)
            {
                return self.FloatValue.CompareTo(other.FloatValue);
            }
        }
        return -2;
    }
}