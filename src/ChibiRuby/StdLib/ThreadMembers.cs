namespace ChibiRuby.StdLib;

/// <summary>
/// ChibiRuby does not implement OS threads (the VM is single-threaded);
/// the <c>Thread</c> class exists primarily to host CRuby-compatible
/// cooperative-scheduling entry points such as <c>Thread.pass</c>.
/// </summary>
[RubyClass("Thread")]
static class ThreadMembers
{
    /// <summary>
    /// <c>Thread.pass</c> -- CRuby-compatible cooperative yield. Hands
    /// control back to the <see cref="MRubyFiberScheduler"/> so other
    /// in-flight fibers and host async work can run before this fiber is
    /// resumed. No-op at the root fiber or when no scheduler is installed.
    /// </summary>
    /// <example>
    /// <code>
    /// Thread.pass    # => nil
    /// </code>
    /// </example>
    [RubyDef("() -> nil")]
    public static MRubyValue Pass(MRubyState state, MRubyValue _)
    {
        if (state.TryGetActiveFiberScheduler(out var scheduler))
        {
            scheduler.Yield();
        }
        return MRubyValue.Nil;
    }
}
