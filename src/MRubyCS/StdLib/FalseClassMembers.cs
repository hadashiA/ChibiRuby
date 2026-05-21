namespace MRubyCS.StdLib;

[RubyClass("FalseClass")]
static class FalseClassMembers
{
    static readonly byte[] FalseString = "false"u8.ToArray();

    [RubyDef("(untyped) -> false")]
    public static MRubyValue And(MRubyState state, MRubyValue self) => MRubyValue.False;

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Or(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.GetArgumentAt(0).Truthy);
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Xor(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.GetArgumentAt(0).Truthy);
    }

    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var result = state.NewStringOwned(FalseString);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }
}
