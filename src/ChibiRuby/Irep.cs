namespace ChibiRuby;

// AOT-compiled body for one irep — what a build-time Ruby->C# generator emits for a
// hot, statically-compilable method. It runs directly on the call frame instead of
// interpreting the irep's bytecode: `self` is at stack[stackPointer], args at
// stack[stackPointer + 1 ..]. Returns true with `result` set when its speculative
// type/shape guards hold; returns false (having mutated nothing) to deopt — the VM
// then interprets the irep's bytecode. Ordinary compiled C# (no runtime IL), so it
// works under IL2CPP / NativeAOT.
public delegate bool CompiledRubyMethodBody(MRubyState state, int stackPointer, out MRubyValue result);

public enum CatchHandlerType : byte
{
    Rescue = 0,
    Ensure = 1,
    All = 2
}

public readonly struct CatchHandler(CatchHandlerType handlerType, uint begin, uint end, uint target)
{
    public readonly CatchHandlerType HandlerType = handlerType;

    /// <summary>
    /// The starting address to match the handler. Includes this.
    /// </summary>
    public readonly uint Begin = begin;

    /// <summary>
    /// The endpoint address that matches the handler. Not Includes this.
    /// </summary>
    public readonly uint End = end;

    /// <summary>
    /// The address to jump to if a match is made.
    /// </summary>
    public readonly uint Target = target;
}

/// <summary>
/// Program data
/// </summary>
public class Irep
{
    public byte Flags { get; init; }
    public ushort RegisterVariableCount { get; init; }
    public byte[] Sequence { get; init; } = [];
    public Symbol[] Symbols { get; init; } = [];
    public Symbol[] LocalVariables { get; init; } = [];
    public MRubyValue[] PoolValues { get; init; } = [];
    public Irep[] Children { get; init; } = [];
    public CatchHandler[] CatchHandlers { get; init; } = [];

    /// <summary>
    /// Source-position information recovered from the .mrb file's DBG section, if it was
    /// emitted (controlled by <c>MRubyCompiler.Compile(..., debugInfo: true)</c>). Null
    /// when the bytecode was produced without debug info.
    /// </summary>
    public IrepDebugInfo? DebugInfo { get; internal set; }

    // AOT-compiled C# body for THIS irep (null = interpret the bytecode). The
    // bytecode VM branches on this where an irep begins executing: if set and its
    // speculative guards hold, the compiled body runs and the bytecode is ignored;
    // on a guard miss the body returns false and this same irep is interpreted
    // (the irep is its own deopt fallback). Attached at load time by the codegen
    // layer; keyed by the irep itself (no class/method names involved).
    internal CompiledRubyMethodBody? CompiledBody;

    // Memoized content fingerprint (see MRubyState.ComputeIrepFingerprint). An irep is
    // immutable once built and its fingerprint depends only on its content, so the first
    // computation is cached here forever. Guard miss paths (poly-dispatch sites, first
    // calls, post-redefinition) and the build-time codegen would otherwise re-hash the
    // entire irep tree (bytecode + symbol names + pool + child ireps) on every lookup.
    internal ulong? CachedFingerprint;

    public bool TryFindCatchHandler(int pc, CatchHandlerType filter, out CatchHandler handler)
    {
        var noFilter = filter == CatchHandlerType.All;
        for (var i = CatchHandlers.Length - 1; i >= 0; i--)
        {
            var x = CatchHandlers[i];
            // The comparison operators use `>` and `<=` because pc already points to the next instruction
            if ((noFilter || x.HandlerType == filter) && pc > x.Begin && pc <= x.End)
            {
                handler = x;
                return true;
            }
        }
        handler = default;
        return false;
    }
}
