namespace MRubyCS.StdLib;

[RubyClass("NilClass")]
static class NilClassMembers
{
    [RubyDef("() -> String")]
    public static MRubyValue Tos(MRubyState state, MRubyValue self)
    {
        var result = state.NewString(0);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var result = state.NewString("nil"u8);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }
}