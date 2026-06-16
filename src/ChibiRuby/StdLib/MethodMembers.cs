namespace ChibiRuby.StdLib;

/// <summary>
/// A bound Ruby method object. It keeps the receiver and the resolved method
/// entry captured by <c>Kernel#method</c>, then invokes that entry from
/// <c>call</c> / <c>[]</c>.
/// </summary>
[RubyClass("Method")]
static class MethodMembers
{
    /// <summary>
    /// Invokes the captured method with the given arguments and block.
    /// </summary>
    [RubyDef("(*untyped) ?{ (*untyped) -> untyped } -> untyped")]
    public static MRubyValue Call(MRubyState state, MRubyValue self)
    {
        var method = self.As<RMethod>();
        var args = state.GetRestArgumentsAfter(0);
        var kargs = state.GetKeywordArguments();
        var block = state.GetBlockArgument();
        return state.CallResolvedMethod(method.Receiver, method.MethodId, method.Method, method.Owner, args, kargs, block);
    }

    /// <summary>
    /// Returns the method name.
    /// </summary>
    [RubyDef("() -> Symbol")]
    public static MRubyValue Name(MRubyState state, MRubyValue self)
    {
        return self.As<RMethod>().MethodId;
    }

    /// <summary>
    /// Returns the receiver bound to this method.
    /// </summary>
    [RubyDef("() -> untyped")]
    public static MRubyValue Receiver(MRubyState state, MRubyValue self)
    {
        return self.As<RMethod>().Receiver;
    }

    /// <summary>
    /// Returns <c>true</c> when both method objects wrap the same receiver and method entry.
    /// </summary>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eql(MRubyState state, MRubyValue self)
    {
        if (state.GetArgumentAt(0).Object is not RMethod other)
        {
            return MRubyValue.False;
        }

        var method = self.As<RMethod>();
        return method.Receiver == other.Receiver &&
               method.Owner == other.Owner &&
               method.Method == other.Method;
    }

    /// <summary>
    /// Returns a hash value consistent with <c>eql?</c>.
    /// </summary>
    [RubyDef("() -> Integer")]
    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        return self.As<RMethod>().GetHashCode();
    }
}
