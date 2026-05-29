namespace ChibiRuby.StdLib;

/// <summary>
/// Root of the Ruby class hierarchy -- has no superclass and an intentionally
/// minimal API. Used as a base for proxy/builder objects that need to avoid
/// inheriting <c>Object</c>/<c>Kernel</c> methods (e.g. <c>method_missing</c>
/// delegators). Most code should inherit from <c>Object</c> instead.
/// </summary>
[RubyClass("BasicObject", Superclass = "")]
static class BasicObjectMembers
{
    /// <summary>
    /// Boolean negation. Returns <c>true</c> if <c>self</c> is falsy, otherwise <c>false</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// !true       # => false
    /// !nil        # => true
    /// !"hello"    # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue Not(MRubyState _, MRubyValue self) => new MRubyValue(!self.Truthy);

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> and the argument are the same object.
    /// </summary>
    /// <example>
    /// <code>
    /// a = "hi"
    /// a == a       # => true
    /// a == "hi"    # => false  (BasicObject#== is identity)
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        return self == state.GetArgumentAt(0);
    }

    /// <summary>
    /// Returns an integer identifier for <c>self</c>. Equal to <c>object_id</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// obj = Object.new
    /// obj.__id__ == obj.__id__   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue Id(MRubyState state, MRubyValue self)
    {
        return self.ObjectId;
    }

    /// <summary>
    /// Invokes the method named <c>name</c> on <c>self</c> with the given arguments and block.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".__send__(:upcase)        # => "HELLO"
    /// [1, 2, 3].__send__(:push, 4)     # => [1, 2, 3, 4]
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String, *untyped) ?{ (*untyped) -> untyped } -> untyped")]

    public static MRubyValue Send(MRubyState state, MRubyValue self)
    {
        return state.SendMeta(self);
    }

    /// <summary>
    /// Evaluates the given block in the context of <c>self</c>, where <c>self</c> within the block refers to the receiver.
    /// </summary>
    /// <example>
    /// <code>
    /// "hello".instance_eval { upcase }   # => "HELLO"
    /// </code>
    /// </example>
    [RubyDef("(*untyped) ?{ (instance) -> untyped } -> untyped")]

    public static MRubyValue InstanceEval(MRubyState state, MRubyValue self)
    {
        var block = state.GetBlockArgument(false);
        return state.EvalUnder(self, block!, state.SingletonClassOf(self));
    }

    /// <summary>
    /// Invoked by Ruby when an undefined method is called. The default implementation raises <c>NoMethodError</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def method_missing(name, *args)
    ///     "called #{name}"
    ///   end
    /// end
    /// Foo.new.bar    # => "called bar"
    /// </code>
    /// </example>
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
