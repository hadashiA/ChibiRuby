using ChibiRuby;

namespace ChibiRuby.JetPack.Mrb2Cs;

// True-AOT front-end for mrb2cs: register a program's classes/modules/methods into the state
// WITHOUT executing it. mruby's class/def are runtime ops, so the normal way to discover
// definitions is to run the program (which also runs main — not AOT). This instead statically
// walks the def-structural ops (CLASS/MODULE/EXEC/TDEF/DEF/METHOD/TCLASS/OCLASS, + the GETCONST/
// LOADNIL/MOVE feeding outer/super) and drives the same DefineClass/DefineMethod APIs the VM
// uses — never running method bodies or top-level main.
//
// Best-effort and CONSERVATIVE: it tracks only registers it can resolve to a class or a method
// irep; any op it doesn't model clears its destination register, so an unresolved class/super/
// method is simply SKIPPED (those methods stay interpreted — correct, just not AOT-compiled). It
// never registers a method on a wrong class. EXT-widened operands abort the walk for that irep.
public static class DefinitionLoader
{
    public static void Load(MRubyState state, Irep root)
    {
        Walk(state, root, state.ObjectClass);
    }

    static void Walk(MRubyState state, Irep irep, RClass currentClass)
    {
        var seq = irep.Sequence;
        var n = irep.RegisterVariableCount < 1 ? 256 : irep.RegisterVariableCount + 1;
        var regClass = new RClass?[n];      // register -> the class object it holds, if known
        var regProc = new Irep?[n];         // register -> the method-body irep it holds (from OP_METHOD)

        void Clear(int r) { if ((uint)r < (uint)n) { regClass[r] = null; regProc[r] = null; } }

        var pc = 0;
        while (pc < seq.Length)
        {
            var op = (OpCode)seq[pc];
            // EXT prefixes widen the next op's operands; we don't model widened operands, so stop
            // (the rest of this irep's defs are skipped — safe).
            if (op is OpCode.EXT1 or OpCode.EXT2 or OpCode.EXT3) return;

            var a = pc + 1 < seq.Length ? seq[pc + 1] : 0;
            var b = pc + 2 < seq.Length ? seq[pc + 2] : 0;

            switch (op)
            {
                case OpCode.Move:
                    if ((uint)a < (uint)n && (uint)b < (uint)n) { regClass[a] = regClass[b]; regProc[a] = regProc[b]; }
                    break;
                case OpCode.LoadNil:
                    Clear(a);
                    break;
                case OpCode.OClass:
                    if ((uint)a < (uint)n) { regClass[a] = state.ObjectClass; regProc[a] = null; }
                    break;
                case OpCode.TClass:
                    if ((uint)a < (uint)n) { regClass[a] = currentClass; regProc[a] = null; }
                    break;
                case OpCode.LoadSelf:
                    // At a class/module body scope self IS the class being defined.
                    if ((uint)a < (uint)n) { regClass[a] = currentClass; regProc[a] = null; }
                    break;
                case OpCode.GetConst:
                case OpCode.GetMCnst:
                    if ((uint)a < (uint)n)
                    {
                        regProc[a] = null;
                        regClass[a] = (uint)b < (uint)irep.Symbols.Length &&
                                      state.TryGetConst(irep.Symbols[b], out var cv) && cv.Object is RClass kc
                            ? kc : null;
                    }
                    break;
                case OpCode.Method:
                    if ((uint)a < (uint)n) { regProc[a] = (uint)b < (uint)irep.Children.Length ? irep.Children[b] : null; regClass[a] = null; }
                    break;
                case OpCode.Class:
                    DefineClassAt(state, irep, currentClass, regClass, a, b, n);
                    break;
                case OpCode.Module:
                    DefineModuleAt(state, irep, regClass, a, b, n);
                    break;
                case OpCode.Exec:
                    // Run the class/module body irep statically (NOT via the VM) under its class.
                    if ((uint)a < (uint)n && regClass[a] is { } bodyClass && (uint)b < (uint)irep.Children.Length)
                    {
                        Walk(state, irep.Children[b], bodyClass);
                    }
                    break;
                case OpCode.TDef:
                    // The normal `def name; ...; end` inside a class/module body (and at top level).
                    // Mirrors the VM's OP_TDEF (MRubyState.Vm.cs): BBB, target = the lexical scope's
                    // class (here `currentClass`), method symbol = B, proc body = child irep C.
                    DefineTDef(state, irep, currentClass, b, pc + 3 < seq.Length ? seq[pc + 3] : 0);
                    break;
                case OpCode.Def:
                    // The `define_method`/proc-in-register form: target class in reg[A], proc in reg[A+1].
                    if ((uint)a < (uint)n && (uint)(a + 1) < (uint)n &&
                        regClass[a] is { } defClass && regProc[a + 1] is { } methodIrep &&
                        (uint)b < (uint)irep.Symbols.Length)
                    {
                        var proc = state.NewProc(methodIrep, defClass);
                        // Mirror the VM's OP_METHOD: a method proc is strict + scoped. Trivial
                        // accessor/setter detection requires these flags, so without them the
                        // accessor registry stays empty and no devirtualization happens.
                        proc.SetFlag(MRubyObjectFlags.ProcStrict | MRubyObjectFlags.ProcScope);
                        state.DefineMethod(defClass, irep.Symbols[b], MRubyMethod.CreateFromProc(proc));
                    }
                    break;
                default:
                    // Any other op (the program's actual logic): not modeled — its destination
                    // register (operand A, for the ops that have one) no longer holds a tracked value.
                    Clear(a);
                    break;
            }

            pc += 1 + OperandBytes(op);
        }
    }

    static void DefineClassAt(MRubyState state, Irep irep, RClass currentClass, RClass?[] regClass, int a, int b, int n)
    {
        if ((uint)a >= (uint)n || (uint)b >= (uint)irep.Symbols.Length) return;
        var name = irep.Symbols[b];
        var outer = regClass[a] ?? currentClass; // nil outer -> lexical scope
        var super = (uint)(a + 1) < (uint)n ? regClass[a + 1] : null;
        RClass cls;
        if (state.TryGetConst(name, outer, out var existing) && existing.Object is RClass reopened)
        {
            cls = reopened; // reopening an existing class/module
        }
        else
        {
            cls = state.DefineClass(name, super ?? state.ObjectClass, outer: outer);
            // DefineClass only links the class path; bind the constant in `outer` so the class
            // is reachable from EnumerateAotMethods (which walks outer.InstanceVariables).
            state.DefineConst(outer, name, new MRubyValue(cls));
        }
        regClass[a] = cls;
    }

    static void DefineTDef(MRubyState state, Irep irep, RClass target, int symbolIdx, int childIdx)
    {
        if ((uint)symbolIdx >= (uint)irep.Symbols.Length || (uint)childIdx >= (uint)irep.Children.Length) return;
        var proc = state.NewProc(irep.Children[childIdx], target);
        proc.SetFlag(MRubyObjectFlags.ProcStrict | MRubyObjectFlags.ProcScope);
        state.DefineMethod(target, irep.Symbols[symbolIdx], MRubyMethod.CreateFromProc(proc));
    }

    static void DefineModuleAt(MRubyState state, Irep irep, RClass?[] regClass, int a, int b, int n)
    {
        if ((uint)a >= (uint)n || (uint)b >= (uint)irep.Symbols.Length) return;
        var name = irep.Symbols[b];
        var outer = regClass[a] ?? state.ObjectClass;
        if (state.TryGetConst(name, outer, out var existing) && existing.Object is RClass reopened)
        {
            regClass[a] = reopened;
        }
        else
        {
            var mod = state.DefineModule(name, outer);
            state.DefineConst(outer, name, new MRubyValue(mod));
            regClass[a] = mod;
        }
    }

    // Operand bytes AFTER the 1-byte opcode (mruby 4.0). Derived from the disassembler's per-op
    // operand reads (MRubyState.Dump.cs): Z=0, B=1, S/BB=2, BS/BBB/W=3, BSS=5. Add/Sub are B
    // (they `goto case EQ`). Anything not listed is treated as 0 (safe: a too-short advance just
    // ends the walk early / mis-decodes into the conservative default path, never mis-registers).
    static int OperandBytes(OpCode op) => op switch
    {
        OpCode.Nop or OpCode.Stop or OpCode.Call or OpCode.KeyEnd or
        OpCode.EXT1 or OpCode.EXT2 or OpCode.EXT3 => 0,

        OpCode.Mul or OpCode.Div or OpCode.EQ or OpCode.LT or OpCode.LE or OpCode.GT or OpCode.GE or
        OpCode.Add or OpCode.Sub or OpCode.AryCat or OpCode.ArySplat or OpCode.Break or OpCode.Err or
        OpCode.Except or OpCode.GetIdx or OpCode.HashCat or OpCode.Intern or OpCode.LoadF or
        OpCode.LoadI_0 or OpCode.LoadI_1 or OpCode.LoadI_2 or OpCode.LoadI_3 or OpCode.LoadI_4 or
        OpCode.LoadI_5 or OpCode.LoadI_6 or OpCode.LoadI_7 or OpCode.LoadI__1 or OpCode.LoadNil or
        OpCode.LoadSelf or OpCode.LoadT or OpCode.MatchErr or OpCode.OClass or OpCode.RaiseIf or
        OpCode.RangeExc or OpCode.RangeInc or OpCode.Return or OpCode.ReturnBlk or OpCode.SClass or
        OpCode.SetIdx or OpCode.StrCat or OpCode.TClass or OpCode.Undef => 1,

        OpCode.Jmp or OpCode.JmpUw or
        OpCode.AddI or OpCode.SubI or OpCode.Alias or OpCode.Array or OpCode.AryPush or OpCode.BlkCall or
        OpCode.Block or OpCode.Class or OpCode.Def or OpCode.GetCV or OpCode.GetConst or OpCode.GetGV or
        OpCode.GetIV or OpCode.GetIdx0 or OpCode.GetMCnst or OpCode.GetSV or OpCode.Hash or OpCode.HashAdd or
        OpCode.KArg or OpCode.KeyP or OpCode.Lambda or OpCode.LoadI8 or OpCode.LoadL or OpCode.LoadSym or
        OpCode.Method or OpCode.Module or OpCode.Move or OpCode.Rescue or OpCode.SSend0 or OpCode.Send0 or
        OpCode.SetCV or OpCode.SetConst or OpCode.SetGV or OpCode.SetIV or OpCode.SetMCnst or OpCode.SetSV or
        OpCode.String or OpCode.Super or OpCode.Symbol or OpCode.Exec => 2,

        OpCode.Enter or
        OpCode.APost or OpCode.ARef or OpCode.ASet or OpCode.AddILV or OpCode.Array2 or OpCode.Debug or
        OpCode.GetUpVar or OpCode.SDef or OpCode.SSend or OpCode.SSendB or OpCode.Send or OpCode.SendB or
        OpCode.SetUpVar or OpCode.SubILV or OpCode.TDef or
        OpCode.ArgAry or OpCode.BlkPush or OpCode.JmpIf or OpCode.JmpNil or OpCode.JmpNot or OpCode.LoadI16 => 3,

        OpCode.LoadI32 => 5,

        _ => 0,
    };
}
