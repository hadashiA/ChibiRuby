namespace MRubyCS.StdLib;

/// <summary>
/// The class of the singleton <c>false</c> value. Along with <c>nil</c>, this
/// is one of the only two falsy values in Ruby -- every other object,
/// including <c>0</c> and the empty string, is truthy.
/// </summary>
[RubyClass("FalseClass")]
static class FalseClassMembers
{
    static readonly byte[] FalseString = "false"u8.ToArray();

    /// <summary>
    /// Returns <c>false</c>. The argument is not evaluated.
    /// </summary>
    /// <example>
    /// <code>
    /// false &amp; true       # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> false")]
    public static MRubyValue And(MRubyState state, MRubyValue self) => MRubyValue.False;

    /// <summary>
    /// Returns <c>true</c> if the argument is truthy, otherwise <c>false</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// false | true       # => true
    /// false | nil        # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Or(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.GetArgumentAt(0).Truthy);
    }

    /// <summary>
    /// Returns <c>true</c> if the argument is truthy, otherwise <c>false</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// false ^ true       # => true
    /// false ^ false      # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Xor(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.GetArgumentAt(0).Truthy);
    }

    /// <summary>
    /// Returns the String <c>"false"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// false.to_s         # => "false"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var result = state.NewStringOwned(FalseString);
        result.MarkAsFrozen();
        return new MRubyValue(result);
    }
}
