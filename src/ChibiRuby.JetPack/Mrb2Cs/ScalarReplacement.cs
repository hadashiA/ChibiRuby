using System;
using System.Collections.Generic;
using System.Text;
namespace ChibiRuby.JetPack.Mrb2Cs;

// A scalar-replaced allocation: a `Const.new(...)` whose object never escapes the
// method, so it is not allocated — each of its fields becomes a C# local. initialize is
// inlined (field local <- ctor arg) and trivial-accessor sends on the object (`o.x`,
// `o.x=`) become direct field-local reads/writes. Validity (constant still the same
// class, initialize + accessors not redefined) is guarded once at the new site.
sealed class ScalarObject(int valueId, RClass klass, Symbol constName, ulong initFingerprint, int ctorArgCount)
{
    public int ValueId { get; } = valueId;
    public RClass Klass { get; } = klass;
    public Symbol ConstName { get; } = constName;
    public ulong InitFingerprint { get; } = initFingerprint;
    public int CtorArgCount { get; } = ctorArgCount;
    public HashSet<int> Aliases { get; } = [valueId];
    // Field symbols in initialize order; FieldArg[i] = which ctor arg sets field i.
    public List<Symbol> Fields { get; } = [];
    public List<int> FieldArg { get; } = [];
    // method symbol -> (field index into Fields, isSetter, callee fingerprint).
    public Dictionary<Symbol, (int FieldIndex, bool IsSetter, ulong Fingerprint)> Accessors { get; } = new();

    public int FieldIndexOf(Symbol field)
    {
        for (var i = 0; i < Fields.Count; i++)
        {
            if (Fields[i] == field) return i;
        }
        return -1;
    }
}

// Static escape analysis + scalar-replacement planning over one method's IR. Built
// before emission; supplies field-local declarations and per-op emission for the
// VirtualNew + accessor sends of every non-escaping, statically-typed allocation.
sealed class ScalarContext
{
    internal readonly MRubyState state;
    internal readonly string methodName;
    internal readonly SymbolCache sym;
    internal readonly Dictionary<int, ScalarObject> objects;
    // Escaping `Const.new(args)` that can't be scalar-replaced but whose construction can be
    // inlined (plain-Object class, simple `@f=arg` initialize): valueId -> initialize template.
    internal readonly Dictionary<int, ScalarObject> fastNew;
    internal int guardCount;
    // Per (object,inlined-method) inline-cache fields for the validity guard.
    internal readonly Dictionary<(int, Symbol), int> guardSlot = new();

    ScalarContext(MRubyState state, string methodName, SymbolCache sym, Dictionary<int, ScalarObject> objects, Dictionary<int, ScalarObject> fastNew)
    {
        this.state = state;
        this.methodName = methodName;
        this.sym = sym;
        this.objects = objects;
        this.fastNew = fastNew;
    }

    public bool IsScalar(int valueId) => objects.ContainsKey(valueId);
    public bool IsFastNew(int valueId) => fastNew.ContainsKey(valueId);

    public ScalarObject GetScalarObject(int valueId) => objects[valueId];

    // For a Send, report whether it is an accessor on a scalar object and which field.
    public bool TryGetAccessorSend(RubyIRMethod exe, in RubyIRInstruction ins, out int objId, out int fieldIndex, out bool isSetter)
    {
        objId = -1; fieldIndex = -1; isSetter = false;
        if (ins.OpCode is not RubyIROpCode.Send) return false;
        if (!objects.TryGetValue(ins.Src0, out var o)) return false;
        if (!o.Accessors.TryGetValue(exe.GetCallSiteSymbol(ins.Aux), out var acc)) return false;
        objId = o.ValueId; fieldIndex = acc.FieldIndex; isSetter = acc.IsSetter;
        return true;
    }

    // True when `ins` is an accessor send whose receiver is a scalar object (so it lowers
    // to a field access, not a real call) — lets the caller keep the method leaf.
    public bool IsAccessorSendOnScalar(in RubyIRInstruction ins) =>
        (ins.OpCode is RubyIROpCode.Send) &&
        objects.TryGetValue(ins.Src0, out var o) &&
        o.Accessors.ContainsKey(SymbolOfSend(ins));

    Symbol SymbolOfSend(in RubyIRInstruction ins) => sendSymbols![ins.Aux];
    Symbol[]? sendSymbols;

    public static ScalarContext? TryBuild(MRubyState state, RubyIRMethod exe, SymbolCache sym, string methodName)
    {
        if (Environment.GetEnvironmentVariable("AOT_NOSCALAR") == "1") return null;
        var instructions = exe.Instructions;
        // SSA single-definition map: value-id -> defining instruction index (-1 = arg/self).
        var defIndex = new int[exe.ValueCount];
        for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
        for (var i = 0; i < instructions.Length; i++)
        {
            var d = instructions[i].Dst;
            if ((uint)d < (uint)defIndex.Length) defIndex[d] = i;
        }

        var initCache = new Dictionary<RClass, ScalarObject?>();
        var objects = new Dictionary<int, ScalarObject>();
        var fastNew = new Dictionary<int, ScalarObject>();
        for (var i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode != RubyIROpCode.VirtualNew) continue;
            var obj = TryAnalyze(state, exe, instructions, defIndex, i, initCache);
            if (obj is not null)
            {
                foreach (var alias in obj.Aliases)
                {
                    objects[alias] = obj;
                }
                continue;
            }
            // Couldn't scalar-replace (the object escapes). If it's still a plain-Object
            // class with a simple `@f=arg` initialize, inline the construction (alloc + ivar
            // stores) to skip the `:new` + `:initialize` double dispatch.
            var template = TryAnalyzeFastNew(state, exe, instructions, defIndex, i, initCache);
            if (template is not null && !objects.ContainsKey(instructions[i].Dst))
            {
                fastNew[instructions[i].Dst] = template;
            }
        }
        if (objects.Count == 0 && fastNew.Count == 0) return null;

        var ctx = new ScalarContext(state, methodName, sym, objects, fastNew)
        {
            // Cache the per-callsite send symbols once for IsAccessorSendOnScalar.
            sendSymbols = new Symbol[CallSiteUpperBound(instructions)]
        };
        foreach (var i in instructions)
        {
            if (RubyIROpInfo.IsSendOp(i.OpCode) || i.OpCode == RubyIROpCode.PureUnarySend)
            {
                ctx.sendSymbols[i.Aux] = exe.GetCallSiteSymbol(i.Aux);
            }
        }
        return ctx;
    }

    static int CallSiteUpperBound(ReadOnlySpan<RubyIRInstruction> ins)
    {
        var max = 0;
        foreach (var i in ins)
        {
            if (RubyIROpInfo.IsSendOp(i.OpCode) || i.OpCode == RubyIROpCode.PureUnarySend)
            {
                if (i.Aux + 1 > max) max = i.Aux + 1;
            }
        }
        return max;
    }

    static ScalarObject? TryAnalyze(
        MRubyState state, RubyIRMethod exe, ReadOnlySpan<RubyIRInstruction> ins,
        int[] defIndex, int newIndex, Dictionary<RClass, ScalarObject?> initCache)
    {
        var newIns = ins[newIndex];
        var objId = newIns.Dst;
        foreach (var captured in exe.ClosureCapturedValueIds)
        {
            if (captured == objId) return null;
        }
        // Receiver of `.new` must trace to a GetConstant we can resolve to a class now.
        var classVid = newIns.Src0;
        if ((uint)classVid >= (uint)defIndex.Length || defIndex[classVid] < 0) return null;
        var classDef = ins[defIndex[classVid]];
        if (classDef.OpCode != RubyIROpCode.GetConstant) return null;
        var constSym = exe.GetSymbol(classDef.Aux);
        if (!state.TryGetConst(constSym, out var classValue) ||
            classValue.Object is not RClass klass)
        {
            return null;
        }

        // initialize must be a simple `@field_i = param_i` setter (cached per class).
        var template = AnalyzeInitialize(state, klass, constSym, initCache);
        if (template is null) return null;

        var newArgc = exe.GetCallSiteArgumentCount(newIns.Aux);
        if (newArgc != template.CtorArgCount) return null;

        var obj = new ScalarObject(objId, klass, constSym, template.InitFingerprint, template.CtorArgCount);
        obj.Fields.AddRange(template.Fields);
        obj.FieldArg.AddRange(template.FieldArg);

        // Escape analysis: every use of objId (and aliases created by SSA moves, such as
        // an inlined callee's return slot) must be a trivial accessor/direct field access.
        // Anything else escapes.
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var j = 0; j < ins.Length; j++)
            {
                if (j == newIndex) continue;
                var u = ins[j];
                var allowSrc0 = false;
                switch (u.OpCode)
                {
                    case RubyIROpCode.Move when obj.Aliases.Contains(u.Src0):
                    {
                        if ((uint)u.Dst >= (uint)defIndex.Length) return null;
                        foreach (var captured in exe.ClosureCapturedValueIds)
                        {
                            if (captured == u.Dst) return null;
                        }
                        if (obj.Aliases.Add(u.Dst)) changed = true;
                        allowSrc0 = true;
                        break;
                    }
                    case RubyIROpCode.Send when obj.Aliases.Contains(u.Src0):
                    {
                        var msym = exe.GetCallSiteSymbol(u.Aux);
                        var acc = RecognizeAccessor(state, klass, msym);
                        if (acc is null) return null;
                        var fieldIdx = obj.FieldIndexOf(acc.Value.Field);
                        if (fieldIdx < 0) return null;
                        var argc = exe.GetCallSiteArgumentCount(u.Aux);
                        if (acc.Value.IsSetter ? argc != 1 : argc != 0) return null;
                        obj.Accessors[msym] = (fieldIdx, acc.Value.IsSetter, acc.Value.Fingerprint);
                        allowSrc0 = true;
                        break;
                    }
                    default:
                    {
                        if (IsFieldAccessOnScalar(exe, u, obj) || u.OpCode == RubyIROpCode.GuardInlineClass && obj.Aliases.Contains(u.Src0))
                        {
                            allowSrc0 = true;
                        }

                        break;
                    }
                }

                if (ObjectAppears(exe, u, obj.Aliases, allowSrc0)) return null;
            }
        }

        return obj;
    }

    static bool IsFieldAccessOnScalar(RubyIRMethod exe, in RubyIRInstruction ins, ScalarObject obj)
    {
        if (!obj.Aliases.Contains(ins.Src0) ||
            ins.OpCode is not (
                RubyIROpCode.GetInstanceVariable or
                RubyIROpCode.VirtualGetField or
                RubyIROpCode.SetInstanceVariable or
                RubyIROpCode.VirtualSetField))
        {
            return false;
        }

        return obj.FieldIndexOf(exe.GetSymbol(ins.Aux)) >= 0;
    }

    // True if objId appears in any operand slot of `u` other than (optionally) Src0.
    static bool ObjectAppears(RubyIRMethod exe, in RubyIRInstruction u, HashSet<int> aliases, bool allowSrc0)
    {
        if (!allowSrc0 && aliases.Contains(u.Src0)) return true;
        if (u.OpCode != RubyIROpCode.GuardInlineClass &&
            (aliases.Contains(u.Src1) || aliases.Contains(u.Src2))) return true;
        if (RubyIROpInfo.IsSendOp(u.OpCode) || u.OpCode is RubyIROpCode.PureUnarySend or RubyIROpCode.VirtualNew)
        {
            // VirtualNew carries ctor args in a callsite too: an object passed as a
            // constructor argument escapes (the new object may retain it).
            var argc = exe.GetCallSiteArgumentCount(u.Aux);
            for (var a = 0; a < argc; a++)
            {
                if (aliases.Contains(exe.GetCallSiteArgumentValueId(u.Aux, a))) return true;
            }
        }
        else if (u.OpCode is RubyIROpCode.NewArray or RubyIROpCode.NewArray2 or RubyIROpCode.NewHash)
        {
            var c = exe.GetOperandListCount(u.Aux);
            for (var a = 0; a < c; a++)
            {
                if (aliases.Contains(exe.GetOperandListValueId(u.Aux, a))) return true;
            }
        }
        return false;
    }

    // A `Const.new(args)` whose construction can be inlined even though the object escapes:
    // the constant resolves to a plain-Object class with a simple `@f=arg` initialize and the
    // arity matches. Returns the initialize template (shared/cached) or null.
    static ScalarObject? TryAnalyzeFastNew(
        MRubyState state, RubyIRMethod exe, ReadOnlySpan<RubyIRInstruction> ins,
        int[] defIndex, int newIndex, Dictionary<RClass, ScalarObject?> initCache)
    {
        var newIns = ins[newIndex];
        var classVid = newIns.Src0;
        if ((uint)classVid >= (uint)defIndex.Length || defIndex[classVid] < 0) return null;
        var classDef = ins[defIndex[classVid]];
        if (classDef.OpCode != RubyIROpCode.GetConstant) return null;
        var constSym = exe.GetSymbol(classDef.Aux);
        if (!state.TryGetConst(constSym, out var classValue) ||
            classValue.Object is not RClass klass ||
            klass.InstanceVType != MRubyVType.Object)
        {
            return null;
        }
        var template = AnalyzeInitialize(state, klass, constSym, initCache);
        if (template is null || exe.GetCallSiteArgumentCount(newIns.Aux) != template.CtorArgCount) return null;
        return template;
    }

    // Recognize `def initialize(a, b, ...); @f1 = a; @f2 = b; ...; end` — every mandatory
    // param stored to one field on self, nothing else. Returns a template (field order +
    // arg mapping + fingerprint) or null. Cached per class.
    // The struct layout for a stack-allocatable class: field symbols (initialize order), per
    // field which ctor arg sets it, the initialize fingerprint, and arg count. Null if the
    // class has no simple `@f=arg` initialize. Reuses AnalyzeInitialize.
    internal static (System.Collections.Generic.List<Symbol> Fields, System.Collections.Generic.List<int> FieldArg, ulong InitFingerprint, int CtorArgCount)?
        GetStackLayout(MRubyState state, RClass klass, Symbol constName)
    {
        var o = AnalyzeInitialize(state, klass, constName, new Dictionary<RClass, ScalarObject?>());
        return o is null ? null : (o.Fields, o.FieldArg, o.InitFingerprint, o.CtorArgCount);
    }

    static ScalarObject? AnalyzeInitialize(MRubyState state, RClass klass, Symbol constName, Dictionary<RClass, ScalarObject?> cache)
    {
        if (cache.TryGetValue(klass, out var cached)) return cached;
        cache[klass] = null; // guard against initialize that re-news the same class (recursion)

        ScalarObject? result = null;
        if (state.TryFindMethod(klass, state.Intern("initialize"u8), out var method, out _) &&
            method.Proc is { } proc &&
            Mrb2CsCompiler.TryReadMandatoryArgCount(proc.Irep, out var iargc) &&
            iargc > 0)
        {
            RubyIRMethod? exeI;
            try { exeI = RubyIRBuilder.Build(proc.Irep, 0, out _); }
            catch { exeI = null; }
            if (exeI is not null)
            {
                var obj = new ScalarObject(0, klass, constName, state.ComputeIrepFingerprint(proc.Irep), iargc);
                var selfVids = new HashSet<int> { 0 };
                foreach (var li in exeI.Instructions)
                {
                    if (li.OpCode == RubyIROpCode.LoadSelf) selfVids.Add(li.Dst);
                }
                var ok = true;
                foreach (var ii in exeI.Instructions)
                {
                    switch (ii.OpCode)
                    {
                        case RubyIROpCode.CheckArity:
                        case RubyIROpCode.LoadSelf:
                        case RubyIROpCode.Return:
                        case RubyIROpCode.ReturnValue:
                        case RubyIROpCode.ReturnSelf:
                            break;
                        case RubyIROpCode.SetInstanceVariable:
                        case RubyIROpCode.VirtualSetField:
                            if (!selfVids.Contains(ii.Src0)) { ok = false; break; }
                            var valueId = ii.Src1;
                            if (valueId < 1 || valueId > iargc) { ok = false; break; }
                            obj.Fields.Add(exeI.GetSymbol(ii.Aux));
                            obj.FieldArg.Add(valueId - 1);
                            break;
                        default:
                            ok = false;
                            break;
                    }
                    if (!ok) break;
                }
                if (ok && obj.Fields.Count > 0) result = obj;
            }
        }
        cache[klass] = result;
        return result;
    }

    // Recognize a trivial getter/setter method `msym` on `klass`. Returns (field, isSetter,
    // fingerprint) or null. Delegates to the irep-based recognizer.
    static (Symbol Field, bool IsSetter, ulong Fingerprint)? RecognizeAccessor(MRubyState state, RClass klass, Symbol msym)
    {
        if (!state.TryFindMethod(klass, msym, out var method, out _) || method.Proc is not { } proc) return null;
        return Analyzer.TryRecognizeTrivialAccessor(state, proc.Irep);
    }

    // Field-local name for field `fieldIndex` of scalar object `objId`.
    internal static string FieldLocal(int objId, int fieldIndex) => "so" + objId + "_" + fieldIndex;

    public bool TryEmitScalarMove(in RubyIRInstruction ins)
    {
        return ins.OpCode == RubyIROpCode.Move &&
               objects.TryGetValue(ins.Src0, out var src) &&
               objects.TryGetValue(ins.Dst, out var dst) &&
               ReferenceEquals(src, dst);
    }

    public bool TryGetScalarFieldAccess(RubyIRMethod exe, in RubyIRInstruction ins, out int objId, out int fieldIndex, out bool isSetter)
    {
        objId = -1; fieldIndex = -1; isSetter = false;
        if (ins.OpCode is not (
                RubyIROpCode.GetInstanceVariable or
                RubyIROpCode.VirtualGetField or
                RubyIROpCode.SetInstanceVariable or
                RubyIROpCode.VirtualSetField))
        {
            return false;
        }
        if (!objects.TryGetValue(ins.Src0, out var o)) return false;
        var idx = o.FieldIndexOf(exe.GetSymbol(ins.Aux));
        if (idx < 0) return false;
        objId = o.ValueId;
        fieldIndex = idx;
        isSetter = ins.OpCode is RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField;
        return true;
    }

}
