namespace ChibiRuby.StdLib;

/// <summary>
/// Abstract base class for numeric types -- the parent of <c>Integer</c> and
/// <c>Float</c>. Defines coercion rules and shared behaviour like
/// <c>abs</c>, <c>zero?</c>, and <c>nonzero?</c>. Includes <c>Comparable</c>;
/// values are immutable.
/// </summary>
[RubyClass("Numeric")]
static class NumericMembers
{
    /// <summary>
    /// Returns <c>true</c> when the argument has the same class and numeric value as <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.eql?(1)      # => true
    /// 1.eql?(1.0)    # => false
    /// 1.0.eql?(1.0)  # => true
    /// </code>
    /// </example>
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


    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is less than the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 1 &lt; 2      # => true
    /// 2.0 &lt; 2    # => false
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is less than or equal to the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 1 &lt;= 2     # => true
    /// 2 &lt;= 2     # => true
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is greater than the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 3 &gt; 2      # => true
    /// 2 &gt; 2      # => false
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is greater than or equal to the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 3 &gt;= 2     # => true
    /// 2 &gt;= 2     # => true
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>-1</c>, <c>0</c>, or <c>1</c> when <c>self</c> is less than, equal to, or greater than the argument.
    /// </summary>
    /// <example>
    /// <code>
    /// 1 &lt;=&gt; 2     # => -1
    /// 2 &lt;=&gt; 2     # => 0
    /// 3 &lt;=&gt; 2     # => 1
    /// </code>
    /// </example>
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