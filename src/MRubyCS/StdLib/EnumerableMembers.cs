namespace MRubyCS.StdLib;

[RubyModule("Enumerable")]
static class EnumerableMembers
{
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
