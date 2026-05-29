using System;
using ChibiRuby.Internals;

namespace ChibiRuby;

partial class MRubyState
{
    public RFiber CurrentFiber => Context.Fiber ??= new RFiber(Context, this, FiberClass);

    public MRubyFiberScheduler? FiberScheduler { get; private set; }

    /// <summary>
    /// Returns the installed scheduler iff it can drive the current call
    /// site (a scheduler is installed AND <see cref="CurrentFiber"/> is
    /// non-root). Use from Ruby method implementations to decide between
    /// the cooperative path and the synchronous fallback.
    /// </summary>
    public bool TryGetActiveFiberScheduler(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MRubyFiberScheduler? scheduler)
    {
        scheduler = FiberScheduler is { } s && !CurrentFiber.IsRoot ? s : null;
        return scheduler is not null;
    }

    public void UseFiberScheduler(MRubyFiberScheduler? scheduler = null)
    {
        scheduler ??= new MRubyFiberScheduler();
        scheduler.Attach(this);
        FiberScheduler = scheduler;
    }

    public void ClearFiberScheduler() => FiberScheduler = null;

    public RProc ParseBytecodeAsProc(ReadOnlySpan<byte> bytecode)
    {
        var irep = ParseBytecode(bytecode);
        return NewProc(irep);
    }

    public RFiber ParseBytecodeAsFiber(ReadOnlySpan<byte> bytecode)
    {
        var proc = ParseBytecodeAsProc(bytecode);
        return CreateFiber(proc);
    }

    public RFiber CreateFiber(RProc proc)
    {
        var fiber = new RFiber(this, FiberClass);
        fiber.Reset(proc);
        return fiber;
    }

    internal MRubyValue AsFiberResult(ReadOnlySpan<MRubyValue> args)
    {
        return args.Length switch
        {
            0 => MRubyValue.Nil,
            1 => args[0],
            _ => NewArray(args)
        };
    }

    internal void SwitchToContext(MRubyContext newContext)
    {
        Context = newContext;
    }

    internal void EnsureFiberInitialized(RFiber fiber)
    {
        if (fiber.Proc is null)
        {
            Raise(Names.FiberError, "unitialized fiber"u8);
        }
    }

    internal void EnsureValidFiberBoundary(MRubyContext context)
    {
        for (var i = context.CallDepth; i >= 0; i--)
        {
            if (context.CallStack[i].CallerType > CallerType.InVmLoop)
            {
                Raise(Names.FiberError, "can't cross C# function boundary"u8);
            }
        }
    }

    internal void EnsureValidFiberBoundaryRecursive(MRubyContext context)
    {
        var c = context.Previous;
        while (c != null)
        {
            EnsureValidFiberBoundary(c);
            if (c == ContextRoot) break;
            c = c.Previous;
        }
    }
}
