using System;
using System.Collections.Generic;

namespace MRubyCS;

/// <summary>Snapshot for <see cref="MRubyState.SaveCallStateForSandbox"/> / <see cref="MRubyState.RestoreCallStateForSandbox"/>.</summary>
public readonly struct CallStateSnapshot
{
    internal readonly int CallDepth;
    internal readonly MRubyLongJumpException? Exception;

    internal CallStateSnapshot(int callDepth, MRubyLongJumpException? exception)
    {
        CallDepth = callDepth;
        Exception = exception;
    }
}

partial class MRubyState
{
    public RClass BindingClass { get; private set; } = default!;

    /// <summary>Optional hook for a debugger to suspend execution at <c>binding.irb</c>.</summary>
    public IMRubyDebuggerHook? DebuggerHook { get; set; }

    /// <summary>Capture the caller's frame as a <see cref="RBinding"/> (for C#-defined Ruby methods).</summary>
    public RBinding CreateBinding() => CreateBindingFrom(Context.CallDepth - 1);

    /// <summary>Capture the current top-of-stack frame (for debugger hook contexts).</summary>
    public RBinding CreateBindingForCurrentFrame() => CreateBindingFrom(Context.CallDepth);

    RBinding CreateBindingFrom(int depth)
    {
        var ctx = Context;
        if (depth < 0 || depth > ctx.CallDepth)
        {
            return CreateEmptyBinding(TopSelf);
        }

        ref var frame = ref ctx.CallStack[depth];
        var proc = frame.Proc;
        var self = ctx.Stack[frame.StackPointer];

        var binding = new RBinding(
            BindingClass,
            self,
            proc,
            frame.ProgramCounter,
            frame.MethodId,
            ctx,
            depth);

        (frame.LiveBindings ??= new List<RBinding>(1)).Add(binding);
        return binding;
    }

    RBinding CreateEmptyBinding(MRubyValue receiver) =>
        new(BindingClass, receiver, null, 0, default, Array.Empty<Symbol>(), Array.Empty<MRubyValue>());

    public CallStateSnapshot SaveCallStateForSandbox() =>
        new(Context.CallDepth, Exception);

    public void RestoreCallStateForSandbox(CallStateSnapshot snapshot)
    {
        while (Context.CallDepth > snapshot.CallDepth)
        {
            Context.PopCallStack();
        }
        Exception = snapshot.Exception;
    }
}
