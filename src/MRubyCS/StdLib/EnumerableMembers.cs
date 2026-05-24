namespace MRubyCS.StdLib;

/// <summary>
/// Mixin providing collection traversal, searching, and sorting methods on top
/// of a host-defined <c>each</c>. Including classes get <c>map</c>, <c>select</c>,
/// <c>reduce</c>, <c>sort</c>, <c>min</c>/<c>max</c>, and many more for free.
/// Most methods are implemented in Ruby (see <c>StdLib/lib.rb</c>); this C#
/// class only hosts internal helpers.
/// </summary>
[RubyModule("Enumerable")]
static class EnumerableMembers
{
    /// <summary>
    /// Internal helper used by Enumerable to combine element hashes.
    /// </summary>
    [RubyDef("(Integer, Integer, Integer) -> Integer")]
    public static MRubyValue InternalUpdateHash(MRubyState state, MRubyValue self)
    {
        var hash = (int)state.GetArgumentAsIntegerAt(0);
        var index = (int)state.GetArgumentAsIntegerAt(1);
        var hv = (int)state.GetArgumentAsIntegerAt(2);
        hash ^= hv << (index % 16);
        return new MRubyValue(hash);
    }
}
