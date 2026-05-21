namespace MRubyCS.StdLib;

[RubyClass("BasicObject", Superclass = "")]
static class BasicObjectMembers
{
    [RubyDef("() -> bool")]
    public static MRubyValue Not(MRubyState _, MRubyValue self) => new MRubyValue(!self.Truthy);

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        return self == state.GetArgumentAt(0);
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue Id(MRubyState state, MRubyValue self)
    {
        return self.ObjectId;
    }

    [RubyDef("(Symbol | String, *untyped) ?{ (*untyped) -> untyped } -> untyped")]

    public static MRubyValue Send(MRubyState state, MRubyValue self)
    {
        return state.SendMeta(self);
    }

    [RubyDef("(*untyped) ?{ (instance) -> untyped } -> untyped")]

    public static MRubyValue InstanceEval(MRubyState state, MRubyValue self)
    {
        var block = state.GetBlockArgument(false);
        return state.EvalUnder(self, block!, state.SingletonClassOf(self));
    }

    [RubyDef("(Symbol, *untyped) ?{ (*untyped) -> untyped } -> untyped")]

    public static MRubyValue MethodMissing(MRubyState state, MRubyValue self)
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        var args = state.GetRestArgumentsAfter(1);
        var array = state.NewArray(args);
        state.RaiseMethodMissing(methodId, self, array);
        return MRubyValue.Nil;
    }
}
