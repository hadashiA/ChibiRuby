using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif
using System.Threading;

namespace ChibiRuby.Internals;

public enum CallerType
{
    /// <summary>
    /// Called method from mruby VM
    /// </summary>
    InVmLoop,

    /// <summary>
    /// Ignited mruby VM from C#
    /// </summary>
    VmExecuted,

    /// <summary>
    /// Called method from C#
    /// </summary>
    MethodCalled,

    /// <summary>
    /// Resumed by `Fiber.yield` (probabily the main call is `mrb_fiber_resume`)
    /// </summary>
    Resumed,
}

public enum FiberState
{
    Created,
    Running,
    Resumed,
    Suspended,
    Transferred,
    Terminated
}

struct MRubyCallInfo
{
    public const int CallMaxArgs = 15;
    static readonly int CallVarArgs = (CallMaxArgs << 4) | CallMaxArgs;

    internal static int CalculateBlockArgumentOffset(int argc, int kargc)
    {
        var n = argc;
        if (argc == CallMaxArgs) n = 1;
        if (kargc == CallMaxArgs) n += 1;
        else n += kargc * 2;
        return n + 1; // self + args + kargs
    }

    internal static int CalculateKeywordArgumentOffset(int argc, int kargc)
    {
         if (kargc == 0) return -1;
        return argc == CallMaxArgs ? 2 : argc + 1;
    }

    public int StackPointer;
    public RProc? Proc;
    public int ProgramCounter;
    public byte ArgumentCount;
    public byte KeywordArgumentCount;
    // for stacktrace..
    public CallerType CallerType;
    public ICallScope Scope;
    internal ChibiRuby.REnv? OptimizedBlockEnvironment;
    internal ChibiRuby.RProc? OptimizedBlockUpperProc;
    internal int OptimizedBlockRegisterCount;
    internal int OptimizerRegisterVariableCount;
    public Symbol MethodId;
    public MRubyMethodVisibility Visibility;
    public bool VisibilityBreak;

    /// <summary>
    /// Bindings that hold a live reference to this frame's locals. Allocated lazily — null on
    /// the hot path. Walked by <see cref="MRubyContext.PopCallStack"/> just before the frame's
    /// slot is reused so each binding can copy its current register values into its own
    /// snapshot storage, keeping the binding usable after the frame is gone.
    /// </summary>
    public List<RBinding>? LiveBindings;

    public bool ArgumentPacked => ArgumentCount >= CallMaxArgs;
    public bool KeywordArgumentPacked => KeywordArgumentCount >= CallMaxArgs;
    public int KeywordArgumentOffset => CalculateKeywordArgumentOffset(ArgumentCount, KeywordArgumentCount);
    public int BlockArgumentOffset => CalculateBlockArgumentOffset(ArgumentCount, KeywordArgumentCount);

    public int NumberOfRegisters
    {
        get
        {
            var numberOfRegisters = BlockArgumentOffset + 1; // self + args + kargs + blk
            if (Proc is { } p && p.Irep.RegisterVariableCount > numberOfRegisters)
            {
                numberOfRegisters = p.Irep.RegisterVariableCount;
            }
            if (OptimizerRegisterVariableCount > numberOfRegisters)
            {
                numberOfRegisters = OptimizerRegisterVariableCount;
            }
            return numberOfRegisters;
        }
    }

    public bool KeepContext => Scope != null;

    public void Clear()
    {
        // Proc?.SetFlag(MRubyObjectFlags.ProcOrphan);
        Proc = null;
        Scope = null!;
        OptimizedBlockEnvironment = null;
        OptimizedBlockUpperProc = null;
        OptimizedBlockRegisterCount = 0;
        OptimizerRegisterVariableCount = 0;
        MethodId = default;
        ArgumentCount = 0;
        KeywordArgumentCount = 0;
        Visibility = MRubyMethodVisibility.Default;
        VisibilityBreak = false;
        LiveBindings = null;
    }

    public void MarkAsArgumentPacked()
    {
        ArgumentCount = CallMaxArgs;
    }

    public void MarkAsKeywordArgumentPacked()
    {
        KeywordArgumentCount = CallMaxArgs;
    }

    public void MarkContextModify()
    {
        Scope = null!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out short operand1)
    {
        var pc = ProgramCounter;
        operand1 = BinaryPrimitives.ReadInt16BigEndian(sequence[(pc + 1)..]);
        ProgramCounter += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        ProgramCounter += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out byte operand2)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = sequence[pc + 2];
        ProgramCounter += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out byte operand2, out byte operand3)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = sequence[pc + 2];
        operand3 = sequence[pc + 3];
        ProgramCounter += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out short operand2)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = BinaryPrimitives.ReadInt16BigEndian(sequence[(pc + 2)..]);
        ProgramCounter += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out short operand1, out byte operand2)
    {
        var pc = ProgramCounter;
        operand1 = BinaryPrimitives.ReadInt16BigEndian(sequence[(pc + 1)..]);
        operand2 = sequence[pc + 3];
        ProgramCounter += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out int operand2)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = BinaryPrimitives.ReadInt32BigEndian(sequence[(pc + 2)..]);
        ProgramCounter += 6;
    }
}

class MRubyContext
{
    const int CallStackInitSize = 128;
    const int StackInitSize = 32;
    const int CallDepthMax = 512;
    static int LastId = -1;

    public int CallDepth;
    public int Id { get; }

    public RFiber? Fiber { get; internal set; }
    public MRubyContext? Previous { get; internal set; }
    public FiberState State { get; internal set; } = FiberState.Created;
    public bool VmExecutedByFiber { get; internal set; }

    internal MRubyValue[] Stack  = new MRubyValue[StackInitSize];
    internal MRubyCallInfo[] CallStack = new MRubyCallInfo[CallStackInitSize];

    public ref MRubyCallInfo CurrentCallInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.Add(ref GetArrayDataReference(CallStack), CallDepth);
    }

    public Span<MRubyValue> CurrentStack
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var sp = CallStack[CallDepth].StackPointer;
            return Stack.AsSpan(sp);
        }
    }

    /// <summary>
    /// Slice the current call frame's positional arguments straight out of the VM stack
    /// as a <see cref="Span{T}"/>, taking a <c>ref</c> into the backing array so the common
    /// (unpacked) path skips the bounds validation that <c>Stack.AsSpan(start, length)</c>
    /// performs. Lets a C# builtin read its arguments without per-index
    /// <see cref="GetArgumentAt"/> calls. The packed case (argc &gt;= 15) falls back to the
    /// packed array's own span. Valid only while the current frame is active and the stack
    /// is not resized.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<MRubyValue> GetArgumentsSpan()
    {
        ref var callInfo = ref CurrentCallInfo;
        if (callInfo.ArgumentPacked)
        {
            return StackAt(callInfo.StackPointer + 1).As<RArray>().AsSpan();
        }
        return CreateSpan(ref StackAt(callInfo.StackPointer + 1), callInfo.ArgumentCount);
    }

    public MRubyContext()
    {
        CallStack[0] = new MRubyCallInfo();
        Id = Interlocked.Increment(ref LastId);
    }

    public bool CheckProcIsOrphan(RProc proc)
    {
        if (proc.Scope is REnv procEnv)
        {
            if (CallDepth > 0)
            {
                return CallStack[CallDepth - 1].Scope == procEnv;
            }
        }
        return false;
    }

    public void UnwindStack()
    {
        while (CallDepth > 0)
        {
            PopCallStack();
        }
    }

    public ref MRubyCallInfo PushCallStack()
    {
        EnsureStackLevel();

        if (CallStack.Length <= CallDepth + 1)
        {
            Array.Resize(ref CallStack, CallStack.Length * 2);
        }
        return ref Unsafe.Add(ref GetArrayDataReference(CallStack), ++CallDepth);
    }

    public void PopCallStack()
    {
        if (CallDepth <= 0)
        {
            throw new InvalidOperationException();
        }

        ref var currentCallInfo = ref Unsafe.Add(ref GetArrayDataReference(CallStack), CallDepth);
        ref var parentCallInfo = ref Unsafe.Add(ref GetArrayDataReference(CallStack), CallDepth - 1);

        var currentBlock = Stack[currentCallInfo.StackPointer + currentCallInfo.BlockArgumentOffset];
        if (currentBlock.Object is RProc b &&
            !b.HasFlag(MRubyObjectFlags.ProcStrict) &&
            b.Scope == parentCallInfo.Scope)
        {
            b.SetFlag(MRubyObjectFlags.ProcOrphan);
        }

        if (currentCallInfo.Scope is REnv currentEnv)
        {
            currentEnv.CaptureStack();
        }

        // Hand off live bindings that point at this frame to their own snapshot storage
        // before the slot is reused. The null check is the only cost paid on the hot path
        // when no debugger / binding is involved (branch-predicted "null").
        if (currentCallInfo.LiveBindings is { } liveBindings)
        {
            foreach (var binding in liveBindings)
            {
                binding.FreezeFromFrame(Stack, currentCallInfo.StackPointer);
            }
        }

        // currentCallInfo.
        currentCallInfo.Clear();
        CallDepth--;
    }

    public void UnwindStack(int to)
    {
        if ((uint)to >= (uint)Stack.Length)
        {
            throw new ArgumentOutOfRangeException();
        }
        CallDepth = to;
    }

    public Memory<MRubyValue> CaptureStack(int stackPointer)
    {
        return Stack.AsMemory(stackPointer);
    }

    public void ModifyCurrentMethodId(Symbol newMethodId)
    {
        CurrentCallInfo.MethodId = newMethodId;
    }

    public bool IsRecursiveCalling(Symbol methodId, MRubyValue self, int offset = 0)
    {
        for (var i = CallDepth - 1 - offset; i >= 0; i--)
        {
            ref var callInfo = ref CallStack[i];
            if (callInfo.MethodId == methodId &&
                Stack[callInfo.StackPointer] == self)
            {
                return true;
            }
        }
        return false;
    }

    public bool IsRecursiveCalling(Symbol methodId, MRubyValue self, MRubyValue arg0, int offset = 0)
    {
        for (var i = CallDepth - 1 - offset; i >= 0; i--)
        {
            ref var callInfo = ref CallStack[i];
            if (callInfo.MethodId == methodId &&
                Stack[callInfo.StackPointer] == self &&
                Stack[callInfo.StackPointer + 1] == arg0)
            {
                return true;
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExtendStack(int room)
    {
        if (Stack.Length <= room)
        {
            var newSize = Math.Max(128, Math.Max(Stack.Length * 2, room));
            Array.Resize(ref Stack, newSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearStack(int start, int count)
    {
        if (count <= 0) return;
        Stack.AsSpan(start, count).Clear();
    }

    /// <summary>
    /// Unchecked <c>ref</c> into the VM stack backing array. The VM guarantees stack indices
    /// derived from the active call frame are in range, so the per-access bounds check that
    /// <c>Stack[i]</c> emits is pure overhead on the hot argument-reading path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref MRubyValue StackAt(int index) => ref Unsafe.Add(ref GetArrayDataReference(Stack), index);

    public int GetArgumentCount()
    {
        ref var callInfo = ref CurrentCallInfo;
        if (callInfo.ArgumentPacked)
        {
            return StackAt(callInfo.StackPointer + 1).As<RArray>().Length;
        }
        return callInfo.ArgumentCount;
    }

    public int GetKeywordArgumentCount()
    {
        ref var callInfo = ref CurrentCallInfo;
        var offset = callInfo.KeywordArgumentOffset;
        if (callInfo.KeywordArgumentPacked)
        {
            return StackAt(callInfo.StackPointer + offset).As<RHash>().Length;
        }
        return callInfo.KeywordArgumentCount;
    }


    public bool TryGetArgumentAt(int index, out MRubyValue value)
    {
        ref var callInfo = ref CurrentCallInfo;
        if (callInfo.ArgumentPacked)
        {
            var args = StackAt(callInfo.StackPointer + 1).As<RArray>();
            if ((uint)index < (uint)args.Length)
            {
                value = args[index];
                return true;
            }
        }
        else
        {
            if ((uint)index < callInfo.ArgumentCount)
            {
                value = StackAt(callInfo.StackPointer + 1 + index);
                return true;
            }
        }
        value = default;
        return false;
    }

    public bool TryGetKeywordArgument(Symbol key, out MRubyValue value)
    {
        ref var callInfo = ref CurrentCallInfo;
        var offset = callInfo.KeywordArgumentOffset;
        if (offset < 0)
        {
            value = default;
            return false;
        }

        if (callInfo.KeywordArgumentPacked)
        {
            var kdict = StackAt(callInfo.StackPointer + offset).As<RHash>();
            return kdict.TryGetValue(new MRubyValue(key), out value);
        }

        ref var k0 = ref StackAt(callInfo.StackPointer + offset);
        for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
        {
            ref var k = ref Unsafe.Add(ref k0, i * 2);
            if (k.SymbolValue == key)
            {
                value = Unsafe.Add(ref k0, i * 2 + 1);
                return true;
            }
        }

        value = default;
        return false;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetSelf()
    {
        return StackAt(CurrentCallInfo.StackPointer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetArgumentAt(int index)
    {
        TryGetArgumentAt(index, out var result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetKeywordArgument(Symbol key)
    {
        ref var callInfo = ref CurrentCallInfo;
        var offset = callInfo.KeywordArgumentOffset;
        if (offset < 0)
        {
            return MRubyValue.Nil;
        }

        if (callInfo.KeywordArgumentPacked)
        {
            var hash = StackAt(callInfo.StackPointer + offset).As<RHash>();
            return hash[new MRubyValue(key)];
        }

        ref var k0 = ref StackAt(callInfo.StackPointer + offset);
        for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
        {
            ref var k = ref Unsafe.Add(ref k0, i);
            if (k.SymbolValue == key)
            {
                return Unsafe.Add(ref k0, i + 1);
            }
        }
        return MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<MRubyValue> GetRestArgumentsAfter(int startIndex) =>
        GetRestArgumentsAfter(ref CurrentCallInfo, startIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetBlockArgument()
    {
        ref var callInfo = ref CurrentCallInfo;
        return StackAt(callInfo.StackPointer + callInfo.BlockArgumentOffset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Span<MRubyValue> GetRestArgumentsAfter(ref MRubyCallInfo callInfo, int startIndex)
    {
        if (startIndex >= callInfo.ArgumentCount)
        {
            return default;
        }
        if (callInfo.ArgumentPacked)
        {
            var args = StackAt(callInfo.StackPointer + 1).As<RArray>();
            return startIndex >= args.Length ? default : args.AsSpan(startIndex);
        }
        return CreateSpan(
            ref StackAt(callInfo.StackPointer + 1 + startIndex),
            callInfo.ArgumentCount - startIndex);
    }

    internal ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> GetKeywordArgs(ref MRubyCallInfo callInfo)
    {
        var offset = callInfo.KeywordArgumentOffset;
        if (offset < 0)
        {
            return [];
        }

        var list = new List<KeyValuePair<Symbol, MRubyValue>>();
        if (callInfo.KeywordArgumentPacked)
        {
            var kdict = Stack[callInfo.StackPointer + offset].As<RHash>();
            foreach (var (k, v) in kdict)
            {
                list.Add(new KeyValuePair<Symbol, MRubyValue>(k.SymbolValue, v));
            }
        }
        else
        {
            for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
            {
                var k = Stack[callInfo.StackPointer + offset + i * 2];
                var v = Stack[callInfo.StackPointer + offset + i * 2 + 1];
                list.Add(new KeyValuePair<Symbol, MRubyValue>(k.SymbolValue, v));
            }
        }
        return CollectionsMarshal.AsSpan(list);
    }

    internal ref MRubyCallInfo FindClosestVisibilityScope(RClass? c, int n, out REnv? env)
    {
        ref var callInfo = ref CallStack[CallDepth - n];
        c ??= callInfo.Scope.TargetClass;

        var proc = callInfo.Proc;

        if (proc?.Upper is null ||
            proc.HasFlag(MRubyObjectFlags.ProcScope) ||
            proc?.Scope is not REnv ||
            callInfo.Scope.TargetClass != c ||
            callInfo.VisibilityBreak)
        {
            env = callInfo.Scope as REnv;
            return ref callInfo;
        }

        while (true)
        {
            env = proc.Scope as REnv;
            proc = proc.Upper;
            if (proc?.Upper is null ||
                proc.HasFlag(MRubyObjectFlags.ProcScope) ||
                env is null ||
                env.TargetClass != c ||
                env.VisibilityBreak)
            {
                return ref callInfo;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void EnsureStackLevel()
    {
        if (CallDepth >= CallDepthMax)
        {
            ThrowStacTooDeep();
        }

        static void ThrowStacTooDeep()
        {
            throw new InvalidOperationException("stack level too deep");
        }
    }
}
