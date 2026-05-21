namespace MRubyCS.StdLib;

[RubyClass("TrueClass")]
static class TrueClassMembers
{
    static readonly byte[] TrueString = "true"u8.ToArray();

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue And(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.GetArgumentAt(0).Truthy);
    }

    [RubyDef("(untyped) -> true")]
    public static MRubyValue Or(MRubyState state, MRubyValue self)
    {
        return MRubyValue.True;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Xor(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(!state.GetArgumentAt(0).Truthy);
    }

    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var result = state.NewStringOwned(TrueString);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }
}
