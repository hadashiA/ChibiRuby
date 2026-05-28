namespace ChibiRuby.StdLib;

/// <summary>
/// The class of the singleton <c>true</c> value. Note that in Ruby every
/// object except <c>nil</c> and <c>false</c> is truthy, so <c>true</c> itself
/// is just one specific truthy value.
/// </summary>
[RubyClass("TrueClass")]
static class TrueClassMembers
{
    static readonly byte[] TrueString = "true"u8.ToArray();

    /// <summary>
    /// Returns the logical AND of <c>true</c> and the given value
    /// (i.e. <c>true</c> if the argument is truthy, otherwise <c>false</c>).
    /// </summary>
    /// <example>
    /// <code>
    /// true &amp; false       # => false
    /// true &amp; 1           # => true
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue And(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.GetArgumentAt(0).Truthy);
    }

    /// <summary>
    /// Returns <c>true</c>. The argument is not evaluated for short-circuiting.
    /// </summary>
    /// <example>
    /// <code>
    /// true | false       # => true
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> true")]
    public static MRubyValue Or(MRubyState state, MRubyValue self)
    {
        return MRubyValue.True;
    }

    /// <summary>
    /// Returns <c>true</c> if the argument is falsy, otherwise <c>false</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// true ^ false       # => true
    /// true ^ true        # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Xor(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(!state.GetArgumentAt(0).Truthy);
    }

    /// <summary>
    /// Returns the String <c>"true"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// true.to_s          # => "true"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var result = state.NewStringOwned(TrueString);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }
}
