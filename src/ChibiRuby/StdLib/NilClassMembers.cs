namespace ChibiRuby.StdLib;

/// <summary>
/// The class of the singleton <c>nil</c> value. Returned by methods that have
/// no meaningful value, used as the implicit return of empty method bodies,
/// and one of the only two falsy values in Ruby (the other being <c>false</c>).
/// </summary>
[RubyClass("NilClass")]
static class NilClassMembers
{
    /// <summary>
    /// Returns the empty string.
    /// </summary>
    /// <example>
    /// <code>
    /// nil.to_s          # => ""
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Tos(MRubyState state, MRubyValue self)
    {
        var result = state.NewString(0);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }

    /// <summary>
    /// Returns the String <c>"nil"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// nil.inspect       # => "nil"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var result = state.NewString("nil"u8);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }
}