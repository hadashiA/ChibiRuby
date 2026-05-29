using System.Collections.Generic;

namespace ChibiRuby;

public sealed class MRubyValueEqualityComparer(MRubyState state) : IEqualityComparer<MRubyValue>
{
    public bool Equals(MRubyValue x, MRubyValue y)
    {
        return state.ValueEquals(x, y);
    }

    public int GetHashCode(MRubyValue value)
    {
        return value.GetHashCode();
    }
}

public sealed class MRubyValueHashKeyEqualityComparer(MRubyState state) : IEqualityComparer<MRubyValue>
{
    public bool Equals(MRubyValue a, MRubyValue b)
    {
        return a == b || state.Send(a, Names.QEql, b).Truthy;
    }

    public int GetHashCode(MRubyValue value)
    {
        return value.GetHashCode();
    }
}
