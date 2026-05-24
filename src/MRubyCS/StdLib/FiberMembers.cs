using System;
using MRubyCS.Internals;

namespace MRubyCS.StdLib;

/// <summary>
/// Cooperatively-scheduled coroutine. A <c>Fiber</c> runs a block until it
/// calls <c>Fiber.yield</c>, at which point control returns to whoever called
/// <c>resume</c>; the next <c>resume</c> continues from the yield point. In
/// MRubyCS, fibers also drive <c>async</c>/<c>await</c> interop via the
/// active fiber scheduler.
/// </summary>
[RubyClass("Fiber")]
static class FiberMembers
{
    /// <summary>
    /// Initializes a new <c>Fiber</c> with the given block as its body.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new { |x| Fiber.yield x + 1 }
    /// f.resume(10)        # => 11
    /// </code>
    /// </example>
    [RubyDef("() { (*untyped) -> untyped } -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        var fiber = self.As<RFiber>();
        var proc = state.GetBlockArgument(false)!;
        fiber.Reset(proc);
        return self;
    }

    /// <summary>
    /// Resumes the fiber, passing any arguments to its block (or its previous
    /// <c>Fiber.yield</c>). Returns the value yielded or the block's final value.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new { |x| Fiber.yield x * 2 }
    /// f.resume(3)         # => 6
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> untyped")]
    public static MRubyValue Resume(MRubyState state, MRubyValue self)
    {
        var fiber = self.As<RFiber>();
        var args = state.GetRestArgumentsAfter(0);
        var vmexec = state.Context.CurrentCallInfo.CallerType > CallerType.InVmLoop;
        return fiber.MoveNext(args, false, vmexec);
    }

    /// <summary>
    /// Transfers control to <c>self</c>, switching the running fiber explicitly.
    /// Unlike <c>resume</c>, the transferred-from fiber cannot be resumed
    /// implicitly; control must be transferred back.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new { Fiber.yield 1 }
    /// f.transfer          # => 1
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> untyped")]
    public static MRubyValue Transfer(MRubyState state, MRubyValue self)
    {
        var fiber = self.As<RFiber>();
        var args = state.GetRestArgumentsAfter(0);
        return fiber.Transfer(args);
    }

    /// <summary>
    /// Suspends the currently running fiber and returns the given values to
    /// the caller of <c>resume</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new { Fiber.yield 42 }
    /// f.resume            # => 42
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> untyped")]
    public static MRubyValue Yield(MRubyState state, MRubyValue self)
    {
        var fiber = state.Context.Fiber!;
        var args = state.GetRestArgumentsAfter(0);
        fiber.Yield();
        return state.AsFiberResult(args);
    }

    /// <summary>
    /// Returns the currently running fiber.
    /// </summary>
    /// <example>
    /// <code>
    /// Fiber.current       # => Fiber
    /// </code>
    /// </example>
    [RubyDef("() -> Fiber")]
    public static MRubyValue Current(MRubyState state, MRubyValue _) => state.CurrentFiber;

    /// <summary>
    /// <c>Fiber.schedule { ... }</c> -- convenience for creating a fiber and
    /// starting it immediately under the installed scheduler. Returns the
    /// new <see cref="RFiber"/>; the caller can observe it via
    /// <c>alive?</c> or just rely on the scheduler to drive it to completion.
    /// CRuby's <c>blocking: false</c> distinction is implicit here: any
    /// fiber under a scheduler that hits <c>sleep</c>/<c>Thread.pass</c>/IO
    /// will dispatch through the scheduler.
    /// </summary>
    [RubyDef("() { (*untyped) -> untyped } -> Fiber")]
    public static MRubyValue Schedule(MRubyState state, MRubyValue _)
    {
        var block = state.GetBlockArgument(false)!;
        var fiber = state.CreateFiber(block);
        fiber.Resume();
        return new MRubyValue(fiber);
    }

    /// <summary>
    /// Returns <c>true</c> while the fiber has not yet terminated.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new { }
    /// f.alive?            # => true
    /// f.resume
    /// f.alive?            # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue Alive(MRubyState state, MRubyValue self)
    {
        var fiber = self.As<RFiber>();
        return fiber.IsAlive;
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> and the given value are the same fiber.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new {}
    /// f == f              # => true
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState state, MRubyValue self)
    {
        var fiber = self.As<RFiber>();
        var arg = state.GetArgumentAt(0);
        if (arg.Object is RFiber other)
        {
            return fiber == other;
        }
        return MRubyValue.False;
    }

    /// <summary>
    /// Returns a String describing <c>self</c>, including its current state.
    /// </summary>
    /// <example>
    /// <code>
    /// f = Fiber.new {}
    /// f.to_s              # => "#&lt;Fiber:(created)&gt;"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var fiber = self.As<RFiber>();
        var result = state.NewString("#<"u8);
        var c = state.ClassOf(self).GetRealClass();
        result.Concat(state.NameOf(c));
        result.Concat(":"u8);

        var s = fiber.State switch
        {
            FiberState.Created => "created"u8,
            FiberState.Running => "running"u8,
            FiberState.Resumed => "resumed"u8,
            FiberState.Suspended => "suspended"u8,
            FiberState.Transferred => "transferred"u8,
            FiberState.Terminated => "terminated"u8,
            _ => throw new ArgumentOutOfRangeException()
        };
        result.Concat("("u8);
        result.Concat(s);
        result.Concat(")"u8);
        return result;
    }
}
