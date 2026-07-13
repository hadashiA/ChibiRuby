using System;
using System.Collections.Generic;
using ChibiRuby;

namespace ChibiRuby.JetPack;

// SSA-grade live-range splitting, implemented as a value-id renumbering pass (on by default;
// AOT_NOSSA=1 disables).
//
// Why: the lowerer already gives each definition a fresh value-id EXCEPT for "merge-slot"
// registers, which reuse their register-index id across the whole method so the AOT
// codegen's shared default-init local merges branch joins (an implicit phi). That
// per-register reuse conflates a register's DISJOINT live ranges under one id — the
// classic workhorse-register problem (one id holding a float in one range and an object
// in another), which blocks ComputeUnboxing from unboxing the float-only range.
//
// What: this pass splits each reused id's live ranges into the minimal set of ids that
// still merge correctly. It does NOT build explicit SSA/phi — instead it computes, by
// reaching-definitions, which definitions of a value must share storage (any two defs
// that both reach one use), unions them, and gives each resulting congruence class its
// own id. For this loop-free, forward-branch-only CFG that partition is exactly what SSA
// + phi-web coalescing would produce, and the codegen's existing shared local is its
// out-of-SSA form. Downstream analysis (ComputeUnboxing) and emission are unchanged —
// they just see finer ids, so per-range type inference now succeeds.
//
// Correctness is constructive: the lowerer's "one shared local per merge register" is
// already correct; this pass only ever SPLITS that into finer shared locals, never merges
// distinct values. Reaching-defs guarantees every def that can reach a common use stays
// in one class, so no live value loses its merge. Any structural anomaly (unsupported
// opcode, backward branch, value-count overflow) aborts to the original IR.
static class RubyIRSsaRenumber
{
    public static RubyIRMethod Run(RubyIRMethod exe, int argCount)
    {
        try
        {
            return TryRun(exe, argCount) ?? exe;
        }
        catch
        {
            return exe;
        }
    }

    static RubyIRMethod? TryRun(RubyIRMethod exe, int argCount)
    {
        var ins = exe.Instructions;
        var n = ins.Length;
        var oldValueCount = exe.ValueCount;
        if (n == 0 || oldValueCount == 0) return null;

        // "kept" ids are never split and keep a stable mapping: params (self v0..vArgCount)
        // because they are method parameters in the emitted signature, and captured ids
        // because each is a single ref-cell identity passed by ref to inlined blocks.
        var isKept = new bool[oldValueCount];
        for (var v = 0; v <= argCount && v < oldValueCount; v++) isKept[v] = true;
        foreach (var captured in exe.ClosureCapturedValueIds)
            if (captured < oldValueCount) isKept[captured] = true;

        // Def model: classify every opcode (an unsupported one aborts the pass).
        var defOldId = new int[n]; // old id defined by instruction i, or -1 if it defines nothing
        var dsts = new ushort[n];  // materialized so local functions can read it (ins is a ref-struct span)
        for (var i = 0; i < n; i++)
        {
            if (!TryDefinesValue(ins[i].OpCode, out var defines)) return null;
            dsts[i] = ins[i].Dst;
            defOldId[i] = defines && ins[i].Dst < oldValueCount ? ins[i].Dst : -1;
        }

        // Dense slot per splittable old-id (everything not kept). Each splittable id gets a
        // token space: its real defs use their instruction index as a token (0..n-1), and an
        // "entry" pseudo-def token (n + slot) models the default/uninitialized value reaching
        // a read-before-def — so out-of-SSA default-init handles that path with no special case.
        var splitSlot = new int[oldValueCount];
        Array.Fill(splitSlot, -1);
        var numSplit = 0;
        for (var v = 0; v < oldValueCount; v++)
            if (!isKept[v]) splitSlot[v] = numSplit++;

        var defsByOldId = new List<int>[numSplit];
        for (var s = 0; s < numSplit; s++) defsByOldId[s] = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var d = defOldId[i];
            if (d >= 0 && splitSlot[d] >= 0) defsByOldId[splitSlot[d]].Add(i);
        }

        var numTokens = n + numSplit;
        var words = (numTokens + 63) >> 6;

        // --- CFG (forward branches only -> every predecessor has a smaller index, so a single
        // in-order pass computes the reaching-defs fixpoint). ---
        var preds = new List<int>[n];
        for (var i = 0; i < n; i++) preds[i] = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var op = ins[i].OpCode;
            switch (op)
            {
                case RubyIROpCode.Return:
                case RubyIROpCode.ReturnSelf:
                case RubyIROpCode.ReturnValue:
                    break; // terminal: no successor
                case RubyIROpCode.Jump:
                    if (!AddBranchPred(preds, ins[i].Aux, i, n)) return null;
                    break;
                case RubyIROpCode.JumpIfTruthy:
                case RubyIROpCode.JumpIfFalsy:
                case RubyIROpCode.JumpIfNil:
                case RubyIROpCode.GuardInlineClass:
                    if (!AddBranchPred(preds, ins[i].Aux, i, n)) return null;
                    if (i + 1 < n) preds[i + 1].Add(i);
                    break;
                default:
                    if (i + 1 < n) preds[i + 1].Add(i);
                    break;
            }
        }

        // entry seed: all entry pseudo-defs reach the method start (and any unreachable block,
        // so its emitted-but-never-run code still maps to valid ids).
        var entrySeed = new ulong[words];
        for (var s = 0; s < numSplit; s++) SetBit(entrySeed, n + s);

        // Reaching-defs fixpoint. Forward-only methods converge in a single in-order pass (every
        // predecessor index is smaller); a loop back-edge (pred index > node index) feeds the loop
        // header, so iterate until stable. Reaching sets only grow (monotone), so this terminates.
        var inSets = new ulong[n][];
        for (var i = 0; i < n; i++) inSets[i] = new ulong[words];
        for (var i = 0; i < n; i++)
            if (i == 0 || preds[i].Count == 0) Array.Copy(entrySeed, inSets[i], words);
        var outBuf = new ulong[words];
        var inChanged = true;
        while (inChanged)
        {
            inChanged = false;
            for (var i = 0; i < n; i++)
            {
                if (i == 0 || preds[i].Count == 0) continue; // fixed entry seed
                var inI = inSets[i];
                foreach (var p in preds[i])
                {
                    ComputeOut(inSets[p], defOldId[p], splitSlot, defsByOldId, p, n, words, outBuf);
                    if (OrChanged(inI, outBuf)) inChanged = true;
                }
            }
        }

        // --- union-find over tokens: a use unions all defs of its value that reach it ---
        var uf = new int[numTokens];
        for (var t = 0; t < numTokens; t++) uf[t] = t;

        for (var i = 0; i < n; i++)
        {
            foreach (var useId in EnumerateUses(exe, ins[i]))
            {
                if (useId < 0 || useId >= oldValueCount) continue;
                var slot = splitSlot[useId];
                if (slot < 0) continue; // kept id: not split
                UnionReaching(uf, inSets[i], useId, slot, defsByOldId[slot], n);
            }
        }


        // --- assign new ids ---
        // Kept ids (params + captured) keep their ORIGINAL id: params are the emitted method
        // parameters, and the block-inline upvar mechanism binds a captured method-local by
        // `ref v<register>` where the captured value-id == its register number — renumbering a
        // captured id would dangle that ref. Congruence classes get fresh ids that skip the
        // reserved captured numbers so they never collide.
        var keptNewId = new int[oldValueCount];
        for (var v = 0; v < oldValueCount; v++) keptNewId[v] = isKept[v] ? v : -1;

        var capturedReserved = new HashSet<int>();
        var maxKept = argCount;
        foreach (var captured in exe.ClosureCapturedValueIds)
        {
            if (captured >= oldValueCount) continue;
            if (captured > argCount) capturedReserved.Add(captured);
            if (captured > maxKept) maxKept = captured;
        }

        var classNewId = new int[numTokens];
        Array.Fill(classNewId, -1);
        var next = argCount + 1;

        int AllocId()
        {
            while (capturedReserved.Contains(next)) next++;
            return next++;
        }

        int RootId(int token)
        {
            var root = Find(uf, token);
            if (classNewId[root] < 0) classNewId[root] = AllocId();
            return classNewId[root];
        }

        int MapUse(int instr, int useId)
        {
            if (useId < 0 || useId >= oldValueCount) return useId;
            var slot = splitSlot[useId];
            if (slot < 0) return keptNewId[useId];
            var rep = ReachingRep(inSets[instr], slot, defsByOldId[slot], n);
            if (rep < 0) throw new InvalidOperationException("ssa: use with no reaching def");
            return RootId(rep);
        }

        int MapDef(int instr)
        {
            var d = defOldId[instr];
            if (d < 0) return dsts[instr]; // non-defining: Dst is unused (0) -> kept identity below
            if (splitSlot[d] < 0) return keptNewId[d];
            return RootId(instr); // real def token == instruction index
        }

        // --- rewrite (single deterministic pass; RootId assigns ids in first-seen order) ---
        var newOperandPool = exe.CloneOperandPool();
        var newInstructions = new RubyIRInstruction[n];
        for (var i = 0; i < n; i++)
        {
            var instruction = ins[i];
            var op = instruction.OpCode;

            var newDst = (ushort)MapDef(i);
            var newSrc0 = instruction.Src0;
            var newSrc1 = instruction.Src1;
            var newSrc2 = instruction.Src2;

            // Src0 is always a value operand (branch condition / receiver / store value / etc.;
            // for ops that leave it unused it is 0 == self, which maps to itself).
            newSrc0 = (ushort)MapUse(i, instruction.Src0);

            // Src1/Src2 are value operands EXCEPT where they encode an index, mirroring
            // RubyIRMethod.CountValueUses.
            var src1IsIndex = op is RubyIROpCode.GuardInlineClass
                or RubyIROpCode.SendBlockDescriptor or RubyIROpCode.SendSelfBlockDescriptor;
            if (!src1IsIndex)
            {
                newSrc1 = (ushort)MapUse(i, instruction.Src1);
                newSrc2 = (ushort)MapUse(i, instruction.Src2);
            }

            // Call-site arguments and array operand lists live in the operand pool.
            switch (op)
            {
                case RubyIROpCode.Send:
                case RubyIROpCode.SendSelf:
                case RubyIROpCode.SendBlock:
                case RubyIROpCode.SendSelfBlock:
                case RubyIROpCode.SendBlockDescriptor:
                case RubyIROpCode.SendSelfBlockDescriptor:
                case RubyIROpCode.PureUnarySend:
                case RubyIROpCode.VirtualNew:
                {
                    var start = exe.CallSiteArgumentStart(instruction.Aux);
                    var argc = exe.GetCallSiteArgumentCount(instruction.Aux);
                    for (var a = 0; a < argc; a++)
                        newOperandPool[start + a] = MapUse(i, newOperandPool[start + a]);
                    break;
                }
                case RubyIROpCode.NewArray:
                case RubyIROpCode.NewArray2:
                case RubyIROpCode.NewHash:
                {
                    var start = exe.OperandListStart(instruction.Aux);
                    var count = exe.GetOperandListCount(instruction.Aux);
                    for (var a = 0; a < count; a++)
                        newOperandPool[start + a] = MapUse(i, newOperandPool[start + a]);
                    break;
                }
            }

            newInstructions[i] = new RubyIRInstruction(op, newDst, newSrc0, newSrc1, newSrc2, instruction.Aux);
        }

        // Captured ids are uses for LoadBlock / block sends but are read from this list, not from
        // instruction fields — remap the list itself (each captured id is kept => stable).
        var oldCaptured = exe.ClosureCapturedValueIds;
        var newCaptured = new ushort[oldCaptured.Length];
        for (var j = 0; j < oldCaptured.Length; j++)
        {
            var c = oldCaptured[j];
            newCaptured[j] = c < oldValueCount && keptNewId[c] >= 0 ? (ushort)keptNewId[c] : c;
        }

        var newValueCount = Math.Max(next, maxKept + 1);
        if (newValueCount > ushort.MaxValue) return null; // value ids are ushort

        return exe.CreateSsaVariant(newInstructions, newOperandPool, newCaptured, newValueCount);
    }

    // out[i] = (in[i] \ kill[i]) | gen[i], where a def of splittable id M kills M's entry token
    // and all of M's def tokens, and generates this instruction's token.
    static void ComputeOut(ulong[] inSet, int defId, int[] splitSlot, List<int>[] defsByOldId, int instr, int n, int words, ulong[] outBuf)
    {
        Array.Copy(inSet, outBuf, words);
        if (defId < 0) return;
        var slot = splitSlot[defId];
        if (slot < 0) return; // kept id def: no token, nothing to kill/gen
        ClearBit(outBuf, n + slot); // kill entry token
        foreach (var d in defsByOldId[slot]) ClearBit(outBuf, d); // kill all defs of M
        SetBit(outBuf, instr); // gen this def
    }

    // A representative reaching-def token for a splittable id at a program point: its entry
    // pseudo-def if it reaches (default/uninitialized), else any reaching real def (all of which
    // are in one union-find class), or -1 if none reach (only on truly unreachable code).
    static int ReachingRep(ulong[] inSet, int slot, List<int> defs, int n)
    {
        var entryToken = n + slot;
        if (TestBit(inSet, entryToken)) return entryToken;
        foreach (var d in defs)
            if (TestBit(inSet, d)) return d;
        return -1;
    }

    static void UnionReaching(int[] uf, ulong[] inSet, int useId, int slot, List<int> defs, int n)
    {
        var first = -1;
        var entryToken = n + slot;
        if (TestBit(inSet, entryToken)) first = entryToken;
        foreach (var d in defs)
        {
            if (!TestBit(inSet, d)) continue;
            if (first < 0) first = d;
            else Union(uf, first, d);
        }
    }

    static bool AddBranchPred(List<int>[] preds, int target, int from, int n)
    {
        if (target < 0) return false;
        if (target >= n) return true;          // jump to the trailing end label: no successor instruction
        preds[target].Add(from);               // backward targets (loops) are allowed: the reaching-
        return true;                            // defs solver below iterates to a fixpoint over them.
    }

    // Every opcode must be classified. Common value-producing and side-effecting ops are
    // handled precisely; ops whose def/use shape this pass does not model (guards other than
    // GuardInlineClass, InlineBody, TypeSwitch, MaterializeObject) return false to abort the
    // pass for that method (safe fallback to no renumbering).
    static bool TryDefinesValue(RubyIROpCode op, out bool defines)
    {
        switch (op)
        {
            // value-producing
            case RubyIROpCode.Move:
            case RubyIROpCode.LoadValue:
            case RubyIROpCode.LoadSelf:
            case RubyIROpCode.LoadBlock:
            case RubyIROpCode.GetUpVar:
            case RubyIROpCode.GetConstant:
            case RubyIROpCode.GetModuleConstant:
            case RubyIROpCode.GetInstanceVariable:
            case RubyIROpCode.VirtualGetField:
            case RubyIROpCode.VirtualNew:
            case RubyIROpCode.GetIndex:
            case RubyIROpCode.GetIndex0:
            case RubyIROpCode.SetIndex: // a[b] = c lowers via Define (returns the assigned value)
            case RubyIROpCode.ArrayRef:
            case RubyIROpCode.NewArray:
            case RubyIROpCode.NewArray2:
            case RubyIROpCode.NewHash:
            case RubyIROpCode.Send:
            case RubyIROpCode.SendSelf:
            case RubyIROpCode.SendBlock:
            case RubyIROpCode.SendSelfBlock:
            case RubyIROpCode.SendBlockDescriptor:
            case RubyIROpCode.SendSelfBlockDescriptor:
            case RubyIROpCode.PureUnarySend:
            case RubyIROpCode.Add:
            case RubyIROpCode.AddImmediate:
            case RubyIROpCode.Sub:
            case RubyIROpCode.SubImmediate:
            case RubyIROpCode.AddImmediateFixnum:
            case RubyIROpCode.SubImmediateFixnum:
            case RubyIROpCode.AddImmediateFloat:
            case RubyIROpCode.SubImmediateFloat:
            case RubyIROpCode.Mul:
            case RubyIROpCode.Div:
            case RubyIROpCode.MulAdd:
            case RubyIROpCode.MulSub:
            case RubyIROpCode.SubMul:
            case RubyIROpCode.AddFixnum:
            case RubyIROpCode.SubFixnum:
            case RubyIROpCode.MulFixnum:
            case RubyIROpCode.DivFixnum:
            case RubyIROpCode.AddFloat:
            case RubyIROpCode.SubFloat:
            case RubyIROpCode.MulFloat:
            case RubyIROpCode.DivFloat:
            case RubyIROpCode.MulAddFloat:
            case RubyIROpCode.MulSubFloat:
            case RubyIROpCode.SubMulFloat:
            case RubyIROpCode.Eq:
            case RubyIROpCode.Lt:
            case RubyIROpCode.Le:
            case RubyIROpCode.Gt:
            case RubyIROpCode.Ge:
            case RubyIROpCode.LtFixnum:
            case RubyIROpCode.LeFixnum:
            case RubyIROpCode.GtFixnum:
            case RubyIROpCode.GeFixnum:
            case RubyIROpCode.LtFloat:
            case RubyIROpCode.LeFloat:
            case RubyIROpCode.GtFloat:
            case RubyIROpCode.GeFloat:
                defines = true;
                return true;

            // no value def
            case RubyIROpCode.CheckArity:
            case RubyIROpCode.SetUpVar:
            case RubyIROpCode.SetInstanceVariable:
            case RubyIROpCode.VirtualSetField:
            case RubyIROpCode.ArraySet:
            case RubyIROpCode.Jump:
            case RubyIROpCode.JumpIfTruthy:
            case RubyIROpCode.JumpIfFalsy:
            case RubyIROpCode.JumpIfNil:
            case RubyIROpCode.GuardInlineClass:
            case RubyIROpCode.Return:
            case RubyIROpCode.ReturnSelf:
            case RubyIROpCode.ReturnValue:
                defines = false;
                return true;

            // shapes this pass does not model -> abort
            default:
                defines = false;
                return false;
        }
    }

    // Use operands of an instruction, matching RubyIRMethod.CountValueUses exactly.
    // Captured ids (LoadBlock / block descriptors) are intentionally NOT yielded here — they
    // are remapped via the captured-id list, not via instruction fields.
    static IEnumerable<int> EnumerateUses(RubyIRMethod exe, RubyIRInstruction instruction)
    {
        yield return instruction.Src0;
        if (instruction.OpCode is not (
                RubyIROpCode.GuardInlineClass or
                RubyIROpCode.SendBlockDescriptor or
                RubyIROpCode.SendSelfBlockDescriptor))
        {
            yield return instruction.Src1;
            yield return instruction.Src2;
        }

        switch (instruction.OpCode)
        {
            case RubyIROpCode.Send:
            case RubyIROpCode.SendSelf:
            case RubyIROpCode.SendBlock:
            case RubyIROpCode.SendSelfBlock:
            case RubyIROpCode.SendBlockDescriptor:
            case RubyIROpCode.SendSelfBlockDescriptor:
            case RubyIROpCode.PureUnarySend:
            case RubyIROpCode.VirtualNew:
            {
                var start = exe.CallSiteArgumentStart(instruction.Aux);
                var argc = exe.GetCallSiteArgumentCount(instruction.Aux);
                for (var a = 0; a < argc; a++) yield return exe.OperandPoolValue(start + a);
                break;
            }
            case RubyIROpCode.NewArray:
            case RubyIROpCode.NewArray2:
            case RubyIROpCode.NewHash:
            {
                var start = exe.OperandListStart(instruction.Aux);
                var count = exe.GetOperandListCount(instruction.Aux);
                for (var a = 0; a < count; a++) yield return exe.OperandPoolValue(start + a);
                break;
            }
        }
    }

    // --- union-find ---
    static int Find(int[] uf, int x)
    {
        while (uf[x] != x)
        {
            uf[x] = uf[uf[x]];
            x = uf[x];
        }
        return x;
    }

    static void Union(int[] uf, int a, int b)
    {
        var ra = Find(uf, a);
        var rb = Find(uf, b);
        if (ra == rb) return;
        // keep the smaller root for deterministic representatives
        if (ra < rb) uf[rb] = ra; else uf[ra] = rb;
    }

    // --- bitset ---
    static void SetBit(ulong[] bits, int i) => bits[i >> 6] |= 1UL << (i & 63);
    static void ClearBit(ulong[] bits, int i) => bits[i >> 6] &= ~(1UL << (i & 63));
    static bool TestBit(ulong[] bits, int i) => (bits[i >> 6] & (1UL << (i & 63))) != 0;
    static void Or(ulong[] dst, ulong[] src)
    {
        for (var w = 0; w < dst.Length; w++) dst[w] |= src[w];
    }
    // OR src into dst, returning true if any bit was added (for the reaching-defs fixpoint).
    static bool OrChanged(ulong[] dst, ulong[] src)
    {
        var changed = false;
        for (var w = 0; w < dst.Length; w++)
        {
            var before = dst[w];
            var after = before | src[w];
            if (after != before) { dst[w] = after; changed = true; }
        }
        return changed;
    }
}
