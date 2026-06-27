using System;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif

namespace ChibiRuby.JetPack;

sealed class RubyIRMethod(
    RubyIRInstruction[] instructions,
    int valueCount,
    MRubyValue[] literalPool,
    Irep[] childPool,
    Symbol[] symbolPool,
    int[] operandPool,
    RubyIRCallSite[] callSites,
    RubyIROperandList[] operandLists,
    ushort[] closureCapturedValueIds,
    bool hasBackwardBranch = false,
    Irep? sourceIrep = null,
    int[]? sourceBytecodePcs = null)
{
    public ReadOnlySpan<RubyIRInstruction> Instructions => instructions;
    public int ValueCount => valueCount;

    // True when the method contains a backward branch (a `while`/`until` loop back-edge). Such
    // methods are compiled by the AOT codegen in a fully-boxed, ForceSend (deopt-free) mode with
    // SSA / unboxing / scalar-replacement disabled — a loop body must never re-execute a partial
    // iteration, mirroring the block-body contract.
    public bool HasBackwardBranch => hasBackwardBranch;

    // The method this (generic) IR was built from, retained so the
    // specializer can re-lower with an inline plan. Null on specialized variants.
    public Irep? SourceIrep => sourceIrep;

    // Bytecode pc of the op that produced instruction `index`, or -1. Used to key
    // an inline plan (which TryCompile resolves by bytecode pc) from a monomorphic
    // send the profile observed at a given instruction index.
    public int SourceBytecodePc(int index) =>
        sourceBytecodePcs is not null && (uint)index < (uint)sourceBytecodePcs.Length
            ? sourceBytecodePcs[index]
            : -1;
    public MRubyValue GetLiteral(int literalIndex) =>
        (uint)literalIndex < (uint)literalPool.Length
            ? literalPool[literalIndex]
            : default;

    // Symbol pool access (e.g. ivar name for GetInstanceVariable, whose Aux is the
    // symbol index). Used by the AOT codegen.
    public Symbol GetSymbol(int symbolIndex) =>
        (uint)symbolIndex < (uint)symbolPool.Length
            ? symbolPool[symbolIndex]
            : default;

    // Call-site access for the AOT codegen (a Send's Aux indexes callSites; its args
    // are value ids at operandPool[ArgumentStart + i]).
    public Symbol GetCallSiteSymbol(int callSiteIndex) =>
        symbolPool[callSites[callSiteIndex].SymbolIndex];
    public int GetCallSiteArgumentCount(int callSiteIndex) =>
        callSites[callSiteIndex].ArgumentCount;
    public int GetCallSiteArgumentValueId(int callSiteIndex, int argumentIndex) =>
        operandPool[callSites[callSiteIndex].ArgumentStart + argumentIndex];

    // Child irep access for block inlining: a SendBlockDescriptor's Src1 is the index into
    // the child pool of the block body's irep.
    public Irep GetChildIrep(int index) => childPool[index];

    // Registers captured by descendant blocks (their value-ids, since captured registers are
    // merge slots whose value-id equals the register at registerBase 0). The AOT codegen
    // forces these boxed so they can be passed to an inlined block's __block form by ref.
    public ReadOnlySpan<ushort> ClosureCapturedValueIds => closureCapturedValueIds;

    // Operand-list access for the AOT codegen (a NewArray's Aux indexes operandLists;
    // its elements are value ids at operandPool[OperandStart + i]).
    public int GetOperandListCount(int operandListIndex) =>
        operandLists[operandListIndex].OperandCount;
    public int GetOperandListValueId(int operandListIndex, int elementIndex) =>
        operandPool[operandLists[operandListIndex].OperandStart + elementIndex];

    public bool TryGetGuardInline(int callSiteIndex, out ulong calleeFingerprint)
    {
        if ((uint)callSiteIndex < (uint)callSites.Length)
        {
            return callSites[callSiteIndex].TryGetGuardInline(out calleeFingerprint);
        }

        calleeFingerprint = 0;
        return false;
    }

    public RubyIRMethod CreateVariant(
        RubyIRInstruction[] loweredInstructions,
        RubyIRCallSite[]? loweredCallSites = null,
        Irep? loweredSourceIrep = null,
        int[]? loweredSourceBytecodePcs = null) =>
        new(
            loweredInstructions,
            valueCount,
            literalPool,
            childPool,
            symbolPool,
            operandPool,
            loweredCallSites ?? callSites,
            operandLists,
            closureCapturedValueIds,
            hasBackwardBranch,
            // The escape-analysis rewrite produces the generic IR as a
            // 1:1 variant (same instruction count/order), so the source map still
            // aligns — carry it through so later passes stay aligned.
            loweredSourceIrep ?? (loweredInstructions.Length == instructions.Length ? sourceIrep : null),
            loweredSourceBytecodePcs ?? (loweredInstructions.Length == instructions.Length ? sourceBytecodePcs : null));

    internal int OperandPoolValue(int index) => operandPool[index];
    internal int[] CloneOperandPool() => (int[])operandPool.Clone();
    internal int CallSiteArgumentStart(int callSiteIndex) => callSites[callSiteIndex].ArgumentStart;
    internal int OperandListStart(int operandListIndex) => operandLists[operandListIndex].OperandStart;

    // Same pools/metadata as this IR, but with value-remapped instructions /
    // operand pool / captured ids and an expanded value count. Renumbering is 1:1 over
    // instructions (same count/order), so the source map stays aligned and is carried.
    internal RubyIRMethod CreateSsaVariant(
        RubyIRInstruction[] remappedInstructions,
        int[] remappedOperandPool,
        ushort[] remappedClosureCapturedValueIds,
        int newValueCount) =>
        new(
            remappedInstructions,
            newValueCount,
            literalPool,
            childPool,
            symbolPool,
            remappedOperandPool,
            callSites,
            operandLists,
            remappedClosureCapturedValueIds,
            hasBackwardBranch,
            sourceIrep,
            sourceBytecodePcs);

    public int[] CountValueUses(ReadOnlySpan<RubyIRInstruction> loweredInstructions)
    {
        var useCounts = new int[valueCount];
        foreach (var instruction in loweredInstructions)
        {
            CountUse(useCounts, instruction.Src0);
            // GuardInlineClass.Src1 is a call-site index, not a value id.
            // SendBlockDescriptor.Src1 is a child-pool index, not a value id.
            if (instruction.OpCode is not (
                RubyIROpCode.GuardInlineClass or
                RubyIROpCode.SendBlockDescriptor or
                RubyIROpCode.SendSelfBlockDescriptor))
            {
                CountUse(useCounts, instruction.Src1);
                CountUse(useCounts, instruction.Src2);
            }

            switch (instruction.OpCode)
            {
                case RubyIROpCode.LoadBlock:
                    foreach (var t in closureCapturedValueIds)
                    {
                        CountUse(useCounts, t);
                    }
                    break;
                case RubyIROpCode.SendBlockDescriptor:
                case RubyIROpCode.SendSelfBlockDescriptor:
                {
                    foreach (var t in closureCapturedValueIds)
                    {
                        CountUse(useCounts, t);
                    }
                    var callSite = callSites[instruction.Aux];
                    for (var j = 0; j < callSite.ArgumentCount; j++)
                    {
                        CountUse(useCounts, operandPool[callSite.ArgumentStart + j]);
                    }
                    break;
                }
                case RubyIROpCode.Send:
                case RubyIROpCode.SendSelf:
                case RubyIROpCode.SendBlock:
                case RubyIROpCode.SendSelfBlock:
                case RubyIROpCode.PureUnarySend:
                case RubyIROpCode.GuardClass:
                case RubyIROpCode.GuardMethod:
                case RubyIROpCode.InlineBody:
                case RubyIROpCode.VirtualNew:
                {
                    var callSite = callSites[instruction.Aux];
                    for (var j = 0; j < callSite.ArgumentCount; j++)
                    {
                        CountUse(useCounts, operandPool[callSite.ArgumentStart + j]);
                    }
                    break;
                }
                case RubyIROpCode.NewArray:
                case RubyIROpCode.NewArray2:
                case RubyIROpCode.NewHash:
                {
                    var operandList = operandLists[instruction.Aux];
                    for (var j = 0; j < operandList.OperandCount; j++)
                    {
                        CountUse(useCounts, operandPool[operandList.OperandStart + j]);
                    }
                    break;
                }
            }
        }

        return useCounts;
    }

    static void CountUse(int[] useCounts, int valueId)
    {
        if ((uint)valueId < (uint)useCounts.Length)
        {
            useCounts[valueId]++;
        }
    }
}
