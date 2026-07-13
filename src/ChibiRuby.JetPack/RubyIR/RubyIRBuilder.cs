using System;
using System.Collections.Generic;
using ChibiRuby.Internals;

using ChibiRuby;
using ChibiRuby.JetPack.Mrb2Cs;
namespace ChibiRuby.JetPack;

// One planned inline at a caller Send site. `Callee` is the body to splice in.
// When `GuardClass` is null the splice is unguarded (the receiver class is fixed
// by construction — used by isolation tests). When it is set, the splice is
// emitted behind a GuardInlineClass that deopts to the original Send on a class /
// method-cache-version miss, so the inline is safe in profile-driven production.
readonly struct RubyIRInlineSite(
    Irep callee,
    RClass? guardClass = null,
    int guardMethodCacheVersion = 0,
    ulong guardMethodFingerprint = 0,
    bool guardMissDeopts = false)
{
    public readonly Irep Callee = callee;
    public readonly RClass? GuardClass = guardClass;
    public readonly int GuardMethodCacheVersion = guardMethodCacheVersion;
    public readonly ulong GuardMethodFingerprint = guardMethodFingerprint;
    public readonly bool GuardMissDeopts = guardMissDeopts;
}

// Builds RubyIR (a high-level, type-inference-oriented IR) from mruby bytecode. `Build` is the
// entry point for the AOT codegen / analyses; it is NOT a high->low "lowering" and the result is
// NOT for execution — it is an analyzable IR. The bytecode walk emits the analysis-friendly op
// shapes directly (VirtualNew / VirtualGetField / VirtualSetField); the only post-build step is a
// multiply+add peephole fusion.
static class RubyIRBuilder
{
    // Build RubyIR from bytecode. Returns null (with `failure`) if the bytecode shape is
    // unsupported. The bytecode walk already emits the analysis-friendly shapes directly
    // (VirtualNew for `.new`, VirtualGetField/VirtualSetField for ivars); the only post-build
    // step is the multiply+add peephole fusion.
    public static RubyIRMethod? Build(Irep irep, int programCounter, out RubyIRBuildFailure failure)
    {
        var ir = TryCompile(irep, programCounter, out failure);
        return ir is null ? null : FuseMultiplyAdd(ir);
    }

    // Build RubyIR with an inline plan (splice the named callees at their caller Send sites).
    public static RubyIRMethod? Build(
        Irep irep,
        int programCounter,
        Dictionary<int, RubyIRInlineSite>? inlinePlan,
        out RubyIRBuildFailure failure)
    {
        var ir = TryCompile(irep, programCounter, inlinePlan, out failure);
        return ir is null ? null : FuseMultiplyAdd(ir);
    }

    // Peephole: fuse `Mul` immediately followed by an `Add`/`Sub` that consumes it (single-use,
    // not a branch target) into MulAdd / MulSub / SubMul. A pure optimization (fewer ops, and the
    // double-unbox path emits it as one a*b±c expression); the codegen handles the unfused form too.
    static RubyIRMethod FuseMultiplyAdd(RubyIRMethod ir)
    {
        var rewritten = FuseMultiplyAddInstructions(ir.Instructions.ToArray(), ir, out var oldToNew);
        if (oldToNew is null)
        {
            return ir;
        }

        return ir.CreateVariant(
            rewritten,
            loweredSourceIrep: ir.SourceIrep,
            loweredSourceBytecodePcs: RemapSourceBytecodePcs(ir, oldToNew));
    }

    static RubyIRInstruction[] FuseMultiplyAddInstructions(
        RubyIRInstruction[] source,
        RubyIRMethod ir,
        out int[]? oldToNewInstructionIndexes)
    {
        oldToNewInstructionIndexes = null;
        var useCounts = ir.CountValueUses(source);
        var branchTargets = ComputeBranchTargetInstructionIndexes(source);
        var changed = false;
        var removed = new bool[source.Length];

        for (var i = 0; i + 1 < source.Length; i++)
        {
            var multiply = source[i];
            var consumer = source[i + 1];
            if (multiply.OpCode != RubyIROpCode.Mul ||
                branchTargets[i + 1] ||
                useCounts[multiply.Dst] != 1 ||
                consumer.OpCode != RubyIROpCode.Add &&
                consumer.OpCode != RubyIROpCode.Sub ||
                consumer.Src0 != multiply.Dst &&
                consumer.Src1 != multiply.Dst)
            {
                continue;
            }

            var addend = consumer.Src0 == multiply.Dst
                ? consumer.Src1
                : consumer.Src0;
            var fusedOpCode = consumer.OpCode == RubyIROpCode.Add
                ? RubyIROpCode.MulAdd
                : consumer.Src0 == multiply.Dst
                    ? RubyIROpCode.MulSub
                    : RubyIROpCode.SubMul;
            source[i + 1] = new RubyIRInstruction(
                fusedOpCode,
                consumer.Dst,
                multiply.Src0,
                multiply.Src1,
                addend,
                consumer.Aux);
            removed[i] = true;
            changed = true;
        }

        if (!changed)
        {
            return source;
        }

        oldToNewInstructionIndexes = new int[source.Length + 1];
        var newLength = 0;
        for (var i = 0; i < source.Length; i++)
        {
            oldToNewInstructionIndexes[i] = newLength;
            if (!removed[i])
            {
                newLength++;
            }
        }
        oldToNewInstructionIndexes[source.Length] = newLength;

        var rewritten = new RubyIRInstruction[newLength];
        var rewrittenIndex = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (removed[i])
            {
                continue;
            }

            rewritten[rewrittenIndex++] = RemapBranchTarget(source[i], oldToNewInstructionIndexes);
        }

        return rewritten;
    }

    static int[]? RemapSourceBytecodePcs(RubyIRMethod ir, int[] oldToNewInstructionIndexes)
    {
        if (ir.SourceIrep is null)
        {
            return null;
        }

        var remapped = new int[oldToNewInstructionIndexes[^1]];
        for (var oldIndex = 0; oldIndex + 1 < oldToNewInstructionIndexes.Length; oldIndex++)
        {
            var newIndex = oldToNewInstructionIndexes[oldIndex];
            if (oldToNewInstructionIndexes[oldIndex + 1] == newIndex)
            {
                continue;
            }

            remapped[newIndex] = ir.SourceBytecodePc(oldIndex);
        }

        return remapped;
    }

    static bool[] ComputeBranchTargetInstructionIndexes(ReadOnlySpan<RubyIRInstruction> instructions)
    {
        var branchTargets = new bool[instructions.Length];
        for (var i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode is (
                    RubyIROpCode.Jump or
                    RubyIROpCode.JumpIfTruthy or
                    RubyIROpCode.JumpIfFalsy or
                    RubyIROpCode.JumpIfNil or
                    RubyIROpCode.GuardInlineClass) &&
                (uint)instruction.Aux < (uint)branchTargets.Length)
            {
                branchTargets[instruction.Aux] = true;
            }
        }

        return branchTargets;
    }

    static RubyIRInstruction RemapBranchTarget(
        RubyIRInstruction instruction,
        int[] oldToNewInstructionIndexes)
    {
        if (instruction.OpCode is not (
                RubyIROpCode.Jump or
                RubyIROpCode.JumpIfTruthy or
                RubyIROpCode.JumpIfFalsy or
                RubyIROpCode.JumpIfNil or
                RubyIROpCode.GuardInlineClass) ||
            (uint)instruction.Aux >= (uint)oldToNewInstructionIndexes.Length)
        {
            return instruction;
        }

        var target = oldToNewInstructionIndexes[instruction.Aux];
        return target == instruction.Aux
            ? instruction
            : new RubyIRInstruction(
                instruction.OpCode,
                instruction.Dst,
                instruction.Src0,
                instruction.Src1,
                instruction.Src2,
                target);
    }

    public static RubyIRMethod? TryCompile(Irep irep, int entryPoint) =>
        TryCompile(irep, entryPoint, out _);

    public static bool ContainsSelfRecursiveSend(Irep irep, Symbol methodId)
    {
        if (methodId.Value == 0)
        {
            return false;
        }

        var sequence = irep.Sequence;
        var symbols = irep.Symbols;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            switch (opCode)
            {
                case OpCode.SSend:
                    if (sequence[pc + 2] < symbols.Length && symbols[sequence[pc + 2]] == methodId)
                    {
                        return true;
                    }
                    break;
                case OpCode.SSend0:
                    if (sequence[pc + 2] < symbols.Length && symbols[sequence[pc + 2]] == methodId)
                    {
                        return true;
                    }
                    break;
            }

            if (!TryGetInstructionLength(sequence, pc, opCode, out var length))
            {
                return false;
            }
            pc += length;
        }

        return false;
    }

    static bool TryAnalyzeClosureCaptures(
        Irep irep,
        out bool[] closureCapturedRegisters,
        out RubyIRBuildFailure failure)
    {
        closureCapturedRegisters = new bool[irep.RegisterVariableCount];
        failure = RubyIRBuildFailure.None;

        var sequence = irep.Sequence;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (!TryGetInstructionLength(sequence, pc, opCode, out var length))
            {
                failure = new RubyIRBuildFailure(opCode, pc, "unsupported opcode");
                return false;
            }

            if (opCode is OpCode.Block)
            {
                var childIndex = sequence[pc + 2];
                if (childIndex >= irep.Children.Length)
                {
                    failure = new RubyIRBuildFailure(opCode, pc, "child irep out of range");
                    return false;
                }

                MarkDescendantUpVars(irep.Children[childIndex], 0, closureCapturedRegisters);
            }
            pc += length;
        }

        return true;
    }

    static void MarkDescendantUpVars(Irep irep, int depthToAncestor, bool[] ancestorRegisters)
    {
        var sequence = irep.Sequence;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (!TryGetInstructionLengthForControlScan(sequence, pc, opCode, out var length))
            {
                return;
            }

            if (opCode is OpCode.GetUpVar or OpCode.SetUpVar &&
                sequence[pc + 3] == depthToAncestor)
            {
                var register = sequence[pc + 2];
                if (register < ancestorRegisters.Length)
                {
                    ancestorRegisters[register] = true;
                }
            }
            else if (opCode is OpCode.Block)
            {
                var childIndex = sequence[pc + 2];
                if (childIndex < irep.Children.Length)
                {
                    MarkDescendantUpVars(
                        irep.Children[childIndex],
                        depthToAncestor + 1,
                        ancestorRegisters);
                }
            }

            pc += length;
        }
    }

    internal static bool BlockChildNeedsBytecodeBoundary(Irep irep, out string reason)
    {
        reason = string.Empty;
        var sequence = irep.Sequence;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (opCode is OpCode.Break or OpCode.ReturnBlk or OpCode.JmpUw)
            {
                reason = "non-local block control flow";
                return true;
            }

            if (!TryGetInstructionLengthForControlScan(sequence, pc, opCode, out var length))
            {
                reason = "unsupported block child opcode";
                return true;
            }
            pc += length;
        }

        foreach (var child in irep.Children)
        {
            if (BlockChildNeedsBytecodeBoundary(child, out reason))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BlockDescendantsContainNewObject(Irep[] children)
    {
        for (var i = 0; i < children.Length; i++)
        {
            if (IrepContainsNewObjectSend(children[i]))
            {
                return true;
            }
        }

        return false;
    }

    static bool IrepContainsNewObjectSend(Irep irep)
    {
        var sequence = irep.Sequence;
        var symbols = irep.Symbols;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (opCode is
                    OpCode.Send or
                    OpCode.SSend or
                    OpCode.SendB or
                    OpCode.SSendB or
                    OpCode.Send0 or
                    OpCode.SSend0)
            {
                var symbolIndex = sequence[pc + 2];
                if ((uint)symbolIndex < (uint)symbols.Length &&
                    symbols[symbolIndex] == Names.New)
                {
                    return true;
                }
            }
            else if (opCode is OpCode.Block)
            {
                var childIndex = sequence[pc + 2];
                if ((uint)childIndex < (uint)irep.Children.Length &&
                    IrepContainsNewObjectSend(irep.Children[childIndex]))
                {
                    return true;
                }
            }

            if (!TryGetInstructionLengthForControlScan(sequence, pc, opCode, out var length))
            {
                return true;
            }
            pc += length;
        }

        return false;
    }

    public static RubyIRMethod? TryCompile(Irep irep, int entryPoint, out RubyIRBuildFailure failure) =>
        TryCompile(irep, entryPoint, (Dictionary<int, RubyIRInlineSite>?)null, out failure);

    public static RubyIRMethod? TryCompile(
        Irep irep,
        int entryPoint,
        Dictionary<int, Irep>? inlinePlan,
        out RubyIRBuildFailure failure)
    {
        Dictionary<int, RubyIRInlineSite>? sites = null;
        if (inlinePlan is not null)
        {
            sites = new Dictionary<int, RubyIRInlineSite>(inlinePlan.Count);
            foreach (var (pc, callee) in inlinePlan)
            {
                sites[pc] = new RubyIRInlineSite(callee);
            }
        }

        return TryCompile(irep, entryPoint, sites, out failure);
    }

    public static RubyIRMethod? TryCompile(
        Irep irep,
        int entryPoint,
        Dictionary<int, RubyIRInlineSite>? inlinePlan,
        out RubyIRBuildFailure failure)
    {
        failure = RubyIRBuildFailure.None;

        if (entryPoint != 0)
        {
            failure = new RubyIRBuildFailure(null, entryPoint, "non-zero entry point");
            return null;
        }

        if (irep.CatchHandlers.Length != 0)
        {
            failure = new RubyIRBuildFailure(null, entryPoint, "catch handler");
            return null;
        }

        if (!AnalyzeForwardBranches(irep, out var mergeSlotRegisters, out var hasBackwardBranch, out failure))
        {
            return null;
        }
        if (!TryAnalyzeClosureCaptures(irep, out var closureCapturedRegisters, out failure))
        {
            return null;
        }
        for (var i = 0; i < closureCapturedRegisters.Length; i++)
        {
            if (closureCapturedRegisters[i])
            {
                mergeSlotRegisters[i] = true;
            }
        }

        var builder = new Builder(irep, mergeSlotRegisters, closureCapturedRegisters, hasBackwardBranch);
        if (!TryLowerSequence(builder, irep, inlinePlan, out failure))
        {
            return null;
        }

        if (builder.InstructionCount == 0)
        {
            failure = new RubyIRBuildFailure(null, entryPoint, "empty method");
            return null;
        }

        return builder.TryBuild(out var ir, out failure) ? ir : null;
    }

    // Lowers an irep's bytecode body into `builder`. Extracted from TryCompile so
    // the same translation can re-lower a callee's body into a caller's builder
    // when inlining (the builder applies register/pc offsets transparently).
    static bool TryLowerSequence(
        Builder builder,
        Irep irep,
        Dictionary<int, RubyIRInlineSite>? inlinePlan,
        out RubyIRBuildFailure failure)
    {
        failure = RubyIRBuildFailure.None;
        var sequence = irep.Sequence;
        var symbols = irep.Symbols;
        var pc = 0;

        return LowerLoop(builder, irep, inlinePlan, sequence, symbols, ref pc, out failure);
    }

    // Re-lower `site.Callee` into `builder` as an inlined body bound to the
    // caller's `receiverRegister`/args. Returns false (without emitting anything)
    // when the callee is not inline-safe. v1 conservatively requires the callee
    // to compile standalone and to contain no upvars/blocks/catch handlers, so
    // re-lowering it inline cannot fail partway through.
    //
    // When `site.GuardClass` is set, the body is fenced by a GuardInlineClass that
    // jumps to the spliced body on a class/method-version match and otherwise
    // falls through to a cold copy of the original `methodId` Send. Both paths
    // write the shared inline-result slot and merge at the continuation, so the
    // inline is speculative-safe: a receiver of a different class deopts cleanly.
    static bool TryInlineCallee(
        Builder builder,
        RubyIRInlineSite site,
        int destinationRegister,
        ushort receiver,
        int firstArgumentRegister,
        int argc,
        Symbol methodId)
    {
        var calleeIrep = site.Callee;
        if (calleeIrep.CatchHandlers.Length != 0 ||
            calleeIrep.RegisterVariableCount <= argc ||
            CalleeHasInlineUnsafeOpcodes(calleeIrep) ||
            !TryReadCalleeArity(calleeIrep, out var mandatoryArgs, out var bodyPc) ||
            mandatoryArgs != argc ||
            TryCompile(calleeIrep, 0, out _) is null ||
            !AnalyzeForwardBranches(calleeIrep, out var calleeMergeSlots, out var calleeHasBackwardBranch, out _) ||
            calleeHasBackwardBranch)
        {
            return false;
        }

        Span<ushort> args = argc == 0 ? default : stackalloc ushort[argc];
        for (var i = 0; i < argc; i++)
        {
            args[i] = builder.Read(firstArgumentRegister + i);
        }

        var guarded = site.GuardClass is not null;
        var resultValueId = builder.BeginInline(
            calleeIrep.RegisterVariableCount,
            calleeMergeSlots,
            receiver,
            args,
            calleeIrep.Sequence.Length,
            guarded);

        if (guarded)
        {
            if (site.GuardMissDeopts)
            {
                // AOT scalar replacement can then ignore the miss path entirely:
                // the generated method returns false and the VM interprets from
                // the original bytecode, where all objects are materialized.
                builder.EmitInlineGuardDeopt(
                    receiver,
                    methodId,
                    site.GuardClass!,
                    site.GuardMethodCacheVersion,
                    site.GuardMethodFingerprint);
            }
            else
            {
                // Emit guard + cold Send before the body so the guard, on a match,
                // jumps over the cold path into the spliced body (callee pc 0).
                builder.EmitInlineGuardAndColdSend(
                    receiver,
                    args,
                    methodId,
                    site.GuardClass!,
                    site.GuardMethodCacheVersion,
                    site.GuardMethodFingerprint,
                    resultValueId);
            }
        }

        // Validated above, so this cannot fail; ignore failure.
        TryLowerSequence(builder, calleeIrep, null, out _);
        builder.EndInline(destinationRegister, resultValueId);
        return true;
    }

    // Bytecode pc of the first plain (non-self) Send/Send0 to `methodId`, or -1.
    // Used to key inline plans by call-site pc.
    internal static int FindFirstSendPc(Irep irep, Symbol methodId)
    {
        var sequence = irep.Sequence;
        var symbols = irep.Symbols;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (opCode is OpCode.Send or OpCode.Send0 &&
                sequence[pc + 2] < symbols.Length &&
                symbols[sequence[pc + 2]] == methodId)
            {
                return pc;
            }

            if (!TryGetInstructionLength(sequence, pc, opCode, out var length))
            {
                return -1;
            }
            pc += length;
        }

        return -1;
    }

    internal static Dictionary<int, RubyIRInlineSite>? TryBuildSelfInlinePlan(
        MRubyState state,
        Irep callerIrep,
        RClass definingClass,
        IReadOnlyDictionary<ulong, int> inlineRegistry,
        IReadOnlyDictionary<Symbol, InlineSelectorTarget>? inlineSelectorRegistry = null,
        HashSet<int>? candidatePcs = null)
    {
        if (Environment.GetEnvironmentVariable("AOT_NOSPLICE") == "1")
        {
            return null;
        }

        var noSelfSplice = Environment.GetEnvironmentVariable("AOT_NOSELFSPLICE") == "1";
        var noCrossSplice = Environment.GetEnvironmentVariable("AOT_NOCROSSSPLICE") == "1";
        Dictionary<int, RubyIRInlineSite>? plan = null;
        var sequence = callerIrep.Sequence;
        var symbols = callerIrep.Symbols;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if ((candidatePcs is null || candidatePcs.Contains(pc)) &&
                opCode is OpCode.Send or OpCode.SSend or OpCode.Send0 or OpCode.SSend0 &&
                sequence[pc + 2] < symbols.Length)
            {
                var hasArgs = opCode is OpCode.Send or OpCode.SSend;
                var argc = hasArgs ? sequence[pc + 3] & 0xf : 0;
                var kargc = hasArgs ? (sequence[pc + 3] >> 4) & 0xf : 0;
                var methodId = symbols[sequence[pc + 2]];
                if (!noSelfSplice && kargc == 0 && (opCode is OpCode.SSend or OpCode.SSend0) &&
                    state.TryFindMethod(definingClass, methodId, out var selfMethod, out _) &&
                    selfMethod.Proc is { } selfProc)
                {
                    var fp = state.ComputeIrepFingerprint(selfProc.Irep);
                    if (inlineRegistry.TryGetValue(fp, out var calleeArgc) && calleeArgc == argc)
                    {
                        plan ??= new Dictionary<int, RubyIRInlineSite>();
                        plan[pc] = new RubyIRInlineSite(
                            selfProc.Irep,
                            definingClass,
                            guardMethodCacheVersion: 0,
                            guardMethodFingerprint: fp,
                            guardMissDeopts: true);
                    }
                }
                else if (!noCrossSplice && kargc == 0 && (opCode is OpCode.Send or OpCode.Send0) &&
                         inlineSelectorRegistry is not null &&
                         inlineSelectorRegistry.TryGetValue(methodId, out var target) &&
                         target.ArgCount == argc)
                {
                    plan ??= new Dictionary<int, RubyIRInlineSite>();
                    plan[pc] = new RubyIRInlineSite(
                        target.Irep,
                        target.DefiningClass,
                        guardMethodCacheVersion: 0,
                        guardMethodFingerprint: target.Fingerprint,
                        guardMissDeopts: true);
                }
            }

            if (!TryGetInstructionLength(sequence, pc, opCode, out var length))
            {
                return plan;
            }
            pc += length;
        }

        return plan;
    }

    static bool TryReadCalleeArity(Irep calleeIrep, out int mandatoryArgs, out int bodyPc)
    {
        mandatoryArgs = 0;
        bodyPc = 0;
        var sequence = calleeIrep.Sequence;
        if (sequence.Length == 0 || (OpCode)sequence[0] != OpCode.Enter)
        {
            return false;
        }

        var aspec = new ArgumentSpec(ReadUInt24(sequence, 1));
        if (aspec.OptionalArgumentsCount != 0 ||
            aspec.TakeRestArguments ||
            aspec.MandatoryArguments2Count != 0 ||
            aspec.KeywordArgumentsCount != 0 ||
            aspec.TakeKeywordDict ||
            aspec.TakeBlock)
        {
            return false;
        }

        mandatoryArgs = aspec.MandatoryArguments1Count;
        bodyPc = 4;
        return true;
    }

    // Inlining re-binds the callee's self/args to caller values, so a callee that
    // reads an enclosing scope (upvars) or creates closures cannot be inlined as
    // a flat body without breaking those scope references.
    // True if the callee body contains a (forward) branch. SSA inline splicing is
    // restricted to such callees: a straight-line callee is already served by the
    // trivial-getter / expression-tree InlineBody path or a guarded direct call,
    // so splicing only adds the capability those paths lack — inlining branches.
    internal static bool CalleeHasControlFlow(Irep irep)
    {
        var sequence = irep.Sequence;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (opCode is OpCode.Jmp or OpCode.JmpIf or OpCode.JmpNot or OpCode.JmpNil)
            {
                return true;
            }

            if (!TryGetInstructionLengthForControlScan(sequence, pc, opCode, out var length))
            {
                return false;
            }
            pc += length;
        }

        return false;
    }

    static bool CalleeHasInlineUnsafeOpcodes(Irep irep)
    {
        var sequence = irep.Sequence;
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (opCode is OpCode.GetUpVar or OpCode.SetUpVar or OpCode.Block or
                OpCode.SendB or OpCode.SSendB)
            {
                return true;
            }

            if (!TryGetInstructionLengthForControlScan(sequence, pc, opCode, out var length))
            {
                return true;
            }
            pc += length;
        }

        return false;
    }

    static bool LowerLoop(
        Builder builder,
        Irep irep,
        Dictionary<int, RubyIRInlineSite>? inlinePlan,
        byte[] sequence,
        Symbol[] symbols,
        ref int pc,
        out RubyIRBuildFailure failure)
    {
        failure = RubyIRBuildFailure.None;

        while (pc < sequence.Length)
        {
            builder.MarkBytecodePc(pc);
            var opCode = (OpCode)sequence[pc];
            switch (opCode)
            {
                case OpCode.Nop:
                    pc += 1;
                    break;
                case OpCode.Enter:
                {
                    var bits = ReadUInt24(sequence, pc + 1);
                    var aspec = new ArgumentSpec(bits);
                    if (aspec.OptionalArgumentsCount != 0 ||
                        aspec.TakeRestArguments ||
                        aspec.MandatoryArguments2Count != 0 ||
                        aspec.KeywordArgumentsCount != 0 ||
                        aspec.TakeKeywordDict ||
                        aspec.TakeBlock ||
                        aspec.MandatoryArguments1Count >= MRubyCallInfo.CallMaxArgs)
                    {
                        failure = new RubyIRBuildFailure(opCode, pc, "complex argument spec");
                        return false;
                    }
                    // When inlining, arguments are already bound to the caller's
                    // values, so the callee's arity check is unnecessary.
                    if (!builder.Inlining)
                    {
                        builder.Add(new RubyIRInstruction(
                            RubyIROpCode.CheckArity,
                            aux: aspec.MandatoryArguments1Count));
                    }
                    pc += 4;
                    break;
                }
                case OpCode.Move:
                    builder.Move(sequence[pc + 1], sequence[pc + 2]);
                    pc += 3;
                    break;
                case OpCode.LoadL:
                    builder.DefineValue(sequence[pc + 1], irep.PoolValues[sequence[pc + 2]]);
                    pc += 3;
                    break;
                case OpCode.LoadI8:
                    builder.DefineValue(sequence[pc + 1], new MRubyValue(sequence[pc + 2]));
                    pc += 3;
                    break;
                case OpCode.LoadINeg:
                    builder.DefineValue(sequence[pc + 1], new MRubyValue(-sequence[pc + 2]));
                    pc += 3;
                    break;
                case OpCode.LoadI__1:
                case OpCode.LoadI_0:
                case OpCode.LoadI_1:
                case OpCode.LoadI_2:
                case OpCode.LoadI_3:
                case OpCode.LoadI_4:
                case OpCode.LoadI_5:
                case OpCode.LoadI_6:
                case OpCode.LoadI_7:
                    builder.DefineValue(
                        sequence[pc + 1],
                        new MRubyValue((int)opCode - (int)OpCode.LoadI_0));
                    pc += 2;
                    break;
                case OpCode.LoadI16:
                    builder.DefineValue(
                        sequence[pc + 1],
                        new MRubyValue(unchecked((short)ReadUInt16(sequence, pc + 2))));
                    pc += 4;
                    break;
                case OpCode.LoadI32:
                    builder.DefineValue(
                        sequence[pc + 1],
                        new MRubyValue(unchecked((int)ReadUInt32FromTwoUInt16(sequence, pc + 2))));
                    pc += 6;
                    break;
                case OpCode.LoadSym:
                    builder.DefineValue(sequence[pc + 1], new MRubyValue(symbols[sequence[pc + 2]]));
                    pc += 3;
                    break;
                case OpCode.LoadNil:
                    builder.DefineValue(sequence[pc + 1], MRubyValue.Nil);
                    pc += 2;
                    break;
                case OpCode.LoadSelf:
                    builder.DefineSelf(sequence[pc + 1]);
                    pc += 2;
                    break;
                case OpCode.GetUpVar:
                    builder.DefineUpVar(
                        sequence[pc + 1],
                        sequence[pc + 2],
                        sequence[pc + 3]);
                    pc += 4;
                    break;
                case OpCode.SetUpVar:
                    builder.SetUpVar(
                        sequence[pc + 1],
                        sequence[pc + 2],
                        sequence[pc + 3]);
                    pc += 4;
                    break;
                case OpCode.GetConst:
                    builder.DefineSymbolOp(
                        RubyIROpCode.GetConstant,
                        sequence[pc + 1],
                        symbols[sequence[pc + 2]]);
                    pc += 3;
                    break;
                case OpCode.GetMCnst:
                    builder.DefineSymbolOp(
                        RubyIROpCode.GetModuleConstant,
                        sequence[pc + 1],
                        symbols[sequence[pc + 2]],
                        builder.Read(sequence[pc + 1]));
                    pc += 3;
                    break;
                case OpCode.LoadT:
                    builder.DefineValue(sequence[pc + 1], MRubyValue.True);
                    pc += 2;
                    break;
                case OpCode.LoadF:
                    builder.DefineValue(sequence[pc + 1], MRubyValue.False);
                    pc += 2;
                    break;
                case OpCode.GetIV:
                    builder.DefineSymbolOp(
                        RubyIROpCode.GetInstanceVariable,
                        sequence[pc + 1],
                        symbols[sequence[pc + 2]],
                        builder.Self);
                    pc += 3;
                    break;
                case OpCode.SetIV:
                    builder.Add(new RubyIRInstruction(
                        RubyIROpCode.SetInstanceVariable,
                        src0: builder.Self,
                        src1: builder.Read(sequence[pc + 1]),
                        aux: builder.Symbol(symbols[sequence[pc + 2]])));
                    pc += 3;
                    break;
                case OpCode.GetIdx:
                    builder.DefineBinaryOp(
                        RubyIROpCode.GetIndex,
                        sequence[pc + 1],
                        sequence[pc + 1],
                        sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.GetIdx0:
                    builder.DefineOp(
                        RubyIROpCode.GetIndex0,
                        sequence[pc + 1],
                        builder.Read(sequence[pc + 2]));
                    pc += 3;
                    break;
                case OpCode.SetIdx:
                    builder.DefineTernaryOp(
                        RubyIROpCode.SetIndex,
                        sequence[pc + 1],
                        sequence[pc + 1],
                        sequence[pc + 1] + 1,
                        sequence[pc + 1] + 2);
                    pc += 2;
                    break;
                case OpCode.Jmp:
                // JmpUw (`break` out of a while loop) is a plain unconditional jump in a
                // catch-handler-free method (TryCompile requires no catch handlers, so the VM's
                // ensure-unwind branch never fires) — lower it identically to Jmp.
                case OpCode.JmpUw:
                {
                    // Backward targets (loops) are accepted: the codegen lowers them to a C# `goto`
                    // and AnalyzeForwardBranches has already forced every register to a merge slot
                    // (in-place boxed value-id) so loop-carried values round-trip correctly.
                    var targetPc = pc + 3 + unchecked((short)ReadUInt16(sequence, pc + 1));
                    builder.AddBranch(RubyIROpCode.Jump, targetPc);
                    pc += 3;
                    break;
                }
                case OpCode.JmpIf:
                {
                    var targetPc = ResolveShortCircuitBranchTarget(
                        sequence,
                        opCode,
                        sequence[pc + 1],
                        pc + 4 + unchecked((short)ReadUInt16(sequence, pc + 2)));
                    builder.AddBranch(RubyIROpCode.JumpIfTruthy, targetPc, builder.Read(sequence[pc + 1]));
                    pc += 4;
                    break;
                }
                case OpCode.JmpNot:
                {
                    var targetPc = ResolveShortCircuitBranchTarget(
                        sequence,
                        opCode,
                        sequence[pc + 1],
                        pc + 4 + unchecked((short)ReadUInt16(sequence, pc + 2)));
                    builder.AddBranch(RubyIROpCode.JumpIfFalsy, targetPc, builder.Read(sequence[pc + 1]));
                    pc += 4;
                    break;
                }
                case OpCode.JmpNil:
                {
                    var targetPc = ResolveShortCircuitBranchTarget(
                        sequence,
                        opCode,
                        sequence[pc + 1],
                        pc + 4 + unchecked((short)ReadUInt16(sequence, pc + 2)));
                    builder.AddBranch(RubyIROpCode.JumpIfNil, targetPc, builder.Read(sequence[pc + 1]));
                    pc += 4;
                    break;
                }
                case OpCode.Send:
                case OpCode.SSend:
                    if (opCode == OpCode.Send &&
                        inlinePlan is not null &&
                        inlinePlan.TryGetValue(pc, out var sendSite) &&
                        TryInlineCallee(
                            builder,
                            sendSite,
                            sequence[pc + 1],
                            builder.Read(sequence[pc + 1]),
                            sequence[pc + 1] + 1,
                            sequence[pc + 3] & 0xf,
                            symbols[sequence[pc + 2]]))
                    {
                        pc += 4;
                        break;
                    }
                    if (opCode == OpCode.SSend &&
                        inlinePlan is not null &&
                        inlinePlan.TryGetValue(pc, out var selfSendSite) &&
                        TryInlineCallee(
                            builder,
                            selfSendSite,
                            sequence[pc + 1],
                            builder.Self,
                            sequence[pc + 1] + 1,
                            sequence[pc + 3] & 0xf,
                            symbols[sequence[pc + 2]]))
                    {
                        pc += 4;
                        break;
                    }
                    builder.DefineSend(
                        opCode == OpCode.Send ? RubyIROpCode.Send : RubyIROpCode.SendSelf,
                        sequence[pc + 1],
                        sequence[pc + 3] & 0xf,
                        symbols[sequence[pc + 2]]);
                    pc += 4;
                    break;
                case OpCode.SendB:
                case OpCode.SSendB:
                {
                    var register = sequence[pc + 1];
                    var callFlags = sequence[pc + 3];
                    var argc = callFlags & 0xf;
                    var kargc = (callFlags >> 4) & 0xf;
                    if (argc >= MRubyCallInfo.CallMaxArgs || kargc != 0)
                    {
                        failure = new RubyIRBuildFailure(opCode, pc, "complex block send");
                        return false;
                    }

                    var blockRegister = register + MRubyCallInfo.CalculateBlockArgumentOffset(argc, kargc);
                    builder.DefineSendBlock(
                        opCode == OpCode.SendB ? RubyIROpCode.SendBlock : RubyIROpCode.SendSelfBlock,
                        register,
                        argc,
                        blockRegister,
                        symbols[sequence[pc + 2]]);
                    pc += 4;
                    break;
                }
                case OpCode.Send0:
                case OpCode.SSend0:
                    if (opCode == OpCode.Send0 &&
                        inlinePlan is not null &&
                        inlinePlan.TryGetValue(pc, out var send0Site) &&
                        TryInlineCallee(
                            builder,
                            send0Site,
                            sequence[pc + 1],
                            builder.Read(sequence[pc + 1]),
                            sequence[pc + 1] + 1,
                            0,
                            symbols[sequence[pc + 2]]))
                    {
                        pc += 3;
                        break;
                    }
                    if (opCode == OpCode.SSend0 &&
                        inlinePlan is not null &&
                        inlinePlan.TryGetValue(pc, out var selfSend0Site) &&
                        TryInlineCallee(
                            builder,
                            selfSend0Site,
                            sequence[pc + 1],
                            builder.Self,
                            sequence[pc + 1] + 1,
                            0,
                            symbols[sequence[pc + 2]]))
                    {
                        pc += 3;
                        break;
                    }
                    builder.DefineSend(
                        opCode == OpCode.Send0 ? RubyIROpCode.Send : RubyIROpCode.SendSelf,
                        sequence[pc + 1],
                        0,
                        symbols[sequence[pc + 2]]);
                    pc += 3;
                    break;
                case OpCode.Add:
                    builder.DefineBinaryOp(RubyIROpCode.Add, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.AddI:
                    builder.DefineImmediateOp(
                        RubyIROpCode.AddImmediate,
                        sequence[pc + 1],
                        sequence[pc + 1],
                        new MRubyValue(sequence[pc + 2]));
                    pc += 3;
                    break;
                case OpCode.AddILV:
                    builder.DefineImmediateOp(
                        RubyIROpCode.AddImmediate,
                        sequence[pc + 1],
                        sequence[pc + 1],
                        new MRubyValue(sequence[pc + 3]));
                    pc += 4;
                    break;
                case OpCode.Sub:
                    builder.DefineBinaryOp(RubyIROpCode.Sub, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.SubI:
                    builder.DefineImmediateOp(
                        RubyIROpCode.SubImmediate,
                        sequence[pc + 1],
                        sequence[pc + 1],
                        new MRubyValue(sequence[pc + 2]));
                    pc += 3;
                    break;
                case OpCode.SubILV:
                    builder.DefineImmediateOp(
                        RubyIROpCode.SubImmediate,
                        sequence[pc + 1],
                        sequence[pc + 1],
                        new MRubyValue(sequence[pc + 3]));
                    pc += 4;
                    break;
                case OpCode.Mul:
                    builder.DefineBinaryOp(RubyIROpCode.Mul, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.Div:
                    builder.DefineBinaryOp(RubyIROpCode.Div, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.EQ:
                    builder.DefineBinaryOp(RubyIROpCode.Eq, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.LT:
                    builder.DefineBinaryOp(RubyIROpCode.Lt, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.LE:
                    builder.DefineBinaryOp(RubyIROpCode.Le, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.GT:
                    builder.DefineBinaryOp(RubyIROpCode.Gt, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.GE:
                    builder.DefineBinaryOp(RubyIROpCode.Ge, sequence[pc + 1], sequence[pc + 1], sequence[pc + 1] + 1);
                    pc += 2;
                    break;
                case OpCode.Array:
                    builder.DefineArray(sequence[pc + 1], sequence[pc + 1], sequence[pc + 2]);
                    pc += 3;
                    break;
                case OpCode.Array2:
                    builder.DefineArray(sequence[pc + 1], sequence[pc + 2], sequence[pc + 3]);
                    pc += 4;
                    break;
                case OpCode.Hash:
                    // OP_HASH R B: build a hash from B key/value pairs at R..R+2B-1, result in R.
                    builder.DefineHash(sequence[pc + 1], sequence[pc + 2]);
                    pc += 3;
                    break;
                case OpCode.ARef:
                    builder.DefineOp(
                        RubyIROpCode.ArrayRef,
                        sequence[pc + 1],
                        builder.Read(sequence[pc + 2]),
                        aux: sequence[pc + 3]);
                    pc += 4;
                    break;
                case OpCode.ASet:
                    builder.Add(new RubyIRInstruction(
                        RubyIROpCode.ArraySet,
                        src0: builder.Read(sequence[pc + 2]),
                        src1: builder.Read(sequence[pc + 1]),
                        aux: sequence[pc + 3]));
                    pc += 4;
                    break;
                case OpCode.Block:
                    if (sequence[pc + 2] >= irep.Children.Length)
                    {
                        failure = new RubyIRBuildFailure(opCode, pc, "child irep out of range");
                        return false;
                    }

                    var child = irep.Children[sequence[pc + 2]];
                    if (BlockChildNeedsBytecodeBoundary(child, out var boundaryReason))
                    {
                        failure = new RubyIRBuildFailure(opCode, pc, boundaryReason);
                        return false;
                    }

                    var nextPc = pc + 3;
                    if ((uint)(nextPc + 3) < (uint)sequence.Length &&
                        (OpCode)sequence[nextPc] is OpCode.SendB or OpCode.SSendB)
                    {
                        var sendOpCode = (OpCode)sequence[nextPc];
                        var sendRegister = sequence[nextPc + 1];
                        var sendCallFlags = sequence[nextPc + 3];
                        var sendArgc = sendCallFlags & 0xf;
                        var sendKargc = (sendCallFlags >> 4) & 0xf;
                        if (sendArgc < MRubyCallInfo.CallMaxArgs && sendKargc == 0)
                        {
                            var blockRegister = sendRegister + MRubyCallInfo.CalculateBlockArgumentOffset(sendArgc, sendKargc);
                            if (blockRegister == sequence[pc + 1])
                            {
                                builder.DefineSendBlockDescriptor(
                                    sendOpCode == OpCode.SendB
                                        ? RubyIROpCode.SendBlockDescriptor
                                        : RubyIROpCode.SendSelfBlockDescriptor,
                                    sendRegister,
                                    sendArgc,
                                    child,
                                    symbols[sequence[nextPc + 2]]);
                                pc += 7;
                                break;
                            }
                        }
                    }

                    builder.DefineBlock(sequence[pc + 1], child);
                    pc += 3;
                    break;
                case OpCode.Return:
                    builder.EmitReturn(sequence[pc + 1]);
                    pc += 2;
                    break;
                case OpCode.RetSelf:
                    builder.EmitReturnSelf();
                    pc += 1;
                    break;
                case OpCode.RetNil:
                    builder.EmitReturnLiteral(MRubyValue.Nil);
                    pc += 1;
                    break;
                case OpCode.RetTrue:
                    builder.EmitReturnLiteral(MRubyValue.True);
                    pc += 1;
                    break;
                case OpCode.RetFalse:
                    builder.EmitReturnLiteral(MRubyValue.False);
                    pc += 1;
                    break;
                default:
                    failure = new RubyIRBuildFailure(opCode, pc, "unsupported opcode");
                    return false;
            }
        }

        builder.MarkBytecodePc(sequence.Length);
        return true;
    }

    static uint ReadUInt16(byte[] sequence, int offset) =>
        (uint)((sequence[offset] << 8) | sequence[offset + 1]);

    static uint ReadUInt24(byte[] sequence, int offset) =>
        ((uint)sequence[offset] << 16) | ((uint)sequence[offset + 1] << 8) | sequence[offset + 2];

    static uint ReadUInt32FromTwoUInt16(byte[] sequence, int offset) =>
        (ReadUInt16(sequence, offset) << 16) | ReadUInt16(sequence, offset + 2);

    static bool AnalyzeForwardBranches(
        Irep irep,
        out bool[] mergeSlotRegisters,
        out bool hasBackwardBranch,
        out RubyIRBuildFailure failure)
    {
        failure = RubyIRBuildFailure.None;
        hasBackwardBranch = false;

        var sequence = irep.Sequence;
        var registerCount = irep.RegisterVariableCount;
        mergeSlotRegisters = new bool[registerCount];
        var pc = 0;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (!TryGetInstructionLength(sequence, pc, opCode, out var length))
            {
                failure = new RubyIRBuildFailure(opCode, pc, "unsupported opcode");
                return false;
            }

            var nextPc = pc + length;
            switch (opCode)
            {
                case OpCode.Jmp:
                case OpCode.JmpUw:
                {
                    var targetPc = nextPc + unchecked((short)ReadUInt16(sequence, pc + 1));
                    // A backward target is a loop back-edge. The forward-range merge-slot analysis
                    // only models conditionally-skipped FORWARD ranges, so it can't run here; instead
                    // we flag the method as looping and (below) force every register to a merge slot,
                    // which gives each one a single in-place boxed value-id — exactly what a
                    // loop-carried register needs (header reads it, body rewrites it).
                    if (targetPc < nextPc)
                    {
                        hasBackwardBranch = true;
                        break;
                    }
                    if (!AnalyzeForwardBranchRange(
                            sequence,
                            registerCount,
                            mergeSlotRegisters,
                            nextPc,
                            targetPc,
                            opCode,
                            pc,
                            out failure))
                    {
                        return false;
                    }
                    break;
                }
                case OpCode.JmpIf:
                case OpCode.JmpNot:
                case OpCode.JmpNil:
                {
                    var targetPc = ResolveShortCircuitBranchTarget(
                        sequence,
                        opCode,
                        sequence[pc + 1],
                        nextPc + unchecked((short)ReadUInt16(sequence, pc + 2)));
                    if (targetPc < nextPc)
                    {
                        hasBackwardBranch = true;
                        break;
                    }
                    if (!AnalyzeForwardBranchRange(
                            sequence,
                            registerCount,
                            mergeSlotRegisters,
                            nextPc,
                            targetPc,
                            opCode,
                            pc,
                            out failure))
                    {
                        return false;
                    }
                    break;
                }
            }

            pc = nextPc;
        }

        if (hasBackwardBranch)
        {
            // Non-SSA, fully-boxed loop model: every register becomes a merge slot so the builder
            // emits one value-id per register, reassigned in place across the back-edge.
            for (var r = 0; r < mergeSlotRegisters.Length; r++)
            {
                mergeSlotRegisters[r] = true;
            }
        }

        return true;
    }

    static int ResolveShortCircuitBranchTarget(
        byte[] sequence,
        OpCode opCode,
        int conditionRegister,
        int targetPc)
    {
        while (targetPc >= 0 &&
               targetPc + 4 <= sequence.Length &&
               (OpCode)sequence[targetPc] == opCode &&
               sequence[targetPc + 1] == conditionRegister)
        {
            var nextTargetPc = targetPc + 4 + unchecked((short)ReadUInt16(sequence, targetPc + 2));
            if (nextTargetPc <= targetPc)
            {
                break;
            }

            targetPc = nextTargetPc;
        }

        return targetPc;
    }

    static bool AnalyzeForwardBranchRange(
        byte[] sequence,
        int registerCount,
        bool[] mergeSlotRegisters,
        int startPc,
        int targetPc,
        OpCode branchOpCode,
        int branchPc,
        out RubyIRBuildFailure failure)
    {
        failure = RubyIRBuildFailure.None;
        if (targetPc < startPc)
        {
            failure = new RubyIRBuildFailure(branchOpCode, branchPc, "backward branch");
            return false;
        }

        if (targetPc > sequence.Length)
        {
            failure = new RubyIRBuildFailure(branchOpCode, branchPc, "branch target out of range");
            return false;
        }

        if (targetPc == startPc)
        {
            return true;
        }

        var definedInSkippedRange = new bool[registerCount];
        var pc = startPc;
        while (pc < targetPc)
        {
            var opCode = (OpCode)sequence[pc];
            if (!TryGetInstructionLength(sequence, pc, opCode, out var length) ||
                pc + length > targetPc ||
                !TryGetRegisterAccesses(sequence, pc, opCode, out _, out var write0, out var write1))
            {
                failure = new RubyIRBuildFailure(opCode, pc, "unsupported branch body");
                return false;
            }

            MarkDefined(definedInSkippedRange, write0);
            MarkDefined(definedInSkippedRange, write1);
            pc += length;
        }

        for (var register = 0; register < definedInSkippedRange.Length; register++)
        {
            if (definedInSkippedRange[register] &&
                IsRegisterReadBeforeWrite(sequence, targetPc, register, out _, out _))
            {
                mergeSlotRegisters[register] = true;
            }
        }

        return true;
    }

    static bool IsRegisterReadBeforeWrite(
        byte[] sequence,
        int startPc,
        int register,
        out int readPc,
        out OpCode readOpCode)
    {
        var pc = startPc;
        while (pc < sequence.Length)
        {
            var opCode = (OpCode)sequence[pc];
            if (!TryGetInstructionLength(sequence, pc, opCode, out var length) ||
                !TryGetRegisterAccesses(sequence, pc, opCode, out var reads, out var write0, out var write1))
            {
                break;
            }

            if (ReadsRegister(reads, register))
            {
                readPc = pc;
                readOpCode = opCode;
                return true;
            }

            if (write0 == register || write1 == register)
            {
                break;
            }

            pc += length;
        }

        readPc = -1;
        readOpCode = default;
        return false;
    }

    static void MarkDefined(bool[] registers, int register)
    {
        if ((uint)register < (uint)registers.Length)
        {
            registers[register] = true;
        }
    }

    static bool ReadsRegister(RegisterReads reads, int register)
    {
        for (var i = 0; i < reads.Count; i++)
        {
            if (reads[i] == register)
            {
                return true;
            }
        }

        return false;
    }

    static bool TryGetInstructionLength(byte[] sequence, int pc, OpCode opCode, out int length)
    {
        length = opCode switch
        {
            OpCode.Nop or
            OpCode.Call or
            OpCode.RetSelf or
            OpCode.RetNil or
            OpCode.RetTrue or
            OpCode.RetFalse => 1,
            OpCode.LoadI__1 or
            OpCode.LoadI_0 or
            OpCode.LoadI_1 or
            OpCode.LoadI_2 or
            OpCode.LoadI_3 or
            OpCode.LoadI_4 or
            OpCode.LoadI_5 or
            OpCode.LoadI_6 or
            OpCode.LoadI_7 or
            OpCode.LoadNil or
            OpCode.LoadSelf or
            OpCode.LoadT or
            OpCode.LoadF or
            OpCode.GetIdx or
            OpCode.SetIdx or
            OpCode.Add or
            OpCode.Sub or
            OpCode.Mul or
            OpCode.Div or
            OpCode.EQ or
            OpCode.LT or
            OpCode.LE or
            OpCode.GT or
            OpCode.GE or
            OpCode.Return => 2,
            OpCode.Move or
            OpCode.LoadL or
            OpCode.LoadI8 or
            OpCode.LoadINeg or
            OpCode.LoadSym or
            OpCode.GetConst or
            OpCode.GetMCnst or
            OpCode.GetIV or
            OpCode.SetIV or
            OpCode.GetIdx0 or
            OpCode.AddI or
            OpCode.SubI or
            OpCode.Array or
            OpCode.Block or
            OpCode.Send0 or
            OpCode.SSend0 or
            OpCode.Hash or
            OpCode.Jmp or
            OpCode.JmpUw => 3,
            OpCode.Enter or
            OpCode.LoadI16 or
            OpCode.GetUpVar or
            OpCode.SetUpVar or
            OpCode.Send or
            OpCode.SSend or
            OpCode.SendB or
            OpCode.SSendB or
            OpCode.AddILV or
            OpCode.SubILV or
            OpCode.Array2 or
            OpCode.ARef or
            OpCode.ASet or
            OpCode.JmpIf or
            OpCode.JmpNot or
            OpCode.JmpNil => 4,
            OpCode.LoadI32 => 6,
            _ => 0
        };

        return length != 0 && pc + length <= sequence.Length;
    }

    static bool TryGetInstructionLengthForControlScan(byte[] sequence, int pc, OpCode opCode, out int length)
    {
        length = opCode switch
        {
            OpCode.Nop or
            OpCode.Call or
            OpCode.KeyEnd or
            OpCode.RetSelf or
            OpCode.RetNil or
            OpCode.RetTrue or
            OpCode.RetFalse or
            OpCode.Stop or
            OpCode.EXT1 or
            OpCode.EXT2 or
            OpCode.EXT3 => 1,
            OpCode.LoadI__1 or
            OpCode.LoadI_0 or
            OpCode.LoadI_1 or
            OpCode.LoadI_2 or
            OpCode.LoadI_3 or
            OpCode.LoadI_4 or
            OpCode.LoadI_5 or
            OpCode.LoadI_6 or
            OpCode.LoadI_7 or
            OpCode.LoadNil or
            OpCode.LoadSelf or
            OpCode.LoadT or
            OpCode.LoadF or
            OpCode.GetIdx or
            OpCode.SetIdx or
            OpCode.Add or
            OpCode.Sub or
            OpCode.Mul or
            OpCode.Div or
            OpCode.EQ or
            OpCode.LT or
            OpCode.LE or
            OpCode.GT or
            OpCode.GE or
            OpCode.Return or
            OpCode.ReturnBlk or
            OpCode.Break or
            OpCode.AryCat or
            OpCode.ArySplat or
            OpCode.Intern or
            OpCode.StrCat or
            OpCode.HashCat or
            OpCode.RangeInc or
            OpCode.RangeExc or
            OpCode.OClass or
            OpCode.SClass or
            OpCode.TClass or
            OpCode.Err or
            OpCode.Except or
            OpCode.RaiseIf or
            OpCode.MatchErr or
            OpCode.Undef => 2,
            OpCode.Move or
            OpCode.LoadL or
            OpCode.LoadI8 or
            OpCode.LoadINeg or
            OpCode.LoadSym or
            OpCode.GetGV or
            OpCode.SetGV or
            OpCode.GetSV or
            OpCode.SetSV or
            OpCode.GetIV or
            OpCode.SetIV or
            OpCode.GetCV or
            OpCode.SetCV or
            OpCode.GetConst or
            OpCode.SetConst or
            OpCode.GetMCnst or
            OpCode.SetMCnst or
            OpCode.GetIdx0 or
            OpCode.AddI or
            OpCode.SubI or
            OpCode.Jmp or
            OpCode.JmpUw or
            OpCode.SSend0 or
            OpCode.Send0 or
            OpCode.BlkCall or
            OpCode.Super or
            OpCode.KeyP or
            OpCode.KArg or
            OpCode.BlkPush or
            OpCode.Array or
            OpCode.AryPush or
            OpCode.Symbol or
            OpCode.String or
            OpCode.Hash or
            OpCode.HashAdd or
            OpCode.Lambda or
            OpCode.Block or
            OpCode.Method or
            OpCode.Class or
            OpCode.Module or
            OpCode.Exec or
            OpCode.Def or
            OpCode.Alias => 3,
            OpCode.Enter or
            OpCode.LoadI16 or
            OpCode.GetUpVar or
            OpCode.SetUpVar or
            OpCode.JmpIf or
            OpCode.JmpNot or
            OpCode.JmpNil or
            OpCode.SSend or
            OpCode.SSendB or
            OpCode.Send or
            OpCode.SendB or
            OpCode.ArgAry or
            OpCode.AddILV or
            OpCode.SubILV or
            OpCode.Array2 or
            OpCode.ARef or
            OpCode.ASet or
            OpCode.APost or
            OpCode.Rescue or
            OpCode.TDef or
            OpCode.SDef or
            OpCode.Debug => 4,
            OpCode.LoadI32 => 6,
            _ => 0
        };

        return length != 0 && pc + length <= sequence.Length;
    }

    static bool TryGetRegisterAccesses(
        byte[] sequence,
        int pc,
        OpCode opCode,
        out RegisterReads reads,
        out int write0,
        out int write1)
    {
        reads = default;
        write0 = -1;
        write1 = -1;

        switch (opCode)
        {
            case OpCode.Nop:
            case OpCode.Enter:
            case OpCode.Jmp:
            // JmpUw (jump-with-unwind, emitted for `break` out of a while loop) is an
            // unconditional jump with no register effects. In a catch-handler-free method
            // (TryCompile requires that) it degenerates to a plain jump.
            case OpCode.JmpUw:
            case OpCode.RetNil:
            case OpCode.RetTrue:
            case OpCode.RetFalse:
                return true;
            case OpCode.Move:
                reads.Add(sequence[pc + 2]);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.LoadL:
            case OpCode.LoadI8:
            case OpCode.LoadINeg:
            case OpCode.LoadSym:
            case OpCode.GetConst:
            case OpCode.LoadI16:
            case OpCode.LoadI32:
            case OpCode.LoadI__1:
            case OpCode.LoadI_0:
            case OpCode.LoadI_1:
            case OpCode.LoadI_2:
            case OpCode.LoadI_3:
            case OpCode.LoadI_4:
            case OpCode.LoadI_5:
            case OpCode.LoadI_6:
            case OpCode.LoadI_7:
            case OpCode.LoadNil:
            case OpCode.LoadSelf:
            case OpCode.LoadT:
            case OpCode.LoadF:
                write0 = sequence[pc + 1];
                return true;
            case OpCode.GetMCnst:
                reads.Add(sequence[pc + 1]);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.GetIV:
                reads.Add(0);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.GetUpVar:
                write0 = sequence[pc + 1];
                return true;
            case OpCode.SetIV:
                reads.Add(0);
                reads.Add(sequence[pc + 1]);
                return true;
            case OpCode.SetUpVar:
                reads.Add(sequence[pc + 1]);
                return true;
            case OpCode.GetIdx:
                reads.Add(sequence[pc + 1]);
                reads.Add(sequence[pc + 1] + 1);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.GetIdx0:
                reads.Add(sequence[pc + 2]);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.SetIdx:
                reads.Add(sequence[pc + 1]);
                reads.Add(sequence[pc + 1] + 1);
                reads.Add(sequence[pc + 1] + 2);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.Send:
            case OpCode.SSend:
            {
                var register = sequence[pc + 1];
                var argumentCount = sequence[pc + 3] & 0xf;
                reads.Add(opCode == OpCode.SSend ? 0 : register);
                for (var i = 0; i < argumentCount; i++)
                {
                    reads.Add(register + 1 + i);
                }
                write0 = register;
                return true;
            }
            case OpCode.SendB:
            case OpCode.SSendB:
            {
                var register = sequence[pc + 1];
                var argumentCount = sequence[pc + 3] & 0xf;
                var keywordArgumentCount = (sequence[pc + 3] >> 4) & 0xf;
                if (argumentCount >= MRubyCallInfo.CallMaxArgs || keywordArgumentCount != 0)
                {
                    return false;
                }

                reads.Add(opCode == OpCode.SSendB ? 0 : register);
                for (var i = 0; i < argumentCount; i++)
                {
                    reads.Add(register + 1 + i);
                }
                reads.Add(register + MRubyCallInfo.CalculateBlockArgumentOffset(argumentCount, keywordArgumentCount));
                write0 = register;
                return true;
            }
            case OpCode.Send0:
            case OpCode.SSend0:
            {
                var register = sequence[pc + 1];
                reads.Add(opCode == OpCode.SSend0 ? 0 : register);
                write0 = register;
                return true;
            }
            case OpCode.Add:
            case OpCode.Sub:
            case OpCode.Mul:
            case OpCode.Div:
            case OpCode.EQ:
            case OpCode.LT:
            case OpCode.LE:
            case OpCode.GT:
            case OpCode.GE:
                reads.Add(sequence[pc + 1]);
                reads.Add(sequence[pc + 1] + 1);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.AddI:
            case OpCode.SubI:
                reads.Add(sequence[pc + 1]);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.AddILV:
            case OpCode.SubILV:
                reads.Add(sequence[pc + 1]);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.Array:
            {
                var start = sequence[pc + 1];
                var count = sequence[pc + 2];
                for (var i = 0; i < count; i++)
                {
                    reads.Add(start + i);
                }
                write0 = start;
                return true;
            }
            case OpCode.Array2:
            {
                var start = sequence[pc + 2];
                var count = sequence[pc + 3];
                for (var i = 0; i < count; i++)
                {
                    reads.Add(start + i);
                }
                write0 = sequence[pc + 1];
                return true;
            }
            case OpCode.ARef:
                reads.Add(sequence[pc + 2]);
                write0 = sequence[pc + 1];
                return true;
            case OpCode.ASet:
                reads.Add(sequence[pc + 2]);
                reads.Add(sequence[pc + 1]);
                return true;
            case OpCode.Block:
                write0 = sequence[pc + 1];
                return true;
            case OpCode.Hash:
            {
                var start = sequence[pc + 1];
                var pairs = sequence[pc + 2];
                for (var i = 0; i < pairs * 2; i++) reads.Add(start + i);
                write0 = start;
                return true;
            }
            case OpCode.Return:
            case OpCode.JmpIf:
            case OpCode.JmpNot:
            case OpCode.JmpNil:
                reads.Add(sequence[pc + 1]);
                return true;
            case OpCode.RetSelf:
                reads.Add(0);
                return true;
            default:
                return false;
        }
    }

    struct RegisterReads
    {
        int count;
        int value0;
        int value1;
        int value2;
        int value3;
        List<int>? overflow;

        public int Count => count;

        public int this[int index]
        {
            get
            {
                return index switch
                {
                    0 => value0,
                    1 => value1,
                    2 => value2,
                    3 => value3,
                    _ => overflow![index - 4],
                };
            }
        }

        public void Add(int register)
        {
            switch (count)
            {
                case 0:
                    value0 = register;
                    break;
                case 1:
                    value1 = register;
                    break;
                case 2:
                    value2 = register;
                    break;
                case 3:
                    value3 = register;
                    break;
                default:
                    overflow ??= new List<int>();
                    overflow.Add(register);
                    break;
            }

            count++;
        }
    }

    sealed class Builder
    {
        readonly List<RubyIRInstruction> instructions;
        readonly List<MRubyValue> literals = new();
        readonly List<Irep> children = new();
        readonly List<Symbol> symbols = new();
        readonly List<int> operands = new();
        readonly List<RubyIRCallSite> callSites = new();
        readonly List<RubyIROperandList> operandLists = new();
        readonly Dictionary<int, int> bytecodePcToInstructionIndex = new();
        readonly List<BranchFixup> branchFixups = new();
        readonly Irep sourceIrep;
        bool[] mergeSlotRegisters;
        readonly ushort[] closureCapturedValueIds;
        ushort[] currentValues;
        readonly int registerCount;
        readonly bool hasBackwardBranch;
        bool hasMergeSlots;
        ushort nextValue;
        int maxScratchCount;
        // Register / bytecode-pc offsets applied transparently by Read/Define/
        // MarkBytecodePc/AddBranch. Zero for the top-level method; set to a
        // callee's reserved region while re-lowering it for inlining so the
        // callee's registers and branch targets live in a disjoint namespace.
        int registerBase;
        int pcBase;
        // Inline-return state: while re-lowering a callee, its Return is emitted
        // as a Move of the result into inlineResultValueId followed by a Jump to
        // inlineContinuationPc (callee-relative), instead of a Return opcode.
        bool inlining;
        ushort inlineResultValueId;
        int inlineContinuationPc;
        int nextInlinePcBase = 1_000_000;

        public bool Inlining => inlining;

        public Builder(Irep irep, bool[] mergeSlotRegisters, bool[] closureCapturedRegisters, bool hasBackwardBranch = false)
        {
            sourceIrep = irep;
            registerCount = irep.RegisterVariableCount;
            instructions = new List<RubyIRInstruction>(irep.Sequence.Length / 2);
            this.mergeSlotRegisters = mergeSlotRegisters;
            this.hasBackwardBranch = hasBackwardBranch;
            closureCapturedValueIds = GetClosureCapturedValueIds(closureCapturedRegisters);
            for (var i = 0; i < mergeSlotRegisters.Length; i++)
            {
                if (mergeSlotRegisters[i])
                {
                    hasMergeSlots = true;
                    break;
                }
            }
            currentValues = new ushort[registerCount];
            for (var i = 0; i < currentValues.Length; i++)
            {
                currentValues[i] = (ushort)i;
            }
            nextValue = (ushort)registerCount;
        }

        public int InstructionCount => instructions.Count;
        public ushort Self => Read(0);

        public void MarkBytecodePc(int pc)
        {
            bytecodePcToInstructionIndex[pcBase + pc] = instructions.Count;
        }

        public void Add(RubyIRInstruction instruction)
        {
            instructions.Add(instruction);
        }

        public void AddBranch(RubyIROpCode opCode, int targetPc, ushort condition = 0)
        {
            var instructionIndex = instructions.Count;
            instructions.Add(new RubyIRInstruction(opCode, src0: condition));
            branchFixups.Add(new BranchFixup(instructionIndex, pcBase + targetPc));
        }

        public ushort Read(int register)
        {
            var index = registerBase + register;
            return IsMergeSlotIndex(index) ? (ushort)index : currentValues[index];
        }

        public void Move(int destinationRegister, int sourceRegister)
        {
            var source = Read(sourceRegister);
            if (IsMergeSlotIndex(registerBase + destinationRegister) || IsMergeSlotValue(source))
            {
                Add(new RubyIRInstruction(RubyIROpCode.Move, dst: Define(destinationRegister), src0: source));
                return;
            }

            currentValues[registerBase + destinationRegister] = source;
        }

        // Emit a method-body terminator. At top level this is a real Return; while
        // inlining a callee it moves the result into the shared result slot and
        // jumps to the post-inline continuation.
        public void EmitReturn(int valueRegister)
        {
            if (inlining)
            {
                Add(new RubyIRInstruction(RubyIROpCode.Move, dst: inlineResultValueId, src0: Read(valueRegister)));
                AddBranch(RubyIROpCode.Jump, inlineContinuationPc);
                return;
            }
            Add(new RubyIRInstruction(RubyIROpCode.Return, src0: Read(valueRegister)));
        }

        public void EmitReturnSelf()
        {
            if (inlining)
            {
                Add(new RubyIRInstruction(RubyIROpCode.Move, dst: inlineResultValueId, src0: Self));
                AddBranch(RubyIROpCode.Jump, inlineContinuationPc);
                return;
            }
            Add(new RubyIRInstruction(RubyIROpCode.ReturnSelf, src0: Self));
        }

        public void EmitReturnLiteral(MRubyValue value)
        {
            if (inlining)
            {
                Add(new RubyIRInstruction(RubyIROpCode.LoadValue, dst: inlineResultValueId, aux: Literal(value)));
                AddBranch(RubyIROpCode.Jump, inlineContinuationPc);
                return;
            }
            Add(new RubyIRInstruction(RubyIROpCode.ReturnValue, aux: Literal(value)));
        }

        // Reserve a disjoint register/value region for a callee and wire its self
        // and arguments to the caller's value ids, then enter inline mode. Returns
        // the result value id the callee's returns write to. Caller must call
        // EndInline afterwards.
        public ushort BeginInline(
            int calleeRegisterCount,
            bool[] calleeMergeSlots,
            ushort receiverValueId,
            ReadOnlySpan<ushort> argValueIds,
            int calleeSequenceLength,
            bool guarded = false)
        {
            var baseIndex = nextValue;
            // A guarded inline writes the result slot from two paths (the spliced
            // body and the cold Send), so it must be a real merge slot with backing
            // storage rather than a single-writer SSA value.
            var newSize = baseIndex + calleeRegisterCount + (guarded ? 1 : 0);
            Array.Resize(ref currentValues, newSize);
            Array.Resize(ref mergeSlotRegisters, newSize);
            for (var r = 0; r < calleeRegisterCount; r++)
            {
                var isMerge = r < calleeMergeSlots.Length && calleeMergeSlots[r];
                mergeSlotRegisters[baseIndex + r] = isMerge;
                hasMergeSlots |= isMerge;
            }
            nextValue = (ushort)(baseIndex + calleeRegisterCount);

            currentValues[baseIndex] = receiverValueId;
            for (var i = 0; i < argValueIds.Length; i++)
            {
                currentValues[baseIndex + 1 + i] = argValueIds[i];
            }

            var resultValueId = nextValue++;
            if (guarded)
            {
                mergeSlotRegisters[resultValueId] = true;
                hasMergeSlots = true;
            }
            registerBase = baseIndex;
            pcBase = nextInlinePcBase;
            nextInlinePcBase += 1_000_000;
            inlineResultValueId = resultValueId;
            inlineContinuationPc = calleeSequenceLength;
            inlining = true;
            return resultValueId;
        }

        // Emit the speculative fence for a guarded inline, in callee pc space
        // (call after BeginInline, before lowering the callee body):
        //
        //   GuardInlineClass receiver -> body(callee pc 0)   ; match jumps to body
        //   Send(original) -> resultSlot                     ; cold path on miss
        //   Jump continuation                                ; rejoin after the body
        //
        // The cold Send carries the guard's expected class / method-cache version
        // on its call site, so the same site both validates the guard and serves
        // as the deopt target.
        public void EmitInlineGuardAndColdSend(
            ushort receiverValueId,
            ReadOnlySpan<ushort> argValueIds,
            Symbol methodId,
            RClass guardClass,
            int guardMethodCacheVersion,
            ulong guardMethodFingerprint,
            ushort resultValueId)
        {
            var argumentStart = operands.Count;
            for (var i = 0; i < argValueIds.Length; i++)
            {
                operands.Add(argValueIds[i]);
            }
            if (argValueIds.Length > maxScratchCount)
            {
                maxScratchCount = argValueIds.Length;
            }

            var coldCallSite = callSites.Count;
            var callSite = new RubyIRCallSite(Symbol(methodId), argumentStart, argValueIds.Length);
            callSite.SetGuardInline(guardClass, guardMethodCacheVersion, guardMethodFingerprint);
            callSites.Add(callSite);

            // Guard: on a match jump to the body (callee pc 0); on a miss fall
            // through to the cold Send. The branch target is resolved by the same
            // fixup machinery as ordinary jumps.
            var guardIndex = instructions.Count;
            instructions.Add(new RubyIRInstruction(
                RubyIROpCode.GuardInlineClass,
                src0: receiverValueId,
                src1: (ushort)coldCallSite));
            branchFixups.Add(new BranchFixup(guardIndex, pcBase + 0));

            instructions.Add(new RubyIRInstruction(
                RubyIROpCode.Send,
                dst: resultValueId,
                src0: receiverValueId,
                aux: coldCallSite));
            AddBranch(RubyIROpCode.Jump, inlineContinuationPc);
        }

        public void EmitInlineGuardDeopt(
            ushort receiverValueId,
            Symbol methodId,
            RClass guardClass,
            int guardMethodCacheVersion,
            ulong guardMethodFingerprint)
        {
            var guardCallSite = callSites.Count;
            var callSite = new RubyIRCallSite(Symbol(methodId), operands.Count, 0);
            callSite.SetGuardInline(guardClass, guardMethodCacheVersion, guardMethodFingerprint);
            callSites.Add(callSite);

            // On a match, jump into the spliced body. On a miss, generated AOT code
            // returns false so the VM reruns the original bytecode from the start.
            var guardIndex = instructions.Count;
            instructions.Add(new RubyIRInstruction(
                RubyIROpCode.GuardInlineClass,
                src0: receiverValueId,
                src1: (ushort)guardCallSite));
            branchFixups.Add(new BranchFixup(guardIndex, pcBase + 0));
        }

        public void EndInline(int callerDestinationRegister, ushort resultValueId)
        {
            registerBase = 0;
            pcBase = 0;
            inlining = false;
            inlineResultValueId = 0;
            inlineContinuationPc = 0;

            if (IsMergeSlotIndex(callerDestinationRegister))
            {
                currentValues[callerDestinationRegister] = (ushort)callerDestinationRegister;
                Add(new RubyIRInstruction(
                    RubyIROpCode.Move,
                    dst: (ushort)callerDestinationRegister,
                    src0: resultValueId));
            }
            else
            {
                currentValues[callerDestinationRegister] = resultValueId;
            }
        }

        public void DefineValue(int register, MRubyValue value)
        {
            Add(new RubyIRInstruction(
                RubyIROpCode.LoadValue,
                dst: Define(register),
                aux: Literal(value)));
        }

        public void DefineSelf(int register)
        {
            Add(new RubyIRInstruction(RubyIROpCode.LoadSelf, dst: Define(register)));
        }

        public void DefineUpVar(int register, int upvarIndex, int upvarDepth)
        {
            Add(new RubyIRInstruction(
                RubyIROpCode.GetUpVar,
                dst: Define(register),
                aux: PackUpVar(upvarIndex, upvarDepth)));
        }

        public void SetUpVar(int register, int upvarIndex, int upvarDepth)
        {
            Add(new RubyIRInstruction(
                RubyIROpCode.SetUpVar,
                src0: Read(register),
                aux: PackUpVar(upvarIndex, upvarDepth)));
        }

        public void DefineSymbolOp(RubyIROpCode opCode, int register, Symbol symbol, ushort src0 = 0)
        {
            Add(new RubyIRInstruction(
                opCode,
                dst: Define(register),
                src0: src0,
                aux: Symbol(symbol)));
        }

        public void DefineOp(RubyIROpCode opCode, int destinationRegister, ushort src0, int aux = 0)
        {
            Add(new RubyIRInstruction(opCode, dst: Define(destinationRegister), src0: src0, aux: aux));
        }

        public void DefineBinaryOp(
            RubyIROpCode opCode,
            int destinationRegister,
            int leftRegister,
            int rightRegister)
        {
            var left = Read(leftRegister);
            var right = Read(rightRegister);
            Add(new RubyIRInstruction(opCode, dst: Define(destinationRegister), src0: left, src1: right));
        }

        public void DefineTernaryOp(
            RubyIROpCode opCode,
            int destinationRegister,
            int firstRegister,
            int secondRegister,
            int thirdRegister)
        {
            var first = Read(firstRegister);
            var second = Read(secondRegister);
            var third = Read(thirdRegister);
            Add(new RubyIRInstruction(
                opCode,
                dst: Define(destinationRegister),
                src0: first,
                src1: second,
                src2: third));
        }

        public void DefineImmediateOp(
            RubyIROpCode opCode,
            int destinationRegister,
            int receiverRegister,
            MRubyValue value)
        {
            var receiver = Read(receiverRegister);
            Add(new RubyIRInstruction(
                opCode,
                dst: Define(destinationRegister),
                src0: receiver,
                aux: Literal(value)));
        }

        public void DefineSend(RubyIROpCode opCode, int register, int argc, Symbol methodId)
        {
            var receiver = opCode == RubyIROpCode.SendSelf ? Self : Read(register);
            var argumentStart = operands.Count;
            for (var i = 0; i < argc; i++)
            {
                operands.Add(Read(register + 1 + i));
            }
            if (argc > maxScratchCount)
            {
                maxScratchCount = argc;
            }

            var callSite = callSites.Count;
            callSites.Add(new RubyIRCallSite(Symbol(methodId), argumentStart, argc));
            // `.new` is emitted as VirtualNew so escape analysis / scalar replacement can recognize
            // the allocation (a plain Send always heap-allocates via state.Send). VirtualNew that
            // doesn't end up scalar-replaced still emits a real allocation, so this is always safe.
            Add(new RubyIRInstruction(
                methodId == Names.New ? RubyIROpCode.VirtualNew : opCode,
                dst: Define(register),
                src0: receiver,
                aux: callSite));
        }

        public void DefineSendBlock(
            RubyIROpCode opCode,
            int register,
            int argc,
            int blockRegister,
            Symbol methodId)
        {
            var receiver = opCode == RubyIROpCode.SendSelfBlock ? Self : Read(register);
            var block = Read(blockRegister);
            var argumentStart = operands.Count;
            for (var i = 0; i < argc; i++)
            {
                operands.Add(Read(register + 1 + i));
            }
            if (argc > maxScratchCount)
            {
                maxScratchCount = argc;
            }

            var callSite = callSites.Count;
            callSites.Add(new RubyIRCallSite(Symbol(methodId), argumentStart, argc));
            Add(new RubyIRInstruction(
                opCode,
                dst: Define(register),
                src0: receiver,
                src1: block,
                aux: callSite));
        }

        public void DefineSendBlockDescriptor(
            RubyIROpCode opCode,
            int register,
            int argc,
            Irep child,
            Symbol methodId)
        {
            var receiver = opCode == RubyIROpCode.SendSelfBlockDescriptor ? Self : Read(register);
            var argumentStart = operands.Count;
            for (var i = 0; i < argc; i++)
            {
                operands.Add(Read(register + 1 + i));
            }
            if (argc > maxScratchCount)
            {
                maxScratchCount = argc;
            }

            var callSite = callSites.Count;
            callSites.Add(new RubyIRCallSite(Symbol(methodId), argumentStart, argc));
            Add(new RubyIRInstruction(
                opCode,
                dst: Define(register),
                src0: receiver,
                src1: (ushort)Child(child),
                aux: callSite));
        }

        public void DefineBlock(int register, Irep child)
        {
            Add(new RubyIRInstruction(
                RubyIROpCode.LoadBlock,
                dst: Define(register),
                aux: Child(child)));
        }

        public void DefineArray(int destinationRegister, int firstRegister, int count)
        {
            var operandStart = operands.Count;
            for (var i = 0; i < count; i++)
            {
                operands.Add(Read(firstRegister + i));
            }
            if (count > maxScratchCount)
            {
                maxScratchCount = count;
            }

            var operandList = operandLists.Count;
            operandLists.Add(new RubyIROperandList(operandStart, count));
            Add(new RubyIRInstruction(
                RubyIROpCode.NewArray,
                dst: Define(destinationRegister),
                aux: operandList));
        }

        // Hash literal `{k0 => v0, ...}`: pairCount pairs, key/value value-ids interleaved in the
        // operand list (k0,v0,k1,v1,...) read from destinationRegister..+2*pairCount-1.
        public void DefineHash(int destinationRegister, int pairCount)
        {
            var count = pairCount * 2;
            var operandStart = operands.Count;
            for (var i = 0; i < count; i++)
            {
                operands.Add(Read(destinationRegister + i));
            }
            if (count > maxScratchCount)
            {
                maxScratchCount = count;
            }

            var operandList = operandLists.Count;
            operandLists.Add(new RubyIROperandList(operandStart, count));
            Add(new RubyIRInstruction(
                RubyIROpCode.NewHash,
                dst: Define(destinationRegister),
                aux: operandList));
        }

        public bool TryBuild(out RubyIRMethod ir, out RubyIRBuildFailure failure)
        {
            failure = RubyIRBuildFailure.None;
            ir = null!;
            foreach (var fixup in branchFixups)
            {
                if (!bytecodePcToInstructionIndex.TryGetValue(fixup.TargetPc, out var targetInstructionIndex))
                {
                    failure = new RubyIRBuildFailure(null, fixup.TargetPc, "branch target");
                    return false;
                }

                var instruction = instructions[fixup.InstructionIndex];
                instructions[fixup.InstructionIndex] = new RubyIRInstruction(
                    instruction.OpCode,
                    instruction.Dst,
                    instruction.Src0,
                    instruction.Src1,
                    instruction.Src2,
                    targetInstructionIndex);
            }

            var valueCount = nextValue;
            if (maxScratchCount > 0 && valueCount == registerCount)
            {
                valueCount++;
            }

            ir = new RubyIRMethod(
                instructions.ToArray(),
                valueCount,
                literals.ToArray(),
                children.ToArray(),
                symbols.ToArray(),
                operands.ToArray(),
                callSites.ToArray(),
                operandLists.ToArray(),
                closureCapturedValueIds,
                hasBackwardBranch,
                sourceIrep: sourceIrep,
                sourceBytecodePcs: BuildSourceBytecodePcs(instructions.Count));
            return true;
        }

        // Per-instruction source bytecode pc, derived by inverting the
        // pc->first-instruction map: every instruction is attributed to the
        // bytecode op whose mark most recently preceded it. Lets the specializer
        // recover a send's bytecode pc from the instruction index the profile
        // observed it at, to key an inline plan.
        int[] BuildSourceBytecodePcs(int instructionCount)
        {
            var sourcePcs = new int[instructionCount];
            Array.Fill(sourcePcs, -1);

            var starts = new (int Index, int Pc)[bytecodePcToInstructionIndex.Count];
            var n = 0;
            foreach (var entry in bytecodePcToInstructionIndex)
            {
                starts[n++] = (entry.Value, entry.Key);
            }
            Array.Sort(starts, static (a, b) => a.Index.CompareTo(b.Index));

            for (var e = 0; e < starts.Length; e++)
            {
                var start = starts[e].Index;
                if (start < 0)
                {
                    continue;
                }

                var end = e + 1 < starts.Length ? starts[e + 1].Index : instructionCount;
                for (var k = start; k < end && k < instructionCount; k++)
                {
                    sourcePcs[k] = starts[e].Pc;
                }
            }

            return sourcePcs;
        }

        public int Literal(MRubyValue value)
        {
            literals.Add(value);
            return literals.Count - 1;
        }

        public int Symbol(Symbol symbol)
        {
            symbols.Add(symbol);
            return symbols.Count - 1;
        }

        public int Child(Irep child)
        {
            children.Add(child);
            return children.Count - 1;
        }

        static int PackUpVar(int upvarIndex, int upvarDepth) =>
            (upvarIndex << 8) | upvarDepth;

        static ushort[] GetClosureCapturedValueIds(bool[] closureCapturedRegisters)
        {
            var count = 0;
            for (var i = 0; i < closureCapturedRegisters.Length; i++)
            {
                if (closureCapturedRegisters[i])
                {
                    count++;
                }
            }

            var valueIds = new ushort[count];
            var next = 0;
            for (var i = 0; i < closureCapturedRegisters.Length; i++)
            {
                if (closureCapturedRegisters[i])
                {
                    valueIds[next++] = (ushort)i;
                }
            }
            return valueIds;
        }

        ushort Define(int register)
        {
            var index = registerBase + register;
            if (IsMergeSlotIndex(index))
            {
                currentValues[index] = (ushort)index;
                return (ushort)index;
            }

            var value = nextValue++;
            currentValues[index] = value;
            return value;
        }

        bool IsMergeSlotIndex(int index) =>
            (uint)index < (uint)mergeSlotRegisters.Length && mergeSlotRegisters[index];

        bool IsMergeSlotValue(ushort valueId) =>
            valueId < mergeSlotRegisters.Length && mergeSlotRegisters[valueId];

        readonly record struct BranchFixup(int InstructionIndex, int TargetPc);
    }
}
