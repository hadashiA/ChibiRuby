using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Internals;
using ChibiRuby.StdLib;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif

// ReSharper disable UnreachableSwitchArmDueToIntegerAnalysis

namespace ChibiRuby;

enum VmSignal : byte { Next, JumpAndNext, Return }

partial class MRubyState
{
    public MRubyValue Send(MRubyValue self, Symbol methodId) =>
        Send(self, methodId, ReadOnlySpan<MRubyValue>.Empty);

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0) =>
        Send(self, methodId, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1), null, null);

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, MRubyValue arg1)
    {
        var args = (arg0, arg1);
        return Send(
            self,
            methodId,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<(MRubyValue, MRubyValue), MRubyValue>(ref args), 2),
            null,
            null);
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, MRubyValue arg1, MRubyValue arg2)
    {
        var args = (arg0, arg1, arg2);
        return Send(
            self,
            methodId,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<(MRubyValue, MRubyValue, MRubyValue), MRubyValue>(ref args), 3),
            null,
            null);
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, MRubyValue arg1, MRubyValue arg2, MRubyValue arg3)
    {
        var args = (arg0, arg1, arg2, arg3);
        return Send(
            self,
            methodId,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<(MRubyValue, MRubyValue, MRubyValue, MRubyValue), MRubyValue>(ref args), 4),
            null,
            null);
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId, params ReadOnlySpan<MRubyValue> args) =>
        Send(self, methodId, args, null, null);

    public MRubyValue Send(
        MRubyValue self,
        Symbol methodId,
        RProc block) =>
        Send(self, methodId, ReadOnlySpan<MRubyValue>.Empty, null, block);

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, RProc block) =>
        Send(self, methodId, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1), null, block);

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, MRubyValue arg1, RProc block)
    {
        var args = (arg0, arg1);
        return Send(
            self,
            methodId,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<(MRubyValue, MRubyValue), MRubyValue>(ref args), 2),
            null,
            block);
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, MRubyValue arg1, MRubyValue arg2, RProc block)
    {
        var args = (arg0, arg1, arg2);
        return Send(
            self,
            methodId,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<(MRubyValue, MRubyValue, MRubyValue), MRubyValue>(ref args), 3),
            null,
            block);
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId, MRubyValue arg0, MRubyValue arg1, MRubyValue arg2, MRubyValue arg3, RProc block)
    {
        var args = (arg0, arg1, arg2, arg3);
        return Send(
            self,
            methodId,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<(MRubyValue, MRubyValue, MRubyValue, MRubyValue), MRubyValue>(ref args), 4),
            null,
            block);
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId, ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> kargs) =>
        Send(self, methodId, ReadOnlySpan<MRubyValue>.Empty, kargs, null);

    public MRubyValue Send(
        MRubyValue self,
        Symbol methodId,
        ReadOnlySpan<MRubyValue> args,
        ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> kargs,
        RProc? block)
    {
        ref var currentCallInfo = ref Context.CurrentCallInfo;
        var nextStackPointer = currentCallInfo.StackPointer + currentCallInfo.NumberOfRegisters;

        var stackSize = MRubyCallInfo.CalculateBlockArgumentOffset(
            args.Length,
            kargs.IsEmpty ? 0 : MRubyCallInfo.CallMaxArgs) + 1; // argc + kargs(packed) + self + proc
        Context.ExtendStack(nextStackPointer + stackSize);

        var nextStack = Context.Stack.AsSpan(nextStackPointer);

        var receiverClass = ClassOf(self);
        ref var nextCallInfo = ref Context.PushCallStack();
        nextCallInfo.StackPointer = nextStackPointer;
        nextCallInfo.Scope = receiverClass;
        nextCallInfo.ArgumentCount = (byte)args.Length;
        nextCallInfo.KeywordArgumentCount = (byte)kargs.Length;

        nextStack[0] = self;
        if (!args.IsEmpty)
        {
            // packing
            if (args.Length >= MRubyCallInfo.CallMaxArgs)
            {
                throw new NotImplementedException();
            }
            else
            {
                args.CopyTo(nextStack[1..]);
            }
        }

        if (!kargs.IsEmpty)
        {
            var kargOffset = MRubyCallInfo.CalculateKeywordArgumentOffset(args.Length, kargs.Length);
            // packing
            var kdict = NewHash(kargs.Length);
            foreach (var (key, value) in kargs)
            {
                kdict.Add(key, value);
            }

            nextStack[kargOffset] = kdict;
            nextCallInfo.MarkAsKeywordArgumentPacked();
        }

        nextStack[stackSize - 1] = block != null ? new MRubyValue(block) : default;

        if (TryFindMethod(receiverClass, methodId, out var method, out _) &&
            method != MRubyMethod.Undef)
        {
            nextCallInfo.MethodId = methodId;
        }
        else
        {
            method = PrepareMethodMissing(ref nextCallInfo, self, methodId);
        }

        nextCallInfo.Proc = method.Proc;

        // var block = stack[blockArgumentOffset];
        // if (!block.IsNil) EnsureValueIsBlock(block);

        if (method.Kind == MRubyMethodKind.CSharpFunc)
        {
            nextCallInfo.CallerType = CallerType.MethodCalled;
            nextCallInfo.ProgramCounter = 0;

            var result = method.Invoke(this, self);
            Context.PopCallStack();
            return result;
        }
        else
        {
            var irepProc = nextCallInfo.Proc!;
            nextCallInfo.CallerType = CallerType.VmExecuted;
            nextCallInfo.ProgramCounter = irepProc.ProgramCounter;
            return Execute(irepProc.Irep, irepProc.ProgramCounter, nextCallInfo.BlockArgumentOffset + 1);
        }
    }

    internal MRubyValue CallResolvedMethod(
        MRubyValue self,
        Symbol methodId,
        MRubyMethod method,
        RClass owner,
        ReadOnlySpan<MRubyValue> args,
        ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> kargs,
        RProc? block)
    {
        ref var currentCallInfo = ref Context.CurrentCallInfo;
        var nextStackPointer = currentCallInfo.StackPointer + currentCallInfo.NumberOfRegisters;

        var stackSize = MRubyCallInfo.CalculateBlockArgumentOffset(
            args.Length,
            kargs.IsEmpty ? 0 : MRubyCallInfo.CallMaxArgs) + 1;
        Context.ExtendStack(nextStackPointer + stackSize);

        var nextStack = Context.Stack.AsSpan(nextStackPointer);
        ref var nextCallInfo = ref Context.PushCallStack();
        nextCallInfo.StackPointer = nextStackPointer;
        nextCallInfo.Scope = owner;
        nextCallInfo.ArgumentCount = (byte)args.Length;
        nextCallInfo.KeywordArgumentCount = (byte)kargs.Length;
        nextCallInfo.MethodId = methodId;

        nextStack[0] = self;
        if (!args.IsEmpty)
        {
            if (args.Length >= MRubyCallInfo.CallMaxArgs)
            {
                throw new NotImplementedException();
            }
            args.CopyTo(nextStack[1..]);
        }

        if (!kargs.IsEmpty)
        {
            var kargOffset = MRubyCallInfo.CalculateKeywordArgumentOffset(args.Length, kargs.Length);
            var kdict = NewHash(kargs.Length);
            foreach (var (key, value) in kargs)
            {
                kdict.Add(key, value);
            }

            nextStack[kargOffset] = kdict;
            nextCallInfo.MarkAsKeywordArgumentPacked();
        }

        nextStack[stackSize - 1] = block != null ? new MRubyValue(block) : default;
        nextCallInfo.Proc = method.Proc;

        if (method.Kind == MRubyMethodKind.CSharpFunc)
        {
            nextCallInfo.CallerType = CallerType.MethodCalled;
            nextCallInfo.ProgramCounter = 0;

            var result = method.Invoke(this, self);
            Context.PopCallStack();
            return result;
        }

        var irepProc = nextCallInfo.Proc!;
        nextCallInfo.CallerType = CallerType.VmExecuted;
        nextCallInfo.ProgramCounter = irepProc.ProgramCounter;
        return Execute(irepProc.Irep, irepProc.ProgramCounter, nextCallInfo.BlockArgumentOffset + 1);
    }

    public MRubyValue YieldWithClass(
        RClass c,
        MRubyValue self,
        ReadOnlySpan<MRubyValue> args,
        RProc block)
    {
        ref var callInfo = ref Context.CurrentCallInfo;

        var stackSize = callInfo.NumberOfRegisters;
        ref var nextCallInfo = ref Context.PushCallStack();
        nextCallInfo.StackPointer = callInfo.StackPointer + stackSize;
        nextCallInfo.CallerType = CallerType.VmExecuted;
        nextCallInfo.MethodId = block.Scope is REnv env
            ? env.MethodId
            : callInfo.MethodId;
        nextCallInfo.Proc = block;
        nextCallInfo.Scope = c;
        nextCallInfo.VisibilityBreak = true;

        var nextStack = Context.Stack.AsSpan(nextCallInfo.StackPointer);
        nextStack[0] = self;

        if (args.Length >= MRubyCallInfo.CallMaxArgs)
        {
            // TODO: packing
            throw new NotImplementedException();
        }
        else
        {
            args.CopyTo(nextStack[1..]);
            nextCallInfo.ArgumentCount = (byte)args.Length;
        }

        nextCallInfo.KeywordArgumentCount = 0;

        return Execute(block.Irep, block.ProgramCounter, nextCallInfo.BlockArgumentOffset + 1);
    }

    public RProc CreateProc(Irep irep)
    {
        return new RProc(irep, 0, ProcClass)
        {
            Upper = null,
            Scope = ObjectClass
        };
    }

    public Irep ParseBytecode(ReadOnlySpan<byte> bytecode) => RiteParser.Parse(bytecode);

    public MRubyValue LoadBytecode(ReadOnlySpan<byte> bytecode)
    {
        var irep = RiteParser.Parse(bytecode);
        return Execute(irep);
    }

    public MRubyValue LoadBytecodeFile(string filePath)
    {
        var bytecode = File.ReadAllBytes(filePath);
        return LoadBytecode(bytecode);
    }

    public async Task<MRubyValue> LoadBytecodeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var bytecode = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return LoadBytecode(bytecode);
    }

    public MRubyValue Execute(Irep irep)
    {
        var proc = new RProc(irep, 0, ProcClass)
        {
            Upper = null,
            Scope = ObjectClass
        };

        Context.UnwindStack();

        ref var callInfo = ref Context.CurrentCallInfo;
        callInfo.StackPointer = 0;
        callInfo.Proc = proc;
        callInfo.Scope = ObjectClass;
        callInfo.MethodId = default;
        callInfo.CallerType = CallerType.InVmLoop;
        callInfo.VisibilityBreak = true;
        Context.Stack[0] = TopSelf;
        return Execute(irep, 0, 1);
    }

    public string GetBacktraceString()
    {
        var backtrace = Backtrace.Capture(Context);
        return backtrace.ToString(this);
    }

    internal bool CheckProcIsOrphan(RProc proc) =>
        Context.CheckProcIsOrphan(proc);

    internal MRubyValue SendMeta(MRubyValue self)
    {
        ref var callInfo = ref Context.CurrentCallInfo;

        var argc = GetArgumentCount();
        if (argc <= 0)
        {
            RaiseArgumentNumberError(argc, 1, 255);
        }

        var methodId = GetArgumentAsSymbolAt(0);
        if (callInfo.CallerType != CallerType.InVmLoop)
        {
            var block = GetBlockArgument();
            var args = GetRestArgumentsAfter(1);
            var kargs = GetKeywordArguments();
            return Send(self, methodId, args, kargs, block);
        }

        var registers = Context.Stack.AsSpan(callInfo.StackPointer + 1);
        var receiverClass = ClassOf(self);

        if (TryFindMethod(receiverClass, methodId, out var method, out receiverClass))
        {
            callInfo.MethodId = methodId;
            callInfo.Scope = receiverClass;
        }
        else
        {
            method = PrepareMethodMissing(ref callInfo, self, methodId);
        }

        if (callInfo.ArgumentPacked)
        {
            var packedArgv = registers[0].As<RArray>();
            var nextArgumentCount = packedArgv.Length - 1;
            if (callInfo.KeywordArgumentCount == 0 && nextArgumentCount < MRubyCallInfo.CallMaxArgs)
            {
                var block = Context.Stack[callInfo.StackPointer + callInfo.BlockArgumentOffset];
                Context.ExtendStack(
                    callInfo.StackPointer +
                    MRubyCallInfo.CalculateBlockArgumentOffset(nextArgumentCount, 0) + 1);
                registers = Context.Stack.AsSpan(callInfo.StackPointer + 1);
                packedArgv.AsSpan(1, nextArgumentCount).CopyTo(registers);
                callInfo.ArgumentCount = (byte)nextArgumentCount;
                registers[callInfo.ArgumentCount] = block;
            }
            else
            {
                registers[0] = packedArgv.SubSequence(1, nextArgumentCount);
            }
        }
        else
        {
            // __send__/send: drop the method name in register[0] by shifting the remaining args
            var argumentCount = callInfo.ArgumentCount;
            var slots = callInfo.KeywordArgumentCount == 0
                ? argumentCount
                : callInfo.BlockArgumentOffset - 1;
            ref var head = ref MemoryMarshal.GetReference(registers);
            for (var i = 0; i < slots; i++)
            {
                Unsafe.Add(ref head, i) = Unsafe.Add(ref head, i + 1);
            }
            callInfo.ArgumentCount = (byte)(argumentCount - 1); // remove method name
        }

        // var block = stack[blockArgumentOffset];
        // if (!block.IsNil) EnsureValueIsBlock(block);

        if (method.Kind == MRubyMethodKind.CSharpFunc)
        {
            callInfo.CallerType = CallerType.MethodCalled;
            callInfo.ProgramCounter = 0;

            return method.Invoke(this, self);
        }
        else
        {
            callInfo.CallerType = CallerType.VmExecuted;
            callInfo.Proc = method.Proc;
            callInfo.ProgramCounter = method.Proc!.ProgramCounter;

            var nregs = method.Proc.Irep.RegisterVariableCount;
            var keep = callInfo.BlockArgumentOffset + 1;
            if (nregs > keep)
            {
                Context.ExtendStack(callInfo.StackPointer + nregs);
                Context.ClearStack(callInfo.StackPointer + keep, nregs - keep);
            }

            // dummy. pop after `__send__` called.
            ref var nextCallInfo = ref Context.PushCallStack();
            nextCallInfo.MethodId = default;
            nextCallInfo.Proc = null;
            nextCallInfo.StackPointer = callInfo.StackPointer;
            callInfo.CallerType = CallerType.InVmLoop;
            callInfo.Scope = receiverClass;

            return self;
        }
    }

    internal MRubyValue EvalUnder(MRubyValue self, RProc block, RClass c)
    {
        ref var callInfo = ref Context.CurrentCallInfo;
        if (callInfo.CallerType == CallerType.MethodCalled)
        {
            return YieldWithClass(c, self, [self], block);
        }

        callInfo.Scope = c;
        callInfo.Proc = block;
        callInfo.ProgramCounter = block.ProgramCounter;
        callInfo.ArgumentCount = 0;
        callInfo.KeywordArgumentCount = 0;
        callInfo.MethodId = Context.CallStack[Context.CallDepth - 1].MethodId;
        callInfo.VisibilityBreak = true;

        var nregs = block.Irep.RegisterVariableCount < 4 ? 4 : block.Irep.RegisterVariableCount;
        Context.ExtendStack(nregs);
        Context.Stack[callInfo.StackPointer] = self;
        Context.Stack[callInfo.StackPointer + 1] = self;
        Context.ClearStack(callInfo.StackPointer + 2, nregs - 2);

        // Popped at the end of an upstream method call such as instance_eval/class_eval, and the above rewritten callInfo is executed.
        Context.PushCallStack();
        return self;
    }

    /// <summary>
    /// Execute irep assuming the Stack values are placed
    /// </summary>
    // Inline-cache miss paths for variable-access opcodes. Kept out of line so the
    // dispatch switch's hot case bodies stay small; the caches store the found slot,
    // and every cached slot is re-verified by VariableTable.TryGetAt/TrySetAt.

    [MethodImpl(MethodImplOptions.NoInlining)]
    static MRubyValue GetVariableMiss(VariableTable table, Symbol id, ushort[] slotCache, int cacheIndex)
    {
        slotCache[cacheIndex] = unchecked((ushort)table.GetWithSlot(id, out var value));
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SetVariableMiss(VariableTable table, Symbol id, MRubyValue value, ushort[] slotCache, int cacheIndex)
    {
        slotCache[cacheIndex] = unchecked((ushort)table.SetWithSlot(id, value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static MRubyValue TrivialGetterMiss(ref MethodCacheEntry entry, VariableTable table)
    {
        entry.TrivialGetterSlot = unchecked((ushort)table.GetWithSlot(entry.Method.TrivialGetterIVarSymbol, out var value));
        return value;
    }

    internal MRubyValue Execute(Irep irep, int pc, int stackKeep, RException? injectedRaise = null)
    {
        Exception = null;

        var registerVariableCount = irep.RegisterVariableCount;
        if (stackKeep > registerVariableCount)
        {
            registerVariableCount = (ushort)stackKeep;
        }
        // else
        // {
        //     if (context.CurrentCallInfo.Scope is REnv env &&
        //         (stackKeep == 0 || irep.LocalVariables.Length < env.Stack.Length))
        //     {
        //         context.CurrentCallInfo.Scope = null!;
        //         env.CaptureStack();
        //     }
        // }

        ref var sequence = ref GetArrayDataReference(irep.Sequence);
        ref var symbols = ref GetArrayDataReference(irep.Symbols);

        ref var callInfo = ref Context.CurrentCallInfo;
        Context.ExtendStack(callInfo.StackPointer + registerVariableCount);
        Context.ClearStack(callInfo.StackPointer + stackKeep, registerVariableCount - stackKeep);

        ref var registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
        callInfo.ProgramCounter = pc;

        // Inject a raise at the resume point so a Ruby `rescue` wrapping the
        // suspension (e.g. begin ... sleep ... rescue ... end) catches it.
        // Search rescue handlers exactly as if the raise originated at the
        // current PC; if none match, propagate out of the fiber as before.
        if (injectedRaise is not null)
        {
            var ex = new MRubyRaiseException(this, injectedRaise, Context.CallDepth);
            Exception = ex;
            if (!TryRaiseJump(ref Context.CurrentCallInfo))
            {
                throw ex;
            }
            callInfo = ref Context.CurrentCallInfo;
            irep = callInfo.Proc!.Irep;
            registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
            sequence = ref GetArrayDataReference(irep.Sequence);
            symbols = ref GetArrayDataReference(irep.Symbols);
        }

        while (true)
        {
            try
            {
                // Debugger hook: fires once per fetched opcode while a debugger is attached.
                // The null check is the only cost paid when no debugger is installed
                // (a predictable branch the JIT can speculate through). Once attached the
                // hook itself is expected to early-out unless something interesting (a
                // breakpoint, step trap, etc.) actually applies to this pc.
                DebuggerHook?.OnInstruction(this, irep, callInfo.ProgramCounter);

                var opcode = (OpCode)Unsafe.Add(ref sequence, callInfo.ProgramCounter);
                switch (opcode)
                {
                    case OpCode.Nop:
                        Markers.Nop();
                    {
                        callInfo.ProgramCounter++;
                        goto Next;
                    }
                    case OpCode.Move:
                        Markers.Move();
                        var bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = Unsafe.Add(ref registers, bb.B);
                        goto Next;
                    case OpCode.LoadL:
                        Markers.LoadL();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = irep.PoolValues[bb.B];
                        goto Next;
                    case OpCode.LoadI8:
                        Markers.LoadI8();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = bb.B;
                        goto Next;
                    case OpCode.LoadINeg:
                        Markers.LoadINeg();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = -bb.B;
                        goto Next;
                    case OpCode.LoadI__1:
                    case OpCode.LoadI_0:
                    case OpCode.LoadI_1:
                    case OpCode.LoadI_2:
                    case OpCode.LoadI_3:
                    case OpCode.LoadI_4:
                    case OpCode.LoadI_5:
                    case OpCode.LoadI_6:
                    case OpCode.LoadI_7:
                        Markers.LoadI__1();
                        int a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = (int)opcode - (int)OpCode.LoadI_0;
                        goto Next;
                    case OpCode.LoadI16:
                        Markers.LoadI16();
                        var bs = OperandBS.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bs.A) = unchecked((short)bs.B);
                        goto Next;
                    case OpCode.LoadI32:
                        Markers.LoadI32();
                        var bss = OperandBSS.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bss.A) = NewIntegerFlex((bss.B << 16) + bss.C);
                        goto Next;
                    case OpCode.LoadSym:
                        Markers.LoadSym();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = Unsafe.Add(ref symbols, bb.B);
                        goto Next;
                    case OpCode.LoadNil:
                        Markers.LoadNil();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = default;
                        goto Next;
                    case OpCode.LoadSelf:
                        Markers.LoadSelf();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = Unsafe.Add(ref registers, 0);
                        goto Next;
                    case OpCode.LoadT:
                        Markers.LoadT();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = MRubyValue.True;
                        goto Next;
                    case OpCode.LoadF:
                        Markers.LoadF();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = MRubyValue.False;
                        goto Next;
                    case OpCode.GetGV:
                    {
                        Markers.GetGV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var slotCache = irep.VariableSlotCache;
                        if (!globalVariables.TryGetAt(slotCache[bb.B], Unsafe.Add(ref symbols, bb.B), out var value))
                        {
                            value = GetVariableMiss(globalVariables, Unsafe.Add(ref symbols, bb.B), slotCache, bb.B);
                        }
                        Unsafe.Add(ref registers, bb.A) = value;
                        goto Next;
                    }
                    case OpCode.SetGV:
                    {
                        Markers.SetGV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var slotCache = irep.VariableSlotCache;
                        if (!globalVariables.TrySetAt(slotCache[bb.B], Unsafe.Add(ref symbols, bb.B), Unsafe.Add(ref registers, bb.A)))
                        {
                            SetVariableMiss(globalVariables, Unsafe.Add(ref symbols, bb.B), Unsafe.Add(ref registers, bb.A), slotCache, bb.B);
                        }
                        goto Next;
                    }
                    case OpCode.GetSV:
                        Markers.GetSV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = globalVariables.Get(Unsafe.Add(ref symbols, bb.B));
                        goto Next;
                    case OpCode.SetSV:
                        Markers.SetSV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        globalVariables.Set(Unsafe.Add(ref symbols, bb.B), Unsafe.Add(ref registers, bb.A));
                        goto Next;
                    case OpCode.GetIV:
                    {
                        Markers.GetIV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var table = Unsafe.Add(ref registers, 0).As<RObject>().InstanceVariables;
                        var slotCache = irep.VariableSlotCache;
                        if (!table.TryGetAt(slotCache[bb.B], Unsafe.Add(ref symbols, bb.B), out var value))
                        {
                            value = GetVariableMiss(table, Unsafe.Add(ref symbols, bb.B), slotCache, bb.B);
                        }
                        Unsafe.Add(ref registers, bb.A) = value;
                        goto Next;
                    }
                    case OpCode.SetIV:
                    {
                        Markers.SetIV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var table = Unsafe.Add(ref registers, 0).As<RObject>().InstanceVariables;
                        var slotCache = irep.VariableSlotCache;
                        if (!table.TrySetAt(slotCache[bb.B], Unsafe.Add(ref symbols, bb.B), Unsafe.Add(ref registers, bb.A)))
                        {
                            SetVariableMiss(table, Unsafe.Add(ref symbols, bb.B), Unsafe.Add(ref registers, bb.A), slotCache, bb.B);
                        }
                        goto Next;
                    }
                    case OpCode.GetCV:
                        Markers.GetCV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, bb.A) = GetClassVariable(Unsafe.Add(ref symbols, bb.B));
                        goto Next;
                    case OpCode.SetCV:
                        Markers.SetCV();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        SetClassVariable(Unsafe.Add(ref symbols, bb.B), Unsafe.Add(ref registers, bb.A));
                        goto Next;

                    case OpCode.GetConst:
                        Markers.GetConst();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        ref var registerA = ref Unsafe.Add(ref registers, bb.A);
                    {
                        var id = Unsafe.Add(ref symbols, bb.B);
                        var c = callInfo.Proc?.Scope?.TargetClass ?? ObjectClass;
                        var slotCache = irep.VariableSlotCache;
                        if (c.ClassInstanceVariables.TryGetAt(slotCache[bb.B], id, out var value))
                        {
                            registerA = value;
                            goto Next;
                        }

                        GetConstMiss(
                            this, ref registerA, ref callInfo, id, c, slotCache, bb.B);

                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void GetConstMiss(MRubyState state, ref MRubyValue registerA, ref MRubyCallInfo callInfo, Symbol id, RClass c, ushort[] slotCache, int cacheIndex)
                        {
                            var slot = c.ClassInstanceVariables.GetWithSlot(id, out var found);
                            if (slot >= 0)
                            {
                                slotCache[cacheIndex] = unchecked((ushort)slot);
                                registerA = found;
                                return;
                            }
                            GetConstSlowPath(state, ref registerA, ref callInfo, id, c);
                        }

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void GetConstSlowPath(MRubyState state, ref MRubyValue registerA, ref MRubyCallInfo callInfo, Symbol id, RClass c)
                        {
                            var x = c;
                            MRubyValue value;
                            while (x is { VType: MRubyVType.SClass })
                            {
                                if (!x.ClassInstanceVariables.TryGet(id, out value))
                                {
                                    x = null;
                                    break;
                                }
                                x = c.Class;
                            }
                            if (x is { VType: MRubyVType.Class or MRubyVType.Module })
                            {
                                c = x;
                            }
                            var proc = callInfo.Proc?.Upper;
                            while (proc != null)
                            {
                                x = proc.Scope?.TargetClass ?? state.ObjectClass;
                                if (x.ClassInstanceVariables.TryGet(id, out value))
                                {
                                    registerA = value;
                                    return;
                                }
                                proc = proc.Upper;
                            }
                            registerA = state.GetConst(id, c);
                        }
                    }
                    case OpCode.SetConst:
                    {
                        Markers.SetConst();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        //var id = Unsafe.Add(ref symbols, bb.B);
                        var c = callInfo.Proc?.Scope?.TargetClass ?? ObjectClass;
                        SetConst(Unsafe.Add(ref symbols, bb.B), c, Unsafe.Add(ref registers, bb.A));
                        goto Next;
                    }
                    case OpCode.GetMCnst:
                        Markers.GetMCnst();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bb.A);
                    {
                        //var mod = Unsafe.Add(ref registers, bb.A);
                        var name = Unsafe.Add(ref symbols, bb.B);
                        registerA = GetConst(name, registerA.As<RClass>());
                        goto Next;
                    }
                    case OpCode.SetMCnst:
                    {
                        Markers.SetMCnst();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bb.A);
                        //var mod = Unsafe.Add(ref registers, bb.A + 1);
                        var name = Unsafe.Add(ref symbols, bb.B);
                        SetConst(name, Unsafe.Add(ref registerA, 1).As<RClass>(), registerA);
                        goto Next;
                    }
                    case OpCode.GetIdx:
                    {
                        Markers.GetIdx();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        var valueB = Unsafe.Add(ref registerA, 1);
                        switch (registerA.Object)
                        {
                            case RArray array when valueB.IsFixnum && array.Class == ArrayClass:
                                registerA = array[(int)valueB.FixnumValue];
                                goto Next;
                            case RHash hash when hash.Class == HashClass:
                                registerA = hash.GetValueOrDefault(valueB, this);
                                goto Next;
                            case RString str when str.Class == StringClass:
                                switch (valueB.VType)
                                {
                                    case MRubyVType.Integer:
                                    case MRubyVType.String:
                                    case MRubyVType.Range:
                                        var substr = str.GetPartial(this, valueB);
                                        registerA = substr != null
                                            ? new MRubyValue(substr)
                                            : default;
                                        goto Next;
                                }
                                break;
                        }

                        // Jump to send :[]
                        Unsafe.Add(ref registerA, 2) = default; // push nil after arguments
                        callInfo = ref GetNextCallInfo(callInfo.StackPointer + a, opcode, 1);
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.GetIdx0:
                    {
                        Markers.GetIdx0();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var recv = Unsafe.Add(ref registers, bb.B);
                        switch (recv.Object)
                        {
                            case RArray array when array.Class == ArrayClass:
                                Unsafe.Add(ref registers, bb.A) = array.Length > 0
                                    ? array[0]
                                    : default;
                                goto Next;
                            case RHash hash when hash.Class == HashClass:
                                Unsafe.Add(ref registers, bb.A) = hash.GetValueOrDefault(0, this);
                                goto Next;
                        }

                        // Fallback to send :[](0) — set up call frame at register A.
                        Unsafe.Add(ref registers, bb.A) = recv;
                        Unsafe.Add(ref registers, bb.A + 1) = 0;
                        Unsafe.Add(ref registers, bb.A + 2) = default; // push nil after arguments
                        callInfo = ref GetNextCallInfo(callInfo.StackPointer + bb.A, OpCode.GetIdx, 1);
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.SetIdx:
                    {
                        Markers.SetIdx();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        var keyVal = Unsafe.Add(ref registerA, 1);
                        var setVal = Unsafe.Add(ref registerA, 2);
                        switch (registerA.Object)
                        {
                            case RArray array when keyVal.IsFixnum && array.Class == ArrayClass
                                                                    && !array.HasFlag(MRubyObjectFlags.Frozen):
                                array.Set((int)keyVal.FixnumValue, setVal);
                                registerA = setVal;
                                goto Next;
                            case RHash hash when hash.Class == HashClass
                                                 && !hash.HasFlag(MRubyObjectFlags.Frozen):
                                hash[keyVal] = setVal;
                                registerA = setVal;
                                goto Next;
                        }

                        Unsafe.Add(ref registers, a + 3) = default; // push nil after arguments
                        // Jump to send :[]=
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref Context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpAset;
                        callInfo.ArgumentCount = 2;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.GetUpVar:
                        Markers.GetUpVar();
                        OperandBBB bbb;
                    {
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var env = callInfo.Proc?.FindUpperEnvTo(bbb.C);
                        if (env != null && bbb.B < env.Stack.Length)
                        {
                            Unsafe.Add(ref registers, bbb.A) = env.Stack[bbb.B];
                        }
                        else
                        {
                            Unsafe.Add(ref registers, bbb.A) = default;
                        }
                        goto Next;
                    }
                    case OpCode.SetUpVar:
                    {
                        Markers.SetUpVar();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var env = callInfo.Proc?.FindUpperEnvTo(bbb.C);
                        if (env != null && bbb.B < env.Stack.Length)
                        {
                            env.Stack[bbb.B] = Unsafe.Add(ref registers, bbb.A);
                        }
                        goto Next;
                    }
                    case OpCode.Jmp:
                        Markers.Jmp();
                        var s = (short)ReadOperandS(ref sequence, ref callInfo.ProgramCounter);

                        callInfo.ProgramCounter += s;
                        goto Next;
                    case OpCode.JmpIf:
                        Markers.JmpIf();
                        bs = OperandBS.Read(ref sequence, ref callInfo.ProgramCounter);
                        if (Unsafe.Add(ref registers, bs.A).Truthy)
                        {
                            callInfo.ProgramCounter += bs.B;
                        }
                        goto Next;
                    case OpCode.JmpNot:
                        Markers.JmpNot();
                        bs = OperandBS.Read(ref sequence, ref callInfo.ProgramCounter);
                        if (Unsafe.Add(ref registers, bs.A).Falsy)
                        {
                            callInfo.ProgramCounter += bs.B;
                        }
                        goto Next;
                    case OpCode.JmpNil:
                        Markers.JmpNil();
                        bs = OperandBS.Read(ref sequence, ref callInfo.ProgramCounter);
                        if (Unsafe.Add(ref registers, bs.A).IsNil)
                        {
                            callInfo.ProgramCounter += bs.B;
                        }
                        goto Next;

                    // --- Hot arithmetic/comparison opcodes placed early for JIT inline budget ---
                    case OpCode.Add:
                    case OpCode.Sub:
                    case OpCode.Mul:
                    case OpCode.Div:
                        Markers.Add();
                        Markers.Sub();
                        Markers.Mul();
                        Markers.Div();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        var rhs = Unsafe.Add(ref registerA, 1);

                        var lhsIsFloat = registerA.IsFloat;
                        var rhsIsFloat = rhs.IsFloat;

                        // Float fast path: bit check only (bits & 0b11 == 0b10), avoids VType call
                        if (lhsIsFloat && rhsIsFloat)
                        {
                            var leftVal = registerA.FloatValue;
                            var rightVal = rhs.FloatValue;
                            registerA = new MRubyValue(opcode switch
                            {
                                OpCode.Add => leftVal + rightVal,
                                OpCode.Sub => leftVal - rightVal,
                                OpCode.Mul => leftVal * rightVal,
                                OpCode.Div => leftVal / rightVal,
                                _ => default
                            });
                            goto Next;
                        }

                        var lhsIsFixnum = registerA.IsFixnum;
                        var rhsIsFixnum = rhs.IsFixnum;

                        // Fixnum fast path: tagged-bits arithmetic operates directly on the
                        // tagged 64-bit immediate. The fixnum range maps bijectively onto the
                        // long range via (value << 1) | 1, so two's-complement long-overflow
                        // on the tagged form exactly matches fixnum-range overflow. On
                        // overflow we fall through to ArithmeticSlowPath which raises the
                        // proper Ruby RangeError.
                        if (lhsIsFixnum && rhsIsFixnum)
                        {
                            long rhsTemp;
                            long resultBits = 0;
                            var overflow = false;
                            switch (opcode)
                            {
                                case OpCode.Add:
                                    // (a<<1|1) + (b<<1|1) - 1 = ((a+b)<<1) | 1, computed as a + (b - 1)
                                    rhsTemp = unchecked(rhs.bits - 1);
                                    resultBits = unchecked(registerA.bits + rhsTemp);
                                    overflow = ((registerA.bits ^ resultBits) & (rhsTemp ^ resultBits)) < 0;
                                    break;
                                case OpCode.Sub:
                                    // (a<<1|1) - (b<<1|1) + 1 = ((a-b)<<1) | 1, computed as a - (b - 1)
                                    rhsTemp = unchecked(rhs.bits - 1);
                                    resultBits = unchecked(registerA.bits - rhsTemp);
                                    overflow = ((registerA.bits ^ rhsTemp) & (registerA.bits ^ resultBits)) < 0;
                                    break;
                                case OpCode.Mul:
                                {
                                    // (a) * (b<<1) + 1 = ((a*b)<<1) | 1
                                    rhsTemp = unchecked(rhs.bits - 1);
                                    var aUnboxed = registerA.bits >> 1;
                                    resultBits = unchecked(aUnboxed * rhsTemp);
                                    overflow = aUnboxed != 0 &&
                                               (aUnboxed == -1
                                                   ? rhsTemp == long.MinValue
                                                   : resultBits / aUnboxed != rhsTemp);
                                    resultBits = unchecked(resultBits + 1);
                                    break;
                                }
                                case OpCode.Div:
                                    rhsTemp = rhs.FixnumValue;
                                    if (rhsTemp == 0) IntegerMembers.RaiseDivideByZeroError(this);
                                    // Div has no tagged shortcut: both operands need full unboxing.
                                    registerA = new MRubyValue(registerA.FixnumValue / rhsTemp);
                                    goto Next;
                            }
                            if (!overflow)
                            {
                                registerA = new MRubyValue(resultBits, null);
                                goto Next;
                            }
                            // overflow: fall through to slow path
                        }

                        // Float + Fixnum mixed fast path: bit checks only, no VType call
                        if (lhsIsFloat && rhsIsFixnum)
                        {
                            var leftVal = registerA.FloatValue;
                            var rightVal = (double)rhs.FixnumValue;
                            registerA = new MRubyValue(opcode switch
                            {
                                OpCode.Add => leftVal + rightVal,
                                OpCode.Sub => leftVal - rightVal,
                                OpCode.Mul => leftVal * rightVal,
                                OpCode.Div => leftVal / rightVal,
                                _ => default
                            });
                            goto Next;
                        }
                        if (lhsIsFixnum && rhsIsFloat)
                        {
                            var leftVal = (double)registerA.FixnumValue;
                            var rightVal = rhs.FloatValue;
                            registerA = new MRubyValue(opcode switch
                            {
                                OpCode.Add => leftVal + rightVal,
                                OpCode.Sub => leftVal - rightVal,
                                OpCode.Mul => leftVal * rightVal,
                                OpCode.Div => leftVal / rightVal,
                                _ => default
                            });
                            goto Next;
                        }

                        if (ArithmeticSlowPath(this, ref registerA, a, registerA, rhs, lhsIsFixnum, lhsIsFloat, rhsIsFixnum, rhsIsFloat, opcode))
                            goto Next;
                        // fallthrough to Send
                        callInfo = ref GetNextCallInfo(callInfo.StackPointer + a, opcode, 1);
                        goto case OpCode.SendInternal;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static bool ArithmeticSlowPath(MRubyState state, ref MRubyValue registerA, int a, MRubyValue lhs, MRubyValue rhs, bool lhsIsFixnum, bool lhsIsFloat, bool rhsIsFixnum, bool rhsIsFloat, OpCode opcode)
                        {
                            var lhsIsInteger = lhsIsFixnum || lhs.Object?.VType == MRubyVType.Integer;
                            var rhsIsInteger = rhsIsFixnum || rhs.Object?.VType == MRubyVType.Integer;
                            if (lhsIsInteger && rhsIsInteger)
                            {
                                var leftInt = lhs.IntegerValue;
                                var rightInt = rhs.IntegerValue;
                                long result;
                                switch (opcode)
                                {
                                    case OpCode.Add:
                                        result = leftInt + rightInt;
                                        if (((leftInt ^ result) & (rightInt ^ result)) < 0)
                                            IntegerMembers.RaiseIntegerOverflowError(state, "add"u8);
                                        break;
                                    case OpCode.Sub:
                                        result = leftInt - rightInt;
                                        if (((leftInt ^ rightInt) & (leftInt ^ result)) < 0)
                                            IntegerMembers.RaiseIntegerOverflowError(state, "sub"u8);
                                        break;
                                    case OpCode.Mul:
                                        result = leftInt * rightInt;
                                        if (leftInt != 0 && (leftInt == -1
                                                ? rightInt == long.MinValue
                                                : result / leftInt != rightInt))
                                            IntegerMembers.RaiseIntegerOverflowError(state, "mul"u8);
                                        break;
                                    case OpCode.Div:
                                        if (rightInt == 0)
                                            IntegerMembers.RaiseDivideByZeroError(state);
                                        if (leftInt == long.MinValue && rightInt == -1)
                                            IntegerMembers.RaiseIntegerOverflowError(state, "div"u8);
                                        result = leftInt / rightInt;
                                        break;
                                    default:
                                        result = 0;
                                        break;
                                }
                                registerA = new MRubyValue(result);
                                return true;
                            }

                            if ((lhsIsInteger || lhsIsFloat) &&
                                (rhsIsInteger || rhsIsFloat))
                            {
                                var leftVal = lhsIsInteger ? lhs.IntegerValue : lhs.FloatValue;
                                var rightVal = rhsIsInteger ? rhs.IntegerValue : rhs.FloatValue;
                                registerA = new MRubyValue(opcode switch
                                {
                                    OpCode.Add => leftVal + rightVal,
                                    OpCode.Sub => leftVal - rightVal,
                                    OpCode.Mul => leftVal * rightVal,
                                    OpCode.Div => leftVal / rightVal,
                                    _ => default
                                });
                                return true;
                            }

                            if (lhs.Object?.VType == MRubyVType.String &&
                                rhs.Object?.VType == MRubyVType.String &&
                                opcode == OpCode.Add)
                            {
                                registerA = lhs.As<RString>() + rhs.As<RString>();
                                return true;
                            }

                            return false; // fallthrough to Send
                        }

                    case OpCode.AddILV:
                    case OpCode.SubILV:
                        Markers.AddILV();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bbb.A);
                    {
                        var rV = opcode == OpCode.AddILV ? bbb.C : -bbb.C;

                        if (registerA.IsFloat)
                        {
                            registerA = new MRubyValue(registerA.FloatValue + rV);
                            goto Next;
                        }
                        if (registerA.IsFixnum)
                        {
                            // Tagged-bits add: bits + (rV << 1) keeps the LSB tag in place.
                            long lhsBits = registerA.bits;
                            long delta = (long)rV << 1;
                            long resultBits = unchecked(lhsBits + delta);
                            if (((lhsBits ^ resultBits) & (delta ^ resultBits)) >= 0)
                            {
                                registerA = new MRubyValue(resultBits, null);
                                goto Next;
                            }
                            // overflow: fall through to slow path which raises IntegerOverflowError
                        }

                        // Slow path for boxed Integer/Float
                        var equivalentOp = opcode == OpCode.AddILV ? OpCode.AddI : OpCode.SubI;
                        switch (registerA.VType)
                        {
                            case MRubyVType.Integer:
                            {
                                var intVal = registerA.IntegerValue;
                                var result = intVal + rV;
                                if (((intVal ^ result) & ((long)rV ^ result)) < 0)
                                    IntegerMembers.RaiseIntegerOverflowError(this,
                                        equivalentOp == OpCode.AddI ? "add"u8 : "sub"u8);
                                registerA = result;
                                goto Next;
                            }
                            case MRubyVType.Float:
                                registerA = new MRubyValue(registerA.FloatValue + rV);
                                goto Next;
                        }

                        // fallthrough to Send (using register A+1 as the arg slot)
                        Unsafe.Add(ref registerA, 1) = new MRubyValue(bbb.C);
                        callInfo = ref GetNextCallInfo(callInfo.StackPointer + bbb.A, equivalentOp, 1);
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.AddI:
                    case OpCode.SubI:
                        Markers.AddI();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bb.A);
                    {

                        var rV = opcode == OpCode.AddI ? bb.B : -bb.B;
                        // Float fast path: bit check only, avoids VType call
                        if (registerA.IsFloat)
                        {
                            registerA = new MRubyValue(registerA.FloatValue + rV);
                            goto Next;
                        }

                        // Fixnum fast path: tagged-bits add with sign-bit overflow check.
                        if (registerA.IsFixnum)
                        {
                            var lhsBits = registerA.bits;
                            var delta = (long)rV << 1;
                            var resultBits = unchecked(lhsBits + delta);
                            if (((lhsBits ^ resultBits) & (delta ^ resultBits)) >= 0)
                            {
                                registerA = new MRubyValue(resultBits, null);
                                goto Next;
                            }
                            // overflow: fall through to slow path which raises IntegerOverflowError
                        }

                        if (AddISubISlowPath(this, ref registerA, registerA, rV, opcode))
                            goto Next;
                        // fallthrough to Send
                        Unsafe.Add(ref registerA, 1) = new MRubyValue(bb.B);
                        callInfo = ref GetNextCallInfo(callInfo.StackPointer + bb.A, opcode, 1);
                        goto case OpCode.SendInternal;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static bool AddISubISlowPath(MRubyState state, ref MRubyValue registerA, MRubyValue lhs, int rV, OpCode opcode)
                        {
                            switch (lhs.VType)
                            {
                                case MRubyVType.Integer:
                                {
                                    var intVal = lhs.IntegerValue;
                                    var result = intVal + rV;
                                    if (((intVal ^ result) & (rV ^ result)) < 0)
                                        IntegerMembers.RaiseIntegerOverflowError(state, opcode == OpCode.AddI ? "add"u8 : "sub"u8);
                                    registerA = result;
                                    return true;
                                }
                                case MRubyVType.Float:
                                    registerA = new MRubyValue(lhs.FloatValue + rV);
                                    return true;
                            }
                            return false; // fallthrough to Send
                        }
                    }
                    case OpCode.EQ:
                    case OpCode.LT:
                    case OpCode.LE:
                    case OpCode.GT:
                    case OpCode.GE:
                        Markers.EQ();
                        Markers.LT();
                        Markers.LE();
                        Markers.GT();
                        Markers.GE();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        rhs = Unsafe.Add(ref registerA, 1);

                        if (opcode == OpCode.EQ)
                        {
                            if (registerA.Equals(rhs))
                            {
                                registerA = MRubyValue.True;
                                goto Next;
                            }
                            if (registerA.IsSymbol)
                            {
                                registerA = MRubyValue.False;
                                goto Next;
                            }
                        }

                        // Float fast path: bit check only, avoids VType call
                        lhsIsFloat = registerA.IsFloat;
                        rhsIsFloat = rhs.IsFloat;
                        if (lhsIsFloat && rhsIsFloat)
                        {
                            var leftVal = registerA.FloatValue;
                            var rightVal = rhs.FloatValue;
                            registerA = new MRubyValue(opcode switch
                            {
                                // ReSharper disable once CompareOfFloatsByEqualityOperator
                                OpCode.EQ => leftVal == rightVal,
                                OpCode.LT => leftVal < rightVal,
                                OpCode.LE => leftVal <= rightVal,
                                OpCode.GT => leftVal > rightVal,
                                OpCode.GE => leftVal >= rightVal,
                                _ => false
                            });
                            goto Next;
                        }

                        // Fixnum fast path: tagged-bits comparison — left-shift by 1 preserves
                        // signed ordering and the LSB tag (always 1 on both sides) cancels in
                        // subtraction, so raw bits compare equivalently to unboxed values.
                        lhsIsFixnum = registerA.IsFixnum;
                        rhsIsFixnum = rhs.IsFixnum;
                        if (lhsIsFixnum && rhsIsFixnum)
                        {
                            long lhsBits = registerA.bits;
                            long rhsBits = rhs.bits;
                            registerA = new MRubyValue(opcode switch
                            {
                                OpCode.EQ => lhsBits == rhsBits,
                                OpCode.LT => lhsBits < rhsBits,
                                OpCode.LE => lhsBits <= rhsBits,
                                OpCode.GT => lhsBits > rhsBits,
                                OpCode.GE => lhsBits >= rhsBits,
                                _ => false
                            });
                            goto Next;
                        }

                        if (ComparisonSlowPath(ref registerA, registerA, rhs, lhsIsFixnum, lhsIsFloat, rhsIsFixnum, rhsIsFloat, opcode))
                            goto Next;
                        // fallthrough to Send
                        callInfo = ref GetNextCallInfo(callInfo.StackPointer + a, opcode, 1);
                        goto case OpCode.SendInternal;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static bool ComparisonSlowPath(ref MRubyValue registerA, MRubyValue lhs, MRubyValue rhs, bool lhsIsFixnum, bool lhsIsFloat, bool rhsIsFixnum, bool rhsIsFloat, OpCode opcode)
                        {
                            var lhsIsInteger = lhsIsFixnum || lhs.Object?.VType == MRubyVType.Integer;
                            var rhsIsInteger = rhsIsFixnum || rhs.Object?.VType == MRubyVType.Integer;

                            if ((lhsIsInteger || lhsIsFloat) &&
                                (rhsIsInteger || rhsIsFloat))
                            {
                                var leftVal = lhsIsInteger ? lhs.IntegerValue : (long)lhs.FloatValue;
                                var rightVal = rhsIsInteger ? rhs.IntegerValue : (long)rhs.FloatValue;
                                registerA = new MRubyValue(opcode switch
                                {
                                    OpCode.EQ => leftVal == rightVal,
                                    OpCode.LT => leftVal < rightVal,
                                    OpCode.LE => leftVal <= rightVal,
                                    OpCode.GT => leftVal > rightVal,
                                    OpCode.GE => leftVal >= rightVal,
                                    _ => false
                                });
                                return true;
                            }

                            return false; // fallthrough to Send
                        }
                    case OpCode.Return:
                    {
                        Markers.Return();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        var returnValue = Unsafe.Add(ref registers, a);
                        if (TryReturnJump(ref callInfo, Context.CallDepth, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    case OpCode.RetSelf:
                    {
                        Markers.RetSelf();
                        callInfo.ProgramCounter++;
                        var returnValue = Unsafe.Add(ref registers, 0);
                        if (TryReturnJump(ref callInfo, Context.CallDepth, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    case OpCode.RetNil:
                    {
                        Markers.RetNil();
                        callInfo.ProgramCounter++;
                        var returnValue = MRubyValue.Nil;
                        if (TryReturnJump(ref callInfo, Context.CallDepth, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    case OpCode.RetTrue:
                    {
                        Markers.RetTrue();
                        callInfo.ProgramCounter++;
                        var returnValue = MRubyValue.True;
                        if (TryReturnJump(ref callInfo, Context.CallDepth, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    case OpCode.RetFalse:
                    {
                        Markers.RetFalse();
                        callInfo.ProgramCounter++;
                        var returnValue = MRubyValue.False;
                        if (TryReturnJump(ref callInfo, Context.CallDepth, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    // --- End hot opcodes ---

                    case OpCode.JmpUw:
                    {
                        Markers.JmpUw();
                        JmpUw(this, ref callInfo, ref sequence, irep);
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void JmpUw(MRubyState state, ref MRubyCallInfo callInfo, ref byte sequence, Irep irep)
                        {
                            var s = (short)ReadOperandS(ref sequence, ref callInfo.ProgramCounter);
                            var newProgramCounter = callInfo.ProgramCounter + s;
                            if (irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.Ensure, out var catchHandler))
                            {
                                // avoiding a jump from a catch handler into the same handler
                                if (newProgramCounter < catchHandler.Begin ||
                                    newProgramCounter > catchHandler.End)
                                {
                                    state.PrepareTaggedBreak(BreakTag.Jump, state.Context.CallDepth, newProgramCounter);
                                    callInfo.ProgramCounter = (int)catchHandler.Target;
                                    return;
                                }
                            }
                            state.Exception = null;
                            callInfo.ProgramCounter = newProgramCounter;
                        }
                    }
                    case OpCode.Except:
                        Markers.Except();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = Exception switch
                        {
                            MRubyRaiseException x => x.ExceptionObject,
                            MRubyBreakException x => x.BreakObject,
                            _ => default
                        };
                        Exception = null;
                        goto Next;
                    case OpCode.Rescue:
                    {
                        Markers.Rescue();
                        if (TryRescue(this, ref callInfo, ref registers, ref sequence))
                            goto JumpAndNext;
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static bool TryRescue(MRubyState state, ref MRubyCallInfo callInfo, ref MRubyValue registers, ref byte sequence)
                        {
                            var bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                            var exceptionObjectValue = Unsafe.Add(ref registers, bb.A);
                            var exceptionClassValue = Unsafe.Add(ref registers, bb.B);
                            switch (exceptionClassValue.VType)
                            {
                                case MRubyVType.Class:
                                case MRubyVType.Module:
                                    break;
                                default:
                                    var ex = new RException(
                                        state.NewString("class or module required for rescue clause"u8),
                                        state.GetExceptionClass(Names.TypeError));
                                    state.Exception = new MRubyRaiseException(state, ex, state.Context.CallDepth);
                                    if (state.TryRaiseJump(ref callInfo))
                                    {
                                        return true; // signal JumpAndNext
                                    }
                                    throw state.Exception;
                            }

                            Unsafe.Add(ref registers, bb.B) = state.KindOf(exceptionObjectValue, exceptionClassValue.As<RClass>());
                            return false; // signal Next
                        }
                    }
                    case OpCode.RaiseIf:
                    {
                        Markers.RaiseIf();
                        var signal = RaiseIf(this, ref callInfo, ref registers, ref sequence, irep, out var retVal);
                        switch (signal)
                        {
                            case VmSignal.JumpAndNext: goto JumpAndNext;
                            case VmSignal.Return: return retVal;
                        }
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static VmSignal RaiseIf(MRubyState state, ref MRubyCallInfo callInfo, ref MRubyValue registers, ref byte sequence, Irep irep, out MRubyValue retVal)
                        {
                            retVal = default;
                            var a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                            var exceptionValue = Unsafe.Add(ref registers, a);
                            switch (exceptionValue.Object)
                            {
                                case RBreak breakObject:
                                    state.Exception = new MRubyBreakException(state, breakObject);
                                    switch (breakObject.Tag)
                                    {
                                        case BreakTag.Break:
                                        {
                                            if (state.TryReturnJump(ref callInfo, breakObject.BreakIndex, breakObject.Value))
                                            {
                                                return VmSignal.JumpAndNext;
                                            }
                                            retVal = breakObject.Value;
                                            return VmSignal.Return;
                                        }
                                        case BreakTag.Jump:
                                        {
                                            var newProgramCounter = (int)breakObject.Value.IntegerValue;
                                            if (irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.Ensure, out var catchHandler))
                                            {
                                                // avoiding a jump from a catch handler into the same handler
                                                if (newProgramCounter < catchHandler.Begin || newProgramCounter > catchHandler.End)
                                                {
                                                    state.PrepareTaggedBreak(BreakTag.Jump, state.Context.CallDepth, newProgramCounter);
                                                    callInfo.ProgramCounter = (int)catchHandler.Target;
                                                    return VmSignal.Next;
                                                }
                                            }
                                            state.Exception = null;
                                            callInfo.ProgramCounter = newProgramCounter;
                                            return VmSignal.Next;
                                        }
                                        case BreakTag.Stop:
                                        {
                                            if (state.TryUnwindEnsureJump(ref callInfo, state.Context.CallDepth, BreakTag.Stop, breakObject.Value))
                                            {
                                                return VmSignal.JumpAndNext;
                                            }
                                            if (state.Exception != null) throw state.Exception;
                                            retVal = Unsafe.Add(ref registers, irep.LocalVariables.Length);
                                            return VmSignal.Return;
                                        }
                                    }
                                    break;
                                case RException exceptionObject:
                                    state.Exception = new MRubyRaiseException(state, exceptionObject, state.Context.CallDepth);
                                    if (state.TryRaiseJump(ref callInfo))
                                    {
                                        return VmSignal.JumpAndNext;
                                    }
                                    throw state.Exception;
                                default:
                                    state.Exception = null;
                                    break;
                            }
                            return VmSignal.Next;
                        }
                    }
                    case OpCode.MatchErr:
                    {
                        Markers.MatchErr();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        if (Unsafe.Add(ref registers, a).Falsy)
                        {
                            Raise(Names.NoMatchingPatternError, "no matching pattern"u8);
                        }
                        goto Next;
                    }
                    case OpCode.SSend:
                    case OpCode.SSendB:
                    case OpCode.Send:
                    case OpCode.SendB:
                    case OpCode.SSend0:
                    case OpCode.Send0:
                    {
                        Markers.SSend();
                        // Send0/SSend0 use BB operand (no argc byte); synthesize C=0.
                        if (opcode is OpCode.Send0 or OpCode.SSend0)
                        {
                            bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                            bbb = default;
                            bbb.A = bb.A;
                            bbb.B = bb.B;
                            bbb.C = 0;
                        }
                        else
                        {
                            bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        }

                        // Trivial getter fast path — skip full dispatch for no-arg sends
                        // that resolve to trivial getters (attr_reader or def x; @x; end)
                        if (bbb.C == 0 && opcode is OpCode.Send or OpCode.SSend or OpCode.Send0 or OpCode.SSend0)
                        {
                            var selfVal = opcode is OpCode.SSend or OpCode.SSend0
                                ? Unsafe.Add(ref registers, 0)
                                : Unsafe.Add(ref registers, bbb.A);
                            if (selfVal.Object is RObject selfObj and not RClass)
                            {
                                var mid = Unsafe.Add(ref symbols, bbb.B);
                                var cacheIdx = unchecked((uint)RuntimeHelpers.GetHashCode(selfObj.Class) ^ mid.Value) & (MethodCacheSize - 1);
                                ref var ce = ref methodCache[cacheIdx];
                                if (ce.Class == selfObj.Class && ce.MethodId == mid &&
                                    ce.Method.TrivialGetterIVarSymbol.Value != 0)
                                {
                                    var getterTable = selfObj.InstanceVariables;
                                    if (!getterTable.TryGetAt(ce.TrivialGetterSlot, ce.Method.TrivialGetterIVarSymbol, out var getterValue))
                                    {
                                        getterValue = TrivialGetterMiss(ref ce, getterTable);
                                    }
                                    Unsafe.Add(ref registers, bbb.A) = getterValue;
                                    goto Next;
                                }
                            }
                        }

                        var currentStackPointer = callInfo.StackPointer;

                        callInfo = ref Context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = currentStackPointer + bbb.A;
                        callInfo.MethodId = Unsafe.Add(ref symbols, bbb.B);
                        callInfo.ArgumentCount = (byte)(bbb.C & 0xf);
                        callInfo.KeywordArgumentCount = (byte)((bbb.C >> 4) & 0xf);

                        var nextRegisters = Context.Stack.AsSpan(callInfo.StackPointer);
                        var blockOffset = callInfo.BlockArgumentOffset;
                        var kargOffset = callInfo.KeywordArgumentOffset;
                        if (callInfo.KeywordArgumentCount > 0)
                        {
                            if (callInfo.KeywordArgumentPacked)
                            {
                                var kdict = nextRegisters[kargOffset];
                                EnsureValueType(kdict, MRubyVType.Hash);
                            }
                            else
                            {
                                PackKeywordArguments(this, ref callInfo, ref nextRegisters, kargOffset, ref blockOffset);

                                static void PackKeywordArguments(MRubyState state, ref MRubyCallInfo callInfo, ref Span<MRubyValue> nextRegisters, int kargOffset, ref int blockOffset)
                                {
                                    var hash = state.NewHash(callInfo.KeywordArgumentCount);
                                    for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
                                    {
                                        var k = nextRegisters[kargOffset + (i * 2)];
                                        var v = nextRegisters[kargOffset + (i * 2) + 1];
                                        hash.Add(k, v);
                                    }
                                    nextRegisters[kargOffset] = hash;

                                    var block = nextRegisters[blockOffset];
                                    callInfo.MarkAsKeywordArgumentPacked();
                                    blockOffset = callInfo.BlockArgumentOffset;
                                    nextRegisters[blockOffset] = block;
                                }
                            }
                        }

                        if (opcode is OpCode.Send or OpCode.SSend or OpCode.Send0 or OpCode.SSend0)
                        {
                            nextRegisters[blockOffset] = default;
                        }
                        else
                        {
                            var block = nextRegisters[blockOffset];
                            if (!block.IsNil)
                            {
                                if (block.Object is not RProc)
                                {
                                    block = ConvertType(block, MRubyVType.Proc, Intern("to_proc"u8));
                                    nextRegisters[blockOffset] = block;
                                }
                                EnsureValueIsBlock(block);
                            }
                        }

                        // self send
                        if (opcode is OpCode.SSend or OpCode.SSendB or OpCode.SSend0)
                        {
                            nextRegisters[0] = Unsafe.Add(ref registers, 0);
                        }
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.SendInternal:
                    {
                        Markers.SendInternal();
                        var self = Context.Stack[callInfo.StackPointer];
                        var receiverClass = opcode == OpCode.Super
                            ? (RClass)callInfo.Scope // set RClass.Super in OpCode.Super
                            : ClassOf(self);
                        var methodId = callInfo.MethodId;
                        if (!TryFindMethod(receiverClass, methodId, out var method, out receiverClass) ||
                            method == MRubyMethod.Undef)
                        {
                            method = PrepareMethodMissing(ref callInfo, self, methodId,
                                opcode == OpCode.Super
                                    ? static (state, self, methodId) => state.Raise(Names.NoMethodError, $"no superclass method '{state.NameOf(methodId)}' for {state.StringifyAny(self)}")
                                    : null);
                        }

                        callInfo.Scope = receiverClass;
                        callInfo.Proc = method.Proc;

                        // var block = stack[blockArgumentOffset];
                        // if (!block.IsNil) EnsureValueIsBlock(block);

                        // Validate method visibility
                        if (opcode is OpCode.Send or OpCode.SendB)
                        {
                            if (method.Visibility == MRubyMethodVisibility.Private)
                            {
                                var args = callInfo.ArgumentPacked
                                    ? Context.Stack[callInfo.StackPointer + 1]
                                    : NewArray(Context.Stack.AsSpan(callInfo.StackPointer + 1, callInfo.ArgumentCount));

                                RaiseMethodVisibilityVioration(methodId, self, args, MRubyMethodVisibility.Private);
                            }
                            else if (method.Visibility == MRubyMethodVisibility.Protected &&
                                     KindOf(self, callInfo.Scope.TargetClass))
                            {
                                var args = callInfo.ArgumentPacked
                                    ? Context.Stack[callInfo.StackPointer + 1]
                                    : NewArray(Context.Stack.AsSpan(callInfo.StackPointer + 1, callInfo.ArgumentCount));

                                RaiseMethodVisibilityVioration(methodId, self, args, MRubyMethodVisibility.Protected);
                            }
                        }

                        if (method.Kind == MRubyMethodKind.CSharpFunc)
                        {
                            if (CallCSharpFunc(this, method, self, ref irep, out var result))
                            {
                                return result;
                            }

                            callInfo = ref Context.CurrentCallInfo;
                            registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                            sequence = ref GetArrayDataReference(irep.Sequence);
                            symbols = ref GetArrayDataReference(irep.Symbols);
                            goto Next;

                            static bool CallCSharpFunc(MRubyState state, MRubyMethod method, MRubyValue self, ref Irep irep, out MRubyValue result)
                            {
                                result = method.Invoke(state, self);

                                ref var callInfo = ref state.Context.CurrentCallInfo;
                                var keepContext = callInfo.KeepContext;
                                var callerType = callInfo.CallerType;

                                state.Context.Stack[callInfo.StackPointer] = result;

                                // return from context modifying method (resume/yield)
                                if (!keepContext)
                                {
                                    if (callerType == CallerType.Resumed)
                                    {
                                        return true;
                                    }
                                }

                                state.Context.PopCallStack();
                                callInfo = ref state.Context.CurrentCallInfo;
                                irep = callInfo.Proc!.Irep;
                                return false;
                            }
                        }

                        var irepProc = callInfo.Proc;
                        irep = irepProc!.Irep;
                        callInfo.ProgramCounter = irepProc.ProgramCounter;

                        Context.ExtendStack(callInfo.StackPointer + (irep.RegisterVariableCount < 4 ? 4 : irep.RegisterVariableCount) + 1);
                        registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                        sequence = ref GetArrayDataReference(irep.Sequence);
                        symbols = ref GetArrayDataReference(irep.Symbols);

                        goto Next;
                        // pop on OpCode.Return
                    }
                    case OpCode.Call: // modify program counter
                    {
                        CallProc(this, out irep, ref callInfo);
                        registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                        sequence = ref GetArrayDataReference(irep.Sequence);
                        symbols = ref GetArrayDataReference(irep.Symbols);
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void CallProc(MRubyState state, out Irep irep, ref MRubyCallInfo callInfo)
                        {
                            callInfo.ProgramCounter += 1; // read opcode
                            var receiver = state.Context.Stack[callInfo.StackPointer];
                            var proc = receiver.As<RProc>();

                            // replace callinfo
                            callInfo.Scope = proc.Scope!.TargetClass;
                            callInfo.Proc = proc;

                            // setup environment for calling method
                            irep = proc.Irep;
                            callInfo.ProgramCounter = proc.ProgramCounter;

                            var currentSize = callInfo.BlockArgumentOffset + 1;
                            if (currentSize < irep.RegisterVariableCount)
                            {
                                state.Context.ExtendStack(callInfo.StackPointer + irep.RegisterVariableCount);
                                state.Context.ClearStack(
                                    callInfo.StackPointer + currentSize,
                                    irep.RegisterVariableCount - currentSize);
                            }
                            if (proc.Scope is REnv env)
                            {
                                callInfo.MethodId = env.MethodId;
                                state.Context.Stack[callInfo.StackPointer] = env.Stack[0];
                            }
                        }
                    }
                    case OpCode.Super:
                    {
                        Markers.Super();

                        Super(this, ref callInfo, Context.Stack.AsSpan(callInfo.StackPointer), ref sequence);
                        callInfo = ref Context.CurrentCallInfo;
                        goto case OpCode.SendInternal;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void Super(MRubyState state, ref MRubyCallInfo callInfo, Span<MRubyValue> registers, ref byte sequence)
                        {
                            var bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                            var targetClass = callInfo.Scope.TargetClass;
                            var methodId = callInfo.MethodId;
                            if (methodId == default || targetClass == null!)
                            {
                                state.Raise(Names.NoMethodError, "super called outside of method"u8);
                            }

                            var receiver = registers[0];
                            if (targetClass!.HasFlag(MRubyObjectFlags.ClassPrepended) ||
                                targetClass.VType == MRubyVType.Module ||
                                !state.KindOf(receiver, targetClass))
                            {
                                state.Raise(Names.TypeError, "self has wrong type to call super in this context"u8);
                            }

                            registers[bb.A] = receiver;

                            // Jump to send
                            var nextStackPointer = callInfo.StackPointer + bb.A;
                            callInfo = ref state.Context.PushCallStack();
                            callInfo.CallerType = CallerType.InVmLoop;
                            callInfo.Scope = targetClass.Super;
                            callInfo.StackPointer = nextStackPointer;
                            callInfo.MethodId = methodId;
                            callInfo.ArgumentCount = (byte)(bb.B & 0xf);
                            callInfo.KeywordArgumentCount = (byte)((bb.B >> 4) & 0xf);
                        }
                    }
                    case OpCode.ArgAry:
                    {
                        Markers.ArgAry();

                        bs = OperandBS.Read(ref sequence, ref callInfo.ProgramCounter);
                        var bits = (ushort)bs.B;
                        var m1 = (bits >> 11) & 0x3f;
                        var r = (bits >> 10) & 0x1;
                        var m2 = (bits >> 5) & 0x1f;
                        var kd = (bits >> 4) & 0x1;
                        var lv = bits & 0xf;

                        Span<MRubyValue> sourceStack;
                        if (lv == 0)
                        {
                            sourceStack = Context.Stack.AsSpan(callInfo.StackPointer + 1);
                        }
                        else
                        {
                            var env = callInfo.Proc?.FindUpperEnvTo(lv - 1);
                            if (env is null || env.Stack.Length <= m1 + r + m2 + 1)
                            {
                                Raise(Names.NoMethodError, "super called outside of method"u8);
                            }
                            sourceStack = env!.Stack[1..];
                        }

                        var result = r == 0
                            ? NewArray(sourceStack.Slice(0, m1 + m2))
                            : NewArrayFromArgumentArray(sourceStack, m1, m2);

                        Unsafe.Add(ref registers, bs.A) = result;
                        if (kd != 0)
                        {
                            Unsafe.Add(ref registers, bs.A + 1) = sourceStack[m1 + r + m2];
                            Unsafe.Add(ref registers, bs.A + 2) = sourceStack[m1 + r + m2 + 1];
                        }

                        goto Next;

                        RArray NewArrayFromArgumentArray(Span<MRubyValue> sourceStack, int m1, int m2)
                        {
                            var rest = sourceStack[m1].Object as RArray;
                            var resultLength = m1 + (rest?.Length ?? 0) + m2;
                            var array = NewArray(resultLength);

                            if (m1 > 0)
                            {
                                array.PushRange(sourceStack[..m1]);
                            }

                            if (rest is not null)
                            {
                                array.PushRange(rest.AsSpan());
                            }

                            if (m2 > 0)
                            {
                                array.PushRange(sourceStack.Slice(m1 + 1, m2));
                            }

                            return array;
                        }
                    }
                    case OpCode.Enter:
                    {
                        Markers.Enter();

                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var bits = (uint)bbb.A << 16 | (uint)bbb.B << 8 | bbb.C;
                        var aspec = new ArgumentSpec(bits);

                        var argc = callInfo.ArgumentCount;
                        var argv = Context.Stack.AsSpan(callInfo.StackPointer + 1);

                        var m1 = aspec.MandatoryArguments1Count;

                        // fast pass
                        if ((bits & ~0b11111000000000000000001) == 0 && // no other arg
                            !callInfo.ArgumentPacked &&
                            callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true)
                        {
                            EnterFastPass(this, irep, ref callInfo, argc, m1);

                            static void EnterFastPass(MRubyState state, Irep irep, ref MRubyCallInfo callInfo, byte argc, byte m1)
                            {
                                if (argc + (callInfo.KeywordArgumentPacked ? 1 : 0) != m1)
                                {
                                    state.RaiseArgumentNumberError(argc + (callInfo.KeywordArgumentPacked ? 1 : 0), m1);
                                }

                                // clear local (but non-argument) variables
                                var count = m1 + 2; // self + m1 + block
                                if (irep.LocalVariables.Length - count > 0)
                                {
                                    state.Context.ClearStack(
                                        callInfo.StackPointer + count,
                                        irep.LocalVariables.Length - count);
                                }
                            }

                            goto Next;
                        }
                        EnterSlowPath(ref callInfo, argv, Context.Stack.AsSpan(callInfo.StackPointer));

                        goto Next;

                        void EnterSlowPath(ref MRubyCallInfo callInfo, Span<MRubyValue> argv, Span<MRubyValue> registers)
                        {
                            var o = aspec.OptionalArgumentsCount;
                            var r = aspec.TakeRestArguments ? 1 : 0;
                            var m2 = aspec.MandatoryArguments2Count;
                            // mrb_int kd = (MRB_ASPEC_KEY(a) > 0 || MRB_ASPEC_KDICT(a))? 1 : 0;
                            var argv0 = argv.IsEmpty ? default : argv[0];

                            var mandantryTotalRequired = m1 + o + r + m2;
                            var block = registers[callInfo.BlockArgumentOffset];
                            var kdict = default(MRubyValue);
                            var hasAnyKeyword = aspec.KeywordArgumentsCount > 0 || aspec.TakeKeywordDict;

                            // keyword arguments
                            if (callInfo.KeywordArgumentPacked)
                            {
                                kdict = registers[callInfo.KeywordArgumentOffset];
                            }

                            if (!hasAnyKeyword)
                            {
                                if (kdict.Object is RHash { Length: > 0 })
                                {
                                    switch (argc)
                                    {
                                        // packed
                                        case MRubyCallInfo.CallMaxArgs:
                                            // push kdict to packed arguments
                                            registers[1].As<RArray>().Push(kdict);
                                            break;
                                        case MRubyCallInfo.CallMaxArgs - 1:
                                        {
                                            // pack arguments and kdict
                                            var packed = NewArray(registers.Slice(1, argc + 1));
                                            registers[1] = packed;
                                            argc = callInfo.ArgumentCount = MRubyCallInfo.CallMaxArgs;
                                            break;
                                        }
                                        default:
                                            callInfo.ArgumentCount++;
                                            argc++; // include kdict in normal arguments
                                            break;
                                    }
                                }
                                kdict = default;
                                callInfo.KeywordArgumentCount = 0;
                            }
                            else if (aspec.KeywordArgumentsCount > 0 && !kdict.IsNil)
                            {
                                kdict = kdict.As<RHash>().Dup();
                            }

                            // arguments is passed with Array
                            if (callInfo.ArgumentPacked)
                            {
                                argv = argv0.As<RArray>().AsSpan();
                                argc = (byte)argv.Length;
                            }

                            // strict argument check
                            if (callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true)
                            {
                                if (argc < m1 + m2 || (r == 0 && argc > mandantryTotalRequired))
                                {
                                    RaiseArgumentNumberError(argc, m1 + m2);
                                }
                            }
                            // extract first argument array to arguments
                            else if (mandantryTotalRequired > 1 && argc == 1 && argv[0].Object is RArray array)
                            {
                                argc = (byte)array.Length;
                                argv = array.AsSpan();
                            }

                            // rest arguments
                            var rest = default(MRubyValue);
                            if (argc < mandantryTotalRequired)
                            {
                                var mlen = (int)m2;
                                if (argc < m1 + m2)
                                {
                                    mlen = m1 < argc ? argc - m1 : 0;
                                }

                                if (!argv.IsEmpty && argv[0] != argv0)
                                {
                                    argv[..(argc - mlen)].CopyTo(registers[1..]); // m1 + o
                                }
                                if (argc < m1)
                                {
                                    registers.Slice(argc + 1, m1 - argc).Clear();
                                }

                                // copy post mandatory arguments
                                if (mlen > 0)
                                {
                                    argv.Slice(argc - mlen, mlen)
                                        .CopyTo(registers[(mandantryTotalRequired - m2 + 1)..]);
                                }
                                if (mlen < m2)
                                {
                                    registers.Slice(mandantryTotalRequired - m2 + mlen + 1, m2 - mlen).Clear();
                                }

                                // initialize rest arguments with empty Array
                                if (r > 0)
                                {
                                    rest = NewArray(0);
                                    registers[m1 + o + 1] = rest;
                                }

                                // skip initializer of passed arguments
                                if (o > 0 && argc > m1 + m2)
                                {
                                    callInfo.ProgramCounter += (argc - m1 - m2) * 3;
                                }
                            }
                            else
                            {
                                var restElementLength = 0;
                                if (!argv.IsEmpty && argv0 != argv[0])
                                {
                                    argv[..(m1 + o)].CopyTo(registers[1..]);
                                }
                                if (r > 0)
                                {
                                    restElementLength = argc - m1 - o - m2;
                                    rest = NewArray(argv.Slice(m1 + o, restElementLength));
                                    registers[m1 + o + 1] = rest;
                                }

                                if (m2 > 0 && argc - m2 > m1)
                                {
                                    argv[(m1 + o + restElementLength)..].CopyTo(registers[(m1 + o + r + 1)..]);
                                }
                                callInfo.ProgramCounter += o * 3;
                            }

                            // need to be update blk first to protect blk from GC
                            var keywordPos = mandantryTotalRequired + (hasAnyKeyword ? 1 : 0);
                            var blockPos = keywordPos + 1;
                            registers[blockPos] = block;
                            if (hasAnyKeyword)
                            {
                                if (kdict.IsNil) kdict = NewHash(0);
                                registers[keywordPos] = kdict;
                                callInfo.MarkAsKeywordArgumentPacked();
                            }

                            // format arguments for generated code
                            callInfo.ArgumentCount = (byte)mandantryTotalRequired;
                            // clear local (but non-argument) variables
                            if (irep.LocalVariables.Length - blockPos - 1 > 0)
                            {
                                registers.Slice(blockPos + 1, irep.LocalVariables.Length - blockPos - 1).Clear();
                            }
                        }
                    }

                    case OpCode.KArg:
                    {
                        Markers.KArg();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        // mrb_value k = mrb_symbol_value(irep->syms[b]);
                        var key = Unsafe.Add(ref symbols, bb.B);
                        var kargOffset = callInfo.KeywordArgumentOffset;
                        if (kargOffset < 0)
                        {
                            RaiseMissingKeywordError(key);
                        }
                        var kdict = Unsafe.Add(ref registers, kargOffset);
                        var value = default(MRubyValue);
                        if (kdict.VType != MRubyVType.Hash ||
                            !Unsafe.Add(ref registers, kargOffset).As<RHash>().TryGetValue(key, out value))
                        {
                            RaiseMissingKeywordError(key);
                        }

                        Unsafe.Add(ref registers, bb.A) = value;
                        kdict.As<RHash>().TryDelete(key, out _);
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        void RaiseMissingKeywordError(MRubyValue keyValue)
                        {
                            Raise(Names.ArgumentError, $"missing keyword: {Stringify(keyValue)}");
                        }
                    }
                    case OpCode.KeyP:
                    {
                        Markers.KeyP();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var key = Unsafe.Add(ref symbols, bb.B);
                        var kdict = Unsafe.Add(ref registers, callInfo.KeywordArgumentOffset);
                        Unsafe.Add(ref registers, bb.A) = kdict.As<RHash>().TryGetValue(key, out _);
                        goto Next;
                    }
                    case OpCode.KeyEnd:
                    {
                        Markers.KeyEnd();
                        callInfo.ProgramCounter++;
                        var kargOffset = callInfo.KeywordArgumentOffset;
                        if (kargOffset >= 0 &&
                            Unsafe.Add(ref registers, kargOffset).Object is RHash { Length: > 0 } hash)
                        {
                            var key1 = hash.Keys[0];
                            RaiseUnknownKeyword(key1);

                            [MethodImpl(MethodImplOptions.NoInlining)]
                            void RaiseUnknownKeyword(MRubyValue keyValue)
                            {
                                Raise(Names.ArgumentError, $"unknown keyword: {Stringify(keyValue)}");
                            }
                        }
                        goto Next;
                    }
                    case OpCode.ReturnBlk:
                    {
                        Markers.ReturnBlk();
                        if (callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true ||
                            callInfo.Proc?.Scope is not REnv)
                        {
                            goto case OpCode.Return;
                        }
                        var signal = ReturnBlk(this, ref callInfo, ref registers, ref sequence, out var retVal);
                        switch (signal)
                        {
                            case VmSignal.JumpAndNext: goto JumpAndNext;
                            case VmSignal.Return: return retVal;
                        }
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static VmSignal ReturnBlk(MRubyState state, ref MRubyCallInfo callInfo, ref MRubyValue registers, ref byte sequence, out MRubyValue retVal)
                        {
                            retVal = default;
                            var a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                            var dest = callInfo.Proc!.FindReturningDestination(out var env);
                            if (dest.Scope is not REnv destEnv || destEnv.Context == state.Context)
                            {
                                // check jump destination
                                for (var i = state.Context.CallDepth; i >= 0; i--)
                                {
                                    if (state.Context.CallStack[i].Scope == env)
                                    {
                                        var returnValue = Unsafe.Add(ref registers, a);
                                        if (state.TryReturnJump(ref callInfo, i, returnValue))
                                        {
                                            return VmSignal.JumpAndNext;
                                        }
                                        retVal = returnValue;
                                        return VmSignal.Return;
                                    }
                                }
                            }
                            // no jump destination
                            state.Raise(Names.LocalJumpError, "unexpected return"u8);
                            return VmSignal.Next; // not reached
                        }
                    }
                    case OpCode.Break:
                    {
                        Markers.Break();
                        if (callInfo.Proc is { } x && x.HasFlag(MRubyObjectFlags.ProcStrict))
                        {
                            goto case OpCode.Return;
                        }
                        var signal = Break(this, ref callInfo, ref registers, ref sequence, out var retVal);
                        switch (signal)
                        {
                            case VmSignal.JumpAndNext: goto JumpAndNext;
                            case VmSignal.Return: return retVal;
                        }
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static VmSignal Break(MRubyState state, ref MRubyCallInfo callInfo, ref MRubyValue registers, ref byte sequence, out MRubyValue retVal)
                        {
                            retVal = default;
                            var a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                            if (callInfo.Proc is { } proc &&
                                !proc.HasFlag(MRubyObjectFlags.ProcOrphan) &&
                                proc.Scope is REnv env && env.Context == state.Context)
                            {
                                var dest = proc.Upper;
                                for (var i = state.Context.CallDepth; i > 0; i--)
                                {
                                    if (state.Context.CallStack[i - 1].Proc == dest)
                                    {
                                        var returnValue = Unsafe.Add(ref registers, a);
                                        if (state.TryReturnJump(ref callInfo, i, returnValue))
                                        {
                                            return VmSignal.JumpAndNext;
                                        }
                                        retVal = returnValue;
                                        return VmSignal.Return;
                                    }
                                }
                            }
                            state.Raise(Names.LocalJumpError, "break from proc-closure"u8);
                            return VmSignal.Next; // not reached
                        }
                    }
                    case OpCode.BlkPush:
                    {
                        Markers.BlkPush();
                        BlkPush(this, ref callInfo, Context.Stack.AsSpan(callInfo.StackPointer), ref sequence);

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void BlkPush(MRubyState state, ref MRubyCallInfo callInfo, Span<MRubyValue> registers, ref byte sequence)
                        {
                            var bs = OperandBS.Read(ref sequence, ref callInfo.ProgramCounter);
                            var b = bs.B;
                            var m1 = (b >> 11) & 0x3f;
                            var r = (b >> 10) & 0x1;
                            var m2 = (b >> 5) & 0x1f;
                            var kd = (b >> 4) & 0x1;
                            var lv = (b >> 0) & 0xf;
                            var offset = m1 + r + m2 + kd;

                            ReadOnlySpan<MRubyValue> stack;
                            if (lv == 0)
                            {
                                stack = registers[1..];
                            }
                            else
                            {
                                var env = callInfo.Proc?.FindUpperEnvTo(lv - 1);
                                if (env == null ||
                                    env is { OnStack: false, MethodId.Value: 0 } ||
                                    env.Stack.Length <= offset + 1)
                                {
                                    state.Raise(Names.LocalJumpError, "unexpected yield"u8);
                                }
                                stack = env!.Stack[1..];
                            }

                            var block = stack[offset];
                            if (block.IsNil)
                            {
                                state.Raise(Names.LocalJumpError, "unexpected yield"u8);
                            }
                            registers[bs.A] = block;
                        }

                        goto Next;
                    }
                    case OpCode.BlkCall:
                    {
                        Markers.BlkCall();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var blockValue = Unsafe.Add(ref registers, bb.A);
                        if (blockValue.Object is not RProc proc)
                        {
                            Raise(Names.TypeError, $"wrong type {Stringify(blockValue)} (expected Proc)");
                            goto Next; // unreachable
                        }

                        var currentStackPointer = callInfo.StackPointer;
                        callInfo = ref Context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = currentStackPointer + bb.A;
                        callInfo.MethodId = default;
                        callInfo.ArgumentCount = (byte)bb.B;
                        callInfo.KeywordArgumentCount = 0;
                        callInfo.Scope = proc.Scope?.TargetClass!;
                        callInfo.Proc = proc;

                        irep = proc.Irep;
                        callInfo.ProgramCounter = proc.ProgramCounter;

                        var newRegisterCount = irep.RegisterVariableCount;
                        Context.ExtendStack(callInfo.StackPointer + newRegisterCount + 1);
                        // self at stack[0] is already the proc value (= old register A);
                        // ensure block arg slot is nil.
                        var blockArgIdx = callInfo.BlockArgumentOffset;
                        Context.Stack[callInfo.StackPointer + blockArgIdx] = default;
                        var clearStart = callInfo.StackPointer + bb.B + 1;
                        var clearLen = newRegisterCount - (bb.B + 1);
                        if (clearLen > 0)
                        {
                            Context.ClearStack(clearStart, clearLen);
                        }

                        if (proc.Scope is REnv env)
                        {
                            callInfo.MethodId = env.MethodId;
                            Context.Stack[callInfo.StackPointer] = env.Stack[0];
                        }

                        sequence = ref GetArrayDataReference(irep.Sequence);
                        symbols = ref GetArrayDataReference(irep.Symbols);
                        registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                        goto Next;
                    }
                    case OpCode.Array:
                    {
                        Markers.Array();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var values = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref registers, bb.A), bb.B);
                        Unsafe.Add(ref registers, bb.A) = NewArray(values);
                        goto Next;
                    }
                    case OpCode.Array2:
                    {
                        Markers.Array2();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var values = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref registers, bbb.B), bbb.C);
                        Unsafe.Add(ref registers, bbb.A) = NewArray(values);
                        goto Next;
                    }
                    case OpCode.AryCat:
                    {
                        Markers.AryCat();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        var splat = SplatArray(Unsafe.Add(ref registerA, 1));
                        if (registerA.IsNil)
                        {
                            registerA = splat;
                        }
                        else
                        {
                            EnsureValueType(registerA, MRubyVType.Array);
                            var array = registerA.As<RArray>();
                            array.Concat(splat.As<RArray>());
                        }
                        goto Next;
                    }
                    case OpCode.ARef:
                    {
                        Markers.ARef();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bbb.A);
                        var v = Unsafe.Add(ref registers, bbb.B);
                        if (v.VType == MRubyVType.Array)
                        {
                            registerA = v.As<RArray>()[bbb.C];
                        }
                        else
                        {
                            if (bbb.C == 0)
                            {
                                registerA = v;
                            }
                            else
                            {
                                registerA = default;
                            }
                        }
                        goto Next;
                    }
                    case OpCode.ASet:
                    {
                        Markers.ASet();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var array = Unsafe.Add(ref registers, bbb.B).As<RArray>();
                        array.Set(bbb.C, Unsafe.Add(ref registers, bbb.A));
                        goto Next;
                    }
                    case OpCode.APost:
                    {
                        Markers.APost();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bbb.A);
                        if (registerA.Object is not RArray array)
                        {
                            array = NewArray(registerA);
                        }
                        int pre = bbb.B;
                        int post = bbb.C;
                        if (array.Length > pre + post)
                        {
                            APostShort(this, array, bbb, pre, post, ref registerA);

                            [MethodImpl(MethodImplOptions.NoInlining)]
                            static void APostShort(MRubyState state, RArray array, OperandBBB bbb, int pre, int post, ref MRubyValue registerA)
                            {
                                registerA = state.NewSharedArray(array, bbb.B, array.Length - pre - post);
                                registerA = ref Unsafe.Add(ref registerA, 1);
                                while (post-- > 0)
                                {
                                    registerA = array[array.Length - post - 1];
                                    registerA = ref Unsafe.Add(ref registerA, 1);
                                }
                            }
                        }
                        else
                        {
                            APostLong(this, array, pre, post, ref registerA);

                            [MethodImpl(MethodImplOptions.NoInlining)]
                            static void APostLong(MRubyState state, RArray array, int pre, int post, ref MRubyValue registerA)
                            {
                                registerA = state.NewArray(0);
                                registerA = ref Unsafe.Add(ref registerA, 1);
                                int i;
                                for (i = 0; i + pre < array.Length; i++)
                                {
                                    Unsafe.Add(ref registerA, i) = array[pre + i];
                                }
                                while (i < post)
                                {
                                    Unsafe.Add(ref registerA, i) = default;
                                    i++;
                                }
                            }
                        }
                        goto Next;
                    }
                    case OpCode.AryPush:
                    {
                        Markers.AryPush();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bb.A);
                        EnsureNotFrozen(registerA);

                        var array = registerA.As<RArray>();
                        array.PushRange(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref registers, bb.A + 1), bb.B));
                        goto Next;
                    }
                    case OpCode.ArySplat:
                        Markers.ArySplat();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        registerA = SplatArray(registerA);
                        goto Next;
                    case OpCode.Intern:
                        Markers.Intern();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        registerA = Intern(registerA.As<RString>());
                        goto Next;
                    case OpCode.Symbol:
                    {
                        Markers.Symbol();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        //var name = irep.PoolValues[bb.B].As<RString>();
                        Unsafe.Add(ref registers, bb.A) = Intern(irep.PoolValues[bb.B].As<RString>());
                        goto Next;
                    }
                    case OpCode.String:
                    {
                        Markers.String();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var str = irep.PoolValues[bb.B].As<RString>();
                        Unsafe.Add(ref registers, bb.A) = str.Dup();
                        goto Next;
                    }
                    case OpCode.StrCat:
                        Markers.StrCat();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        registerA.As<RString>().Concat(Stringify(Unsafe.Add(ref registerA, 1)));
                        goto Next;
                    case OpCode.Hash:
                    {
                        Markers.Hash();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bb.A);
                        var hash = NewHash(bb.B);
                        var lastIndex = bb.B * 2;
                        for (var i = 0; i < lastIndex; i += 2)
                        {
                            hash.Add(Unsafe.Add(ref registerA, i), Unsafe.Add(ref registerA, i + 1));
                        }

                        registerA = hash;
                        goto Next;
                    }
                    case OpCode.HashAdd:
                    {
                        Markers.HashAdd();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, bb.A);
                        var hashValue = registerA;
                        var lastIndex = bb.B * 2 + 1;

                        EnsureValueType(hashValue, MRubyVType.Hash);
                        var hash = hashValue.As<RHash>();
                        for (var i = 1; i < lastIndex; i += 2)
                        {
                            hash.Add(Unsafe.Add(ref registerA, i), Unsafe.Add(ref registerA, i + 1));
                        }
                        goto Next;
                    }
                    case OpCode.HashCat:
                        Markers.HashCat();
                        a = Unsafe.Add(ref sequence, ++callInfo.ProgramCounter);
                        ++callInfo.ProgramCounter;
                        registerA = ref Unsafe.Add(ref registers, a);
                        EnsureNotFrozen(registerA);
                        registerA.As<RHash>().Merge(Unsafe.Add(ref registerA, 1).As<RHash>());
                        goto Next;
                    case OpCode.Lambda:
                    case OpCode.Block:
                    case OpCode.Method:
                    {
                        Markers.Lambda();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        RProc proc;
                        if (opcode == OpCode.Method)
                        {
                            proc = NewProc(irep.Children[bb.B]);
                            proc.SetFlag(MRubyObjectFlags.ProcStrict | MRubyObjectFlags.ProcScope);
                        }
                        else
                        {
                            proc = NewClosure(irep.Children[bb.B]);
                            if (opcode == OpCode.Lambda)
                            {
                                proc.SetFlag(MRubyObjectFlags.ProcStrict | MRubyObjectFlags.ProcScope);
                            }
                        }
                        Unsafe.Add(ref registers, bb.A) = proc;
                        goto Next;
                    }
                    case OpCode.RangeInc:
                    case OpCode.RangeExc:
                        Markers.RangeInc();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                    {
                        var begin = registerA;
                        var end = Unsafe.Add(ref registerA, 1);
                        var range = new RRange(begin, end, opcode == OpCode.RangeExc, RangeClass);
                        range.MarkAsFrozen();
                        Unsafe.Add(ref registers, a) = range;
                        goto Next;
                    }
                    case OpCode.OClass:
                        Markers.OClass();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = ObjectClass;
                        goto Next;
                    case OpCode.Class:
                    {
                        Markers.Class();
                        Class(this, irep, Context.Stack.AsSpan(callInfo.StackPointer), ref sequence, ref callInfo);

                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void Class(MRubyState state, Irep irep, Span<MRubyValue> registers, ref byte sequence, ref MRubyCallInfo callInfo)
                        {
                            var bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                            var id = irep.Symbols[bb.B];
                            var outer = registers[bb.A];
                            var super = registers[bb.A + 1];

                            RClass outerClass;
                            if (outer.IsNil)
                            {
                                outerClass = callInfo.Proc?.Scope?.TargetClass ?? state.ObjectClass;
                            }
                            else
                            {
                                state.EnsureClassOrModule(outer);
                                outerClass = outer.As<RClass>();
                            }

                            // mrb_vm_define_class
                            RClass? superClass = null;
                            RClass definedClass;
                            if (!super.IsNil)
                            {
                                if (super.Object is RClass sc)
                                {
                                    superClass = sc;
                                }
                                else
                                {
                                    RaiseSuperClassMustBeClass(super);

                                    [MethodImpl(MethodImplOptions.NoInlining)]
                                    void RaiseSuperClassMustBeClass(MRubyValue superValue)
                                    {
                                        state.Raise(Names.TypeError, $"superclass must be a Class ({state.Stringify(superValue)} given)");
                                    }
                                }
                            }

                            if (state.ConstDefinedAt(id, outerClass))
                            {
                                var old = state.GetConst(id, outerClass);
                                if (!old.IsClass)
                                {
                                    RaiseNotAClass(old);

                                    [MethodImpl(MethodImplOptions.NoInlining)]
                                    void RaiseNotAClass(MRubyValue oldValue)
                                    {
                                        state.Raise(Names.TypeError, $"{state.StringifyAny(oldValue)} is not a class");
                                    }
                                }

                                definedClass = old.As<RClass>();
                                if (superClass != null)
                                {
                                    // check super class
                                    if (definedClass.Super.GetRealClass() != superClass)
                                    {
                                        RaiseSuperClassMismatch(old);

                                        [MethodImpl(MethodImplOptions.NoInlining)]
                                        void RaiseSuperClassMismatch(MRubyValue oldValue)
                                        {
                                            state.Raise(Names.TypeError, $"superclass mismatch for {state.StringifyAny(oldValue)}");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                superClass ??= state.ObjectClass;
                                definedClass = state.DefineClass(id, superClass, superClass.InstanceVType, outerClass);
                                state.ClassInheritedHook(superClass.GetRealClass(), definedClass);
                            }
                            registers[bb.A] = definedClass;
                        }
                    }

                    case OpCode.Module:
                    {
                        Markers.Module();
                        Module(this, ref callInfo, ref registers, ref sequence, ref symbols);
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void Module(MRubyState state, ref MRubyCallInfo callInfo, ref MRubyValue registers, ref byte sequence, ref Symbol symbols)
                        {
                            var bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                            ref var registerA = ref Unsafe.Add(ref registers, bb.A);
                            var id = Unsafe.Add(ref symbols, bb.B);
                            RClass outerClass;
                            if (registerA.IsNil)
                            {
                                outerClass = callInfo.Proc?.Scope?.TargetClass ?? state.ObjectClass;
                            }
                            else
                            {
                                state.EnsureClassOrModule(registerA);
                                outerClass = registerA.As<RClass>();
                            }

                            RClass definedModule;
                            if (state.ConstDefinedAt(id, outerClass))
                            {
                                var old = state.GetConst(id, outerClass);
                                if (old.VType != MRubyVType.Module)
                                {
                                    state.Raise(Names.TypeError, $"{state.StringifyAny(old)} is not a module");
                                }
                                definedModule = old.As<RClass>();
                            }
                            else
                            {
                                definedModule = state.DefineModule(id, outerClass);
                            }
                            registerA = definedModule;
                        }
                    }
                    case OpCode.Exec:
                    {
                        Markers.Exec();
                        Exec(this, ref callInfo, ref sequence, ref registers);
                        callInfo = ref Context.CurrentCallInfo;
                        irep = callInfo.Proc!.Irep;
                        sequence = ref GetArrayDataReference(irep.Sequence);
                        symbols = ref GetArrayDataReference(irep.Symbols);
                        registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                        goto Next;

                        [MethodImpl(MethodImplOptions.NoInlining)]
                        static void Exec(MRubyState state, ref MRubyCallInfo callInfo, ref byte sequence, ref MRubyValue registers)
                        {
                            var bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                            var receiver = Unsafe.Add(ref registers, bb.A);
                            var irep = callInfo.Proc!.Irep;
                            var targetIrep = irep.Children[bb.B];

                            // prepare closure
                            var proc = state.NewProc(targetIrep, receiver.As<RClass>());
                            proc.SetFlag(MRubyObjectFlags.ProcScope);

                            // prepare callstack
                            ref var nextCallInfo = ref state.Context.PushCallStack();
                            nextCallInfo.StackPointer = callInfo.StackPointer + bb.A;
                            nextCallInfo.CallerType = CallerType.InVmLoop;
                            nextCallInfo.Scope = receiver.As<RClass>();
                            nextCallInfo.Proc = proc;
                            nextCallInfo.MethodId = default;
                            nextCallInfo.ArgumentCount = 0;
                            nextCallInfo.KeywordArgumentCount = 0;
                            nextCallInfo.ProgramCounter = 0;

                            var newIrep = proc.Irep;
                            state.Context.ExtendStack(nextCallInfo.StackPointer + newIrep.RegisterVariableCount + 1);
                            state.Context.ClearStack(nextCallInfo.StackPointer + 1, newIrep.RegisterVariableCount - 1);
                        }
                    }
                    case OpCode.Def:
                    {
                        Markers.Def();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var target = Unsafe.Add(ref registers, bb.A).As<RClass>();
                        var proc = Unsafe.Add(ref registers, bb.A + 1).As<RProc>();
                        var methodId = Unsafe.Add(ref symbols, bb.B);

                        DefineMethod(target, methodId, MRubyMethod.CreateFromProc(proc));
                        MethodAddedHook(target, methodId);
                        Unsafe.Add(ref registers, bb.A) = methodId;
                        goto Next;
                    }
                    case OpCode.TDef:
                    {
                        Markers.TDef();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var target = callInfo.Scope.TargetClass;
                        var methodId = Unsafe.Add(ref symbols, bbb.B);
                        var proc = NewProc(irep.Children[bbb.C]);
                        proc.SetFlag(MRubyObjectFlags.ProcStrict | MRubyObjectFlags.ProcScope);
                        DefineMethod(target, methodId, MRubyMethod.CreateFromProc(proc));
                        MethodAddedHook(target, methodId);
                        Unsafe.Add(ref registers, bbb.A) = methodId;
                        goto Next;
                    }
                    case OpCode.SDef:
                    {
                        Markers.SDef();
                        bbb = OperandBBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var target = SingletonClassOf(Unsafe.Add(ref registers, bbb.A));
                        var methodId = Unsafe.Add(ref symbols, bbb.B);
                        var proc = NewProc(irep.Children[bbb.C]);
                        proc.SetFlag(MRubyObjectFlags.ProcStrict | MRubyObjectFlags.ProcScope);
                        DefineMethod(target, methodId, MRubyMethod.CreateFromProc(proc));
                        MethodAddedHook(target, methodId);
                        Unsafe.Add(ref registers, bbb.A) = methodId;
                        goto Next;
                    }
                    case OpCode.Alias:
                    {
                        Markers.Alias();
                        bb = OperandBB.Read(ref sequence, ref callInfo.ProgramCounter);
                        var c = callInfo.Scope.TargetClass;
                        var newMethodId = Unsafe.Add(ref symbols, bb.A);
                        var oldMethodId = Unsafe.Add(ref symbols, bb.B);
                        AliasMethod(c, newMethodId, oldMethodId);
                        MethodAddedHook(c, newMethodId);
                        goto Next;
                    }
                    case OpCode.Undef:
                    {
                        Markers.Undef();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        var c = callInfo.Scope.TargetClass;
                        var methodId = Unsafe.Add(ref symbols, a);
                        UndefMethod(c, methodId);
                        goto Next;
                    }
                    case OpCode.SClass:
                    {
                        Markers.SClass();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        registerA = ref Unsafe.Add(ref registers, a);
                        registerA = SingletonClassOf(registerA);
                        goto Next;
                    }
                    case OpCode.TClass:
                    {
                        Markers.TClass();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        Unsafe.Add(ref registers, a) = callInfo.Scope.TargetClass;
                        goto Next;
                    }
                    case OpCode.Err:
                    {
                        Markers.Err();
                        a = ReadOperandB(ref sequence, ref callInfo.ProgramCounter);
                        var message = irep.PoolValues[a];
                        Raise(Names.LocalJumpError, message.As<RString>());
                        goto Next;
                    }
                    case OpCode.Stop:
                    {
                        Markers.Stop();
                        var returnValue = Exception switch
                        {
                            MRubyRaiseException x => new MRubyValue(x.ExceptionObject),
                            MRubyBreakException x => new MRubyValue(x.BreakObject),
                            _ => default
                        };
                        if (TryUnwindEnsureJump(ref callInfo, Context.CallDepth, BreakTag.Stop, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        if (Exception != null) throw Exception;
                        return Unsafe.Add(ref registers, irep.LocalVariables.Length);
                    }
                    default:
                    {
                        ThrowInvalidOpCode(opcode);
                        return default;

                        static void ThrowInvalidOpCode(OpCode opcode)
                        {
                            throw new NotSupportedException($"Invalid opcode {opcode}");
                        }
                    }
                }

                Next: continue;

                JumpAndNext:
                callInfo = ref Context.CurrentCallInfo;
                irep = callInfo.Proc!.Irep;
                registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                sequence = ref GetArrayDataReference(irep.Sequence);
                symbols = ref GetArrayDataReference(irep.Symbols);
            }
            catch (MRubyRaiseException ex)
            {
                Exception = ex;
                if (TryRaiseJump(ref Context.CurrentCallInfo))
                {
                    callInfo = ref Context.CurrentCallInfo;
                    irep = callInfo.Proc!.Irep;
                    registers = ref Unsafe.Add(ref GetArrayDataReference(Context.Stack), callInfo.StackPointer);
                    sequence = ref GetArrayDataReference(irep.Sequence);
                    symbols = ref GetArrayDataReference(irep.Symbols);
                }
                else
                {
                    throw;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        ref MRubyCallInfo GetNextCallInfo(int nextStackPointer, OpCode code, byte argCount)
        {
            ref var callInfo = ref Context.PushCallStack();
            callInfo.CallerType = CallerType.InVmLoop;
            callInfo.StackPointer = nextStackPointer;
            callInfo.MethodId = SymbolHelpers.GetOpCodeSymbol(code);
            callInfo.ArgumentCount = argCount;
            callInfo.KeywordArgumentCount = 0;
            return ref callInfo;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte ReadOperandB(ref byte sequence, ref int pc)
    {
        pc += 2;
        var result = Unsafe.Add(ref sequence, pc - 1);
        return result;
    }

    /// I don't know why, but introducing this method makes the code faster.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ReadOperandS(ref byte sequence, ref int pc)
    {
        return OperandS.Read(ref sequence, ref pc).A;
    }

    bool TryReturnJump(ref MRubyCallInfo callInfo, int returnDepth, MRubyValue returnValue)
    {
        while (true)
        {
            if (TryUnwindEnsureJump(ref callInfo, returnDepth, BreakTag.Break, returnValue))
            {
                return true;
            }

            if (Context.CallDepth == returnDepth)
            {
                break;
            }

            var callerType = callInfo.CallerType;
            Context.PopCallStack();
            callInfo = ref Context.CurrentCallInfo;
            if (callerType != CallerType.InVmLoop)
            {
                Exception = new MRubyBreakException(this, new RBreak
                {
                    BreakIndex = returnDepth,
                    Tag = BreakTag.Break,
                    Value = returnValue
                });
                Context.VmExecutedByFiber = false;
                throw Exception;
            }
        }
        Exception = null; // Clear break object

        // root
        if (Context.CallDepth == 0)
        {
            if (Context == ContextRoot)
            {
                // toplevel return
                return false;
            }

            Context.Fiber?.Terminate(ref callInfo);

            // case using Fiber#transfer in resume
            if (Context.VmExecutedByFiber || (Context == ContextRoot && Context.CallDepth <= 0))
            {
                Context.VmExecutedByFiber = false;
                return false;
            }

            callInfo = Context.CurrentCallInfo;
        }

        if (Context.VmExecutedByFiber && !callInfo.KeepContext)
        {
            Context.VmExecutedByFiber = false;
            return false;
        }

        var returnOffset = callInfo.StackPointer;
        Context.PopCallStack();
        if (callInfo.CallerType is CallerType.VmExecuted or CallerType.MethodCalled)
        {
            return false;
        }

        Context.Stack[returnOffset] = returnValue;
        return true;
    }

    bool TryUnwindEnsureJump(ref MRubyCallInfo callInfo, int returnDepth, BreakTag tag, MRubyValue value)
    {
        if (callInfo.Proc is { Irep: { CatchHandlers.Length: > 0 } irep } &&
            irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.Ensure, out var catchHandler))
        {
            PrepareTaggedBreak(tag, returnDepth, value);
            callInfo.ProgramCounter = (int)catchHandler.Target;
            return true;
        }
        return false;
    }

    bool TryRaiseJump(ref MRubyCallInfo callInfo)
    {
        while (true)
        {
            if (callInfo.Proc is { Irep: { CatchHandlers.Length: > 0 } irep } &&
                irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.All, out var catchHandler))
            {
                callInfo.ProgramCounter = (int)catchHandler.Target;
                return true;
            }

            if (Context.CallDepth > 0)
            {
                var callerType = callInfo.CallerType;
                Context.PopCallStack();
                callInfo = ref Context.CurrentCallInfo;
                if (callerType == CallerType.VmExecuted)
                {
                    return false;
                }
            }
            else if (Context == ContextRoot)
            {
                // top-level
                return false;
            }
            else
            {
                // Fiber context
                Context.Fiber?.Terminate(ref callInfo);
                callInfo = ref Context.CurrentCallInfo;
                if (!Context.VmExecutedByFiber)
                {
                    return TryRaiseJump(ref callInfo);
                }
                return false;
            }
        }
    }

    void PrepareTaggedBreak(BreakTag tag, int callDepth, MRubyValue returnValue)
    {
        if (Exception is MRubyBreakException ex)
        {
            ex.BreakObject.Tag = tag;
        }
        else
        {
            Exception = new MRubyBreakException(this, new RBreak
            {
                BreakIndex = callDepth,
                Tag = tag,
                Value = returnValue
            });
        }
    }

    MRubyMethod PrepareMethodMissing(
        ref MRubyCallInfo callInfo,
        MRubyValue receiver,
        Symbol methodId,
        Action<MRubyState, MRubyValue, Symbol>? raise = null)
    {
        var receiverClass = ClassOf(receiver);
        var args = Context.GetRestArgumentsAfter(ref callInfo, 0);
        if (!TryFindMethod(receiverClass, Names.MethodMissing, out var method, out _) ||
            method == BasicObjectMembers.MethodMissing)
        {
            _Raise(args);
        }

        // call :method_missing

        if (!TryFindMethod(callInfo.Scope.TargetClass, Names.MethodMissing, out var methodMissing, out _))
        {
            _Raise(args);
        }

        Context.ExtendStack(callInfo.StackPointer + 5);
        var registers = Context.Stack.AsSpan(callInfo.StackPointer);

        registers[1] = NewArray(args);
        if (callInfo.KeywordArgumentCount == 0)
        {
            registers[2] = args[callInfo.BlockArgumentOffset];
        }
        else if (callInfo.KeywordArgumentPacked)
        {
            registers[2] = args[callInfo.ArgumentCount];
            registers[3] = args[callInfo.BlockArgumentOffset];
        }
        else
        {
            var hash = NewHash(callInfo.KeywordArgumentCount);
            foreach (var (key, value) in Context.GetKeywordArgs(ref callInfo))
            {
                hash[key] = value;
            }
            registers[2] = hash;
            registers[3] = args[callInfo.BlockArgumentOffset];
        }

        callInfo.MarkAsArgumentPacked();
        callInfo.MarkAsKeywordArgumentPacked();
        callInfo.MethodId = Names.MethodMissing;
        if (methodId != Names.MethodMissing)
        {
            callInfo.Scope = receiverClass;
        }

        return methodMissing;

        void _Raise(ReadOnlySpan<MRubyValue> args)
        {
            if (raise != null)
            {
                raise(this, receiver, methodId);
            }
            else
            {
                RaiseMethodMissing(methodId, receiver, NewArray(args));
            }
        }
    }
}
