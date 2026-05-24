namespace MRubyCS.StdLib;

/// <summary>
/// Root of the exception class hierarchy. Wraps a message and an optional
/// backtrace; raised via <c>raise</c> and caught with <c>rescue</c>. User code
/// should usually subclass <c>StandardError</c>, not <c>Exception</c> directly,
/// since a bare <c>rescue</c> only catches <c>StandardError</c> descendants.
/// </summary>
[RubyClass("Exception")]
static class ExceptionMembers
{
    /// <summary>
    /// Creates a new exception instance, forwarding arguments to <c>initialize</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// e = StandardError.new("oops")
    /// e.message         # => "oops"
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> Exception")]
    public static MRubyValue New(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        var block = state.GetBlockArgument();

        var c = self.As<RClass>();
        var o = new RException(null, c);
        var value = new MRubyValue(o);
        if (state.TryFindMethod(c, Names.Initialize, out var method, out _) &&
            method != MRubyMethod.Nop)
        {
            state.Send(value, Names.Initialize, args, kargs: null, block: block);
        }
        return value;
    }

    /// <summary>
    /// Returns <c>self</c> when called without arguments, otherwise returns a
    /// copy of <c>self</c> with the given message.
    /// </summary>
    /// <example>
    /// <code>
    /// e = StandardError.new("oops")
    /// e.exception            # => e
    /// e.exception("again")   # => copy with "again"
    /// </code>
    /// </example>
    [RubyDef("(?String) -> Exception")]
    public static MRubyValue Exception(MRubyState state, MRubyValue self)
    {
        if (!state.TryGetArgumentAt(0, out var arg) || arg == self)
        {
            return self;
        }

        var ex = state.CloneObject(self);
        ex.As<RException>().Message = state.Stringify(arg);
        return ex;
    }

    /// <summary>
    /// Initializes <c>self</c>; the optional argument is stored as the message.
    /// </summary>
    /// <example>
    /// <code>
    /// e = StandardError.new("oops")
    /// e.message         # => "oops"
    /// </code>
    /// </example>
    [RubyDef("(?String) -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        if (state.TryGetArgumentAt(0, out var arg))
        {
            self.As<RException>().Message = state.Stringify(arg);
        }
        return self;
    }

    /// <summary>
    /// Returns the message of <c>self</c>, or the class name when no message is set.
    /// </summary>
    /// <example>
    /// <code>
    /// e = StandardError.new("oops")
    /// e.to_s            # => "oops"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        if (self.As<RException>().Message is { } message)
        {
            return message;
        }
        return state.NameOf(state.ClassOf(self));
    }

    /// <summary>
    /// Returns a String describing <c>self</c>, including its class and message.
    /// </summary>
    /// <example>
    /// <code>
    /// e = StandardError.new("oops")
    /// e.inspect         # => "oops (StandardError)"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var className = state.NameOf(state.ClassOf(self));
        var message = self.As<RException>().Message;
        if (message is { Length: > 0 })
        {
            return state.NewString($"{message} ({className})");
        }
        return className;
    }

    /// <summary>
    /// Returns the backtrace of <c>self</c> as an Array of Strings, or
    /// <c>nil</c> when no backtrace was captured.
    /// </summary>
    /// <example>
    /// <code>
    /// begin
    ///   raise "oops"
    /// rescue =&gt; e
    ///   e.backtrace      # => ["..."]
    /// end
    /// </code>
    /// </example>
    [RubyDef("() -> Array[String]")]
    public static MRubyValue Backtrace(MRubyState state, MRubyValue self)
    {
        var backtrace = self.As<RException>().Backtrace;
        if (backtrace is null)
        {
            return MRubyValue.Nil;
        }
        return backtrace.ToRArray(state);
    }
}
