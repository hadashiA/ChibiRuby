using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChibiRuby;
namespace ChibiRuby.JetPack.Mrb2Cs;

// Ruby/IR analysis: program-wide devirtualization registries + the trivial-accessor /
// constant-return recognizers they are built from. Pure over the IR (produces facts; emits nothing).
public static class Analyzer
{
    // Trivial getter (`def f; @f; end`) / setter (`def f=(v); @f = v; end`) recognizer over a
    // method irep. Returns (field, isSetter, fingerprint) or null. Used both by scalar
    // replacement (field access on a scalar object) and by cross-object accessor devirt.
    public static (Symbol Field, bool IsSetter, ulong Fingerprint)? TryRecognizeTrivialAccessor(MRubyState state, Irep irep)
    {
        if (!Mrb2CsCompiler.TryReadMandatoryArgCount(irep, out var argc) || argc > 1) return null;
        RubyIRMethod? exe;
        try { exe = RubyIRBuilder.Build(irep, 0, out _); }
        catch { return null; }
        if (exe is null) return null;

        var self = new HashSet<int> { 0 };
        foreach (var ii in exe.Instructions)
        {
            if (ii.OpCode == RubyIROpCode.LoadSelf) self.Add(ii.Dst);
        }

        Symbol field = default;
        var found = false;
        var isSetter = argc == 1;
        foreach (var ii in exe.Instructions)
        {
            switch (ii.OpCode)
            {
                case RubyIROpCode.CheckArity:
                case RubyIROpCode.LoadSelf:
                case RubyIROpCode.Return:
                case RubyIROpCode.ReturnValue:
                case RubyIROpCode.ReturnSelf:
                    break;
                case RubyIROpCode.GetInstanceVariable:
                case RubyIROpCode.VirtualGetField:
                    if (isSetter || found || !self.Contains(ii.Src0)) return null;
                    field = exe.GetSymbol(ii.Aux); found = true;
                    break;
                case RubyIROpCode.SetInstanceVariable:
                case RubyIROpCode.VirtualSetField:
                    if (!isSetter || found || !self.Contains(ii.Src0) || ii.Src1 != 1) return null;
                    field = exe.GetSymbol(ii.Aux); found = true;
                    break;
                default:
                    return null;
            }
        }
        return found ? (field, isSetter, state.ComputeIrepFingerprint(irep)) : null;
    }

    // A 0-arg method whose body is a PURE return of an immediate constant — directly (`def n; 8; end`)
    // or by delegating to another such method (`def n; m; end` where `m` returns a constant — multi-
    // level, resolved on `klass`). Returns (the constant, THIS method's fingerprint for the call-site
    // guard) or null. Strict: the body may contain only the prologue, the single value producer
    // (a LoadValue immediate or one 0-arg self-send), and the return — anything else (a real
    // computation / side effect / non-immediate literal like a string) disqualifies it.
    public static (MRubyValue Value, ulong Fingerprint)? TryRecognizeConstantReturn(MRubyState state, RClass klass, Irep irep, int depth = 0)
    {
        if (depth > 8) return null; // delegation-cycle backstop
        if (!Mrb2CsCompiler.TryReadMandatoryArgCount(irep, out var argc) || argc != 0) return null;
        RubyIRMethod? exe;
        try { exe = RubyIRBuilder.Build(irep, 0, out _); } catch { return null; }
        if (exe is null) return null;
        var ins = exe.Instructions;
        var defIndex = new int[exe.ValueCount];
        for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
        for (var i = 0; i < ins.Length; i++) { var d = ins[i].Dst; if ((uint)d < (uint)defIndex.Length) defIndex[d] = i; }
        var selfIds = new HashSet<int> { 0 };
        foreach (var u in ins) if (u.OpCode == RubyIROpCode.LoadSelf) selfIds.Add(u.Dst);

        var retId = -1;
        foreach (var u in ins)
            if (u.OpCode is RubyIROpCode.Return or RubyIROpCode.ReturnValue) retId = u.Src0;
            else if (u.OpCode == RubyIROpCode.ReturnSelf) return null;
        if (retId < 0 || (uint)retId >= (uint)defIndex.Length || defIndex[retId] < 0) return null;
        var prodIdx = defIndex[retId];

        // Purity: every op is the prologue (CheckArity/LoadSelf), the value producer, or the return.
        for (var i = 0; i < ins.Length; i++)
        {
            if (i == prodIdx) continue;
            if (ins[i].OpCode is not (RubyIROpCode.CheckArity or RubyIROpCode.LoadSelf
                or RubyIROpCode.Return or RubyIROpCode.ReturnValue)) return null;
        }

        var fp = state.ComputeIrepFingerprint(irep);
        var prod = ins[prodIdx];
        if (prod.OpCode == RubyIROpCode.LoadValue)
        {
            var lit = exe.GetLiteral(prod.Aux);
            return Emitter.TryEmitLiteral(lit, out _) ? (lit, fp) : null; // immediates only (a string literal is a fresh object)
        }
        if (prod.OpCode is RubyIROpCode.Send or RubyIROpCode.SendSelf &&
            (prod.OpCode == RubyIROpCode.SendSelf || selfIds.Contains(prod.Src0)) &&
            exe.GetCallSiteArgumentCount(prod.Aux) == 0)
        {
            var sel2 = exe.GetCallSiteSymbol(prod.Aux);
            if (state.TryFindMethod(klass, sel2, out var m2, out _) && m2.Proc is { } p2 &&
                TryRecognizeConstantReturn(state, klass, p2.Irep, depth + 1) is { } inner)
            {
                return (inner.Value, fp); // inner's constant, THIS method's fp for the guard
            }
        }
        return null;
    }


    // Program-wide map: method selector -> the trivial-accessor it denotes, for cross-object
    // devirtualization. Built by walking every method on the class tree. A selector whose
    // trivial-accessor reading is ambiguous (two classes resolve it to different field/body)
    // is dropped — a guarded devirt would just deopt for one of them anyway. Selectors that
    // are sometimes non-accessor methods stay registered; the per-site fingerprint guard
    // sends those receivers down the slow path, so it remains correct.
    public static Dictionary<Symbol, AccessorTarget> BuildAccessorRegistry(MRubyState state)
    {
        var map = new Dictionary<Symbol, AccessorTarget>();
        var ambiguous = new HashSet<Symbol>();
        state.EnumerateAotMethods((_, methodId, irep) =>
        {
            if (ambiguous.Contains(methodId)) return;
            var acc = TryRecognizeTrivialAccessor(state, irep);
            if (acc is null) return;
            var target = new AccessorTarget(acc.Value.Field, acc.Value.Fingerprint, acc.Value.IsSetter);
            if (map.TryGetValue(methodId, out var prev))
            {
                if (!prev.Equals(target))
                {
                    map.Remove(methodId);
                    ambiguous.Add(methodId);
                }
            }
            else
            {
                map[methodId] = target;
            }
        });
        return map;
    }

    // Program-wide map: selector -> the immediate constant a 0-arg method of that name returns
    // (directly or via delegation). Ambiguity (two classes' same-named methods return different
    // constants) drops the selector; the per-site fingerprint guard sends non-matching receivers
    // to the slow path, so a selector that is a constant-returner for one class and a real method
    // for another stays correct.
    public static Dictionary<Symbol, ConstReturnTarget> BuildConstReturnRegistry(MRubyState state)
    {
        var map = new Dictionary<Symbol, ConstReturnTarget>();
        var ambiguous = new HashSet<Symbol>();
        state.EnumerateAotMethods((cls, methodId, irep) =>
        {
            if (ambiguous.Contains(methodId)) return;
            if (TryRecognizeConstantReturn(state, cls, irep) is not { } c) return;
            var target = new ConstReturnTarget(c.Value, c.Fingerprint);
            if (map.TryGetValue(methodId, out var prev))
            {
                if (!prev.Equals(target)) { map.Remove(methodId); ambiguous.Add(methodId); }
            }
            else
            {
                map[methodId] = target;
            }
        });
        return map;
    }

    public static Dictionary<Symbol, InlineSelectorTarget> BuildInlineSelectorRegistry(
        MRubyState state,
        IReadOnlyDictionary<ulong, int> inlineRegistry)
    {
        var map = new Dictionary<Symbol, InlineSelectorTarget>();
        var ambiguous = new HashSet<Symbol>();
        state.EnumerateAotMethods((definingClass, methodId, irep) =>
        {
            if (ambiguous.Contains(methodId)) return;
            var fp = state.ComputeIrepFingerprint(irep);
            if (!inlineRegistry.TryGetValue(fp, out var argc)) return;
            if (TryRecognizeTrivialAccessor(state, irep) is not null) return;
            if (!TryReadInlineSelectorShape(irep, out var returnsNew)) return;

            var target = new InlineSelectorTarget(irep, argc, fp, definingClass, returnsNew);
            if (map.TryGetValue(methodId, out var prev))
            {
                if (prev.Fingerprint != fp || prev.ArgCount != argc)
                {
                    map.Remove(methodId);
                    ambiguous.Add(methodId);
                }
            }
            else
            {
                map[methodId] = target;
            }
        });
        return map;
    }

    // Build the struct layout for a stack-allocatable class, or null if its initialize isn't a
    // straight-line sequence of `@f = <ctor arg>` / `@f = <literal>` stores to self (no method
    // calls, branches, or reads of other state). Every field starts Boxed/not-Mutated; ②/① fill
    // FieldKinds/FieldNested/Mutated. The initialize fingerprint names the struct type.
    //
    // Dedicated to stack allocation (NOT ScalarContext.AnalyzeInitialize, which only models `@f=arg`
    // and feeds scalar replacement's EmitScalarNew); broadening that would change scalar behavior.
    static StackLayout? GetStackLayout(MRubyState state, RClass klass, Symbol constName)
    {
        if (!state.TryFindMethod(klass, state.Intern("initialize"u8), out var method, out _) ||
            method.Proc is not { } proc ||
            !Mrb2CsCompiler.TryReadMandatoryArgCount(proc.Irep, out var iargc))
        {
            return null;
        }
        RubyIRMethod? exe;
        try { exe = RubyIRBuilder.Build(proc.Irep, 0, out _); }
        catch { return null; }
        if (exe is null) return null;
        var ins = exe.Instructions;
        var defIndex = new int[exe.ValueCount];
        for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
        for (var i = 0; i < ins.Length; i++) { var d = ins[i].Dst; if ((uint)d < (uint)defIndex.Length) defIndex[d] = i; }
        var selfVids = new HashSet<int> { 0 };
        foreach (var li in ins) if (li.OpCode == RubyIROpCode.LoadSelf) selfVids.Add(li.Dst);

        var fields = new List<Symbol>();
        var fieldArg = new List<int>();
        var fieldLiteral = new List<MRubyValue>();
        var fieldKinds = new List<StackFieldKind>();
        var fieldNested = new List<StackLayout?>();
        foreach (var ii in ins)
        {
            switch (ii.OpCode)
            {
                case RubyIROpCode.CheckArity:
                case RubyIROpCode.LoadSelf:
                case RubyIROpCode.LoadValue:       // a literal we may store below
                case RubyIROpCode.GetConstant:     // a class for a nested new we may store below
                case RubyIROpCode.VirtualNew:      // a nested object we may store below
                case RubyIROpCode.Return:
                case RubyIROpCode.ReturnValue:
                case RubyIROpCode.ReturnSelf:
                    break;
                case RubyIROpCode.SetInstanceVariable:
                case RubyIROpCode.VirtualSetField:
                    if (!selfVids.Contains(ii.Src0)) return null;
                    var v = ii.Src1;
                    if (v >= 1 && v <= iargc)            // @f = ctor arg
                    {
                        fields.Add(exe.GetSymbol(ii.Aux));
                        fieldArg.Add(v - 1); fieldLiteral.Add(default);
                        fieldKinds.Add(StackFieldKind.Boxed); fieldNested.Add(null);
                    }
                    else if ((uint)v < (uint)defIndex.Length && defIndex[v] >= 0 &&
                             ins[defIndex[v]].OpCode == RubyIROpCode.LoadValue)   // @f = literal
                    {
                        fields.Add(exe.GetSymbol(ii.Aux));
                        fieldArg.Add(-1); fieldLiteral.Add(exe.GetLiteral(ins[defIndex[v]].Aux));
                        fieldKinds.Add(StackFieldKind.Boxed); fieldNested.Add(null);
                    }
                    else if (TryBuildNestedFill(state, exe, defIndex, v) is { } nested)  // @f = Vec.new(literals)
                    {
                        fields.Add(exe.GetSymbol(ii.Aux));
                        fieldArg.Add(-1); fieldLiteral.Add(default);
                        fieldKinds.Add(StackFieldKind.Nested); fieldNested.Add(nested);
                    }
                    else
                    {
                        return null; // @f = computed value / non-literal nested new -> not yet
                    }
                    break;
                default:
                    return null;
            }
        }
        if (fields.Count == 0) return null;
        var fp = state.ComputeIrepFingerprint(proc.Irep);
        return new StackLayout
        {
            Cls = klass,
            ClassFp = fp,
            ConstName = constName,
            NameSuffix = Emitter.NameSuffixFor(Encoding.UTF8.GetString(state.NameOf(constName))),
            InitFingerprint = fp,
            Fields = fields,
            FieldArg = fieldArg,
            FieldLiteral = fieldLiteral,
            FieldKinds = fieldKinds,
            FieldNested = fieldNested,
        };
    }

    // A field initialized by `@f = Klass.new(<literals>)`: returns a flattened inner layout whose
    // fields are all literal-filled (so it nests as a value with no heap allocation), or null if
    // the value isn't a nested `new` with literal args (computed args = ① ctor-arg nesting, later).
    static StackLayout? TryBuildNestedFill(MRubyState state, RubyIRMethod exe, int[] defIndex, int valueId)
    {
        var ins = exe.Instructions;
        if ((uint)valueId >= (uint)defIndex.Length || defIndex[valueId] < 0) return null;
        var def = ins[defIndex[valueId]];
        if (def.OpCode != RubyIROpCode.VirtualNew) return null;
        // Resolve the constructed class from the .new receiver (a GetConstant), tracing Moves.
        var classVid = def.Src0; Symbol cn = default; RClass? kc = null;
        for (var hops = 0; hops < ins.Length; hops++)
        {
            if ((uint)classVid >= (uint)defIndex.Length || defIndex[classVid] < 0) break;
            var d = ins[defIndex[classVid]];
            if (d.OpCode == RubyIROpCode.Move) { classVid = d.Src0; continue; }
            if (d.OpCode != RubyIROpCode.GetConstant) break;
            cn = exe.GetSymbol(d.Aux);
            if (state.TryGetConst(cn, out var cv) && cv.Object is RClass k) kc = k;
            break;
        }
        if (kc is null) return null;
        var inner = GetStackLayout(state, kc, cn);
        if (inner is null) return null;
        var argc = exe.GetCallSiteArgumentCount(def.Aux);
        var litFill = new List<MRubyValue>();
        for (var i = 0; i < inner.Fields.Count; i++)
        {
            if (inner.FieldKinds[i] != StackFieldKind.Boxed) return null; // nested-nested: defer
            if (inner.FieldArg[i] >= 0)                                    // inner field <- nested new arg (must be literal)
            {
                if (inner.FieldArg[i] >= argc) return null;
                var argVid = exe.GetCallSiteArgumentValueId(def.Aux, inner.FieldArg[i]);
                if ((uint)argVid >= (uint)defIndex.Length || defIndex[argVid] < 0 ||
                    ins[defIndex[argVid]].OpCode != RubyIROpCode.LoadValue) return null;
                litFill.Add(exe.GetLiteral(ins[defIndex[argVid]].Aux));
            }
            else
            {
                litFill.Add(inner.FieldLiteral[i]);                        // inner field is itself a literal
            }
        }
        // Same struct TYPE as the generic inner layout (Stk_<innerfp>); only the fill is literal.
        return new StackLayout
        {
            Cls = inner.Cls,
            ClassFp = inner.ClassFp,
            ConstName = inner.ConstName,
            NameSuffix = inner.NameSuffix,          // same struct TYPE (Stk_<innerfp>...) -> same suffix
            InitFingerprint = inner.InitFingerprint,
            Fields = inner.Fields,
            FieldArg = inner.Fields.ConvertAll(_ => -1),
            FieldLiteral = litFill,
            FieldKinds = inner.Fields.ConvertAll(_ => StackFieldKind.Boxed),
            FieldNested = inner.Fields.ConvertAll(_ => (StackLayout?)null),
        };
    }

    // Find VirtualNew sites whose object can be built on the stack (as a struct) instead of
    // heap-allocated: it never escapes EXCEPT by being passed as a non-retained argument to a
    // callee (per the interprocedural escape summary). Field reads/writes on it and being the
    // receiver of its own accessor sends are fine. Any other use (returned, stored to the heap,
    // put in an array, passed as a ctor arg, receiver of a non-accessor send, passed to a callee
    // that retains the arg) disqualifies it. This is ScalarContext's escape analysis relaxed to allow
    // the proven-non-retaining cross-call argument pass.
    // Conservative, single-method check: does `sel` on `klass` use self ONLY for reads — direct
    // ivar gets and trivial getter sends on self — never retaining it (return self / store it /
    // pass it as an arg / receiver of a non-getter send) or mutating it (ivar set / setter)? Such a
    // callee is safe to compile as a read-only struct-RECEIVER variant (self passed by `in`). The
    // escape summary only tracks params 1..argc (not self), so this is a focused, sound check
    // (anything uncertain -> false). The variant still has a reify+Send fallback on guard miss.
    static bool CalleeSelfReadOnly(MRubyState state, RClass klass, Symbol sel)
    {
        if (!state.TryFindMethod(klass, sel, out var m, out _) || m.Proc is not { } proc) return false;
        RubyIRMethod? exe;
        try { exe = RubyIRBuilder.Build(proc.Irep, 0, out _); } catch { return false; }
        if (exe is null) return false;
        if (Mrb2CsCompiler.SsaEnabled && Mrb2CsCompiler.TryReadMandatoryArgCount(proc.Irep, out var ac)) exe = RubyIRSsaRenumber.Run(exe, ac);
        var ins = exe.Instructions;
        var selfIds = new HashSet<int> { 0 };
        foreach (var u in ins) if (u.OpCode == RubyIROpCode.LoadSelf) selfIds.Add(u.Dst);
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var u in ins) if (u.OpCode == RubyIROpCode.Move && selfIds.Contains(u.Src0) && selfIds.Add(u.Dst)) grew = true;
        }
        // self IS value-id 0, but unused IR operand slots also default to 0 — so the id-0
        // ambiguity must be kept out of VALUE-slot checks. A genuine self-as-value always flows
        // through a LoadSelf temp (id > 0; mruby loads self into a register before using it as a
        // value), so excluding 0 from value checks stays sound. Receiver (Src0) and Return operands
        // are always meaningful, so they use the full set (incl 0).
        bool IsSelfValue(int id) => id != 0 && selfIds.Contains(id);
        foreach (var u in ins)
        {
            var op = u.OpCode;
            if (op is RubyIROpCode.ReturnSelf) return false;                       // returns self
            if (op == RubyIROpCode.Return && selfIds.Contains(u.Src0)) return false; // returns self
            if (IsSelfValue(u.Src1) || IsSelfValue(u.Src2)) return false;            // self stored as a value
            // Self as receiver: only the genuinely dangerous ops disqualify (mutate self / a call
            // that may retain it). Reads (GetInstanceVariable) and ops that don't use Src0 as a
            // receiver (CheckArity, LoadSelf, ... — Src0 spuriously 0, self's own id) are fine; the
            // escape paths are covered by the value/arg/return checks above and below.
            if (selfIds.Contains(u.Src0))
            {
                if (op is RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField or RubyIROpCode.SendSelf)
                {
                    return false; // mutates self / self-context call that may retain self
                }
                if (op == RubyIROpCode.Send)
                {
                    var s = exe.GetCallSiteSymbol(u.Aux);
                    if (!(state.TryFindMethod(klass, s, out var am, out _) && am.Proc is { } ap &&
                          TryRecognizeTrivialAccessor(state, ap.Irep) is { IsSetter: false }))
                    {
                        return false; // non-getter send on self (could retain/mutate)
                    }
                }
            }
            if (RubyIROpInfo.IsSendOp(op) || op is RubyIROpCode.VirtualNew or RubyIROpCode.PureUnarySend)
            {
                var argc = exe.GetCallSiteArgumentCount(u.Aux);
                for (var a = 0; a < argc; a++)
                    if (IsSelfValue(exe.GetCallSiteArgumentValueId(u.Aux, a))) return false; // self passed as arg
            }
        }
        return true;
    }

    // `start` plus the value-ids that are (transitively) Move-copies of it — its alias set.
    internal static HashSet<int> MoveClosure(RubyIRMethod exe, int start)
    {
        var set = new HashSet<int> { start };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var u in exe.Instructions)
                if (u.OpCode == RubyIROpCode.Move && set.Contains(u.Src0) && set.Add(u.Dst))
                    grew = true;
        }
        return set;
    }

    internal static Dictionary<int, StackLayout> FindStackEligible(MRubyState state, RubyIRMethod exe, RubyIREscapeSummary.Summary summary)
    {
        var result = new Dictionary<int, StackLayout>();
        var ins = exe.Instructions;
        var defIndex = new int[exe.ValueCount];
        for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
        for (var i = 0; i < ins.Length; i++) { var d = ins[i].Dst; if ((uint)d < (uint)defIndex.Length) defIndex[d] = i; }

        for (var i = 0; i < ins.Length; i++)
        {
            if (ins[i].OpCode != RubyIROpCode.VirtualNew) continue;
            var objId = ins[i].Dst;
            // Resolve the constructed class from the .new receiver (a GetConstant), tracing Moves.
            var classVid = ins[i].Src0;
            Symbol constName = default;
            RClass? klass = null;
            for (var hops = 0; hops < ins.Length; hops++)
            {
                if ((uint)classVid >= (uint)defIndex.Length || defIndex[classVid] < 0) break;
                var def = ins[defIndex[classVid]];
                if (def.OpCode == RubyIROpCode.Move) { classVid = def.Src0; continue; }
                if (def.OpCode != RubyIROpCode.GetConstant) break;
                constName = exe.GetSymbol(def.Aux);
                if (state.TryGetConst(constName, out var cv) && cv.Object is RClass kc) klass = kc;
                break;
            }
            if (klass is null)
            {
                if (Environment.GetEnvironmentVariable("AOT_ESCAPE_DEBUG") == "1")
                    System.Console.Error.WriteLine($"[stackobj]   v{objId}: class unresolved");
                continue;
            }

            if (IsStackEligible(state, exe, defIndex, i, objId, klass, summary, out var aliases, out var mutated) &&
                GetStackLayout(state, klass, constName) is { } layout)
            {
                layout.Mutated = mutated; // mutated by a callee -> passed by ref (A)
                // Map every alias (the VirtualNew dst + its Move copies) to the layout so each
                // becomes a struct local; Moves between them are struct value-copies (sound for a
                // read-only object; a ref/Mutated object's aliases share the same struct local too).
                foreach (var a in aliases) result[a] = layout;
            }
            else if (Environment.GetEnvironmentVariable("AOT_ESCAPE_DEBUG") == "1")
            {
                System.Console.Error.WriteLine($"[stackobj]   v{objId} = new {state.NameOf(constName)}: REJECTED ({StackRejectReason})");
            }
        }

        PropagateNestedReads(state, exe, defIndex, result, summary);
        DropUndispatchableMultiStructSends(state, exe, result);
        return result;
    }

    // Two lowerings dispatch a stack object at a Send: the RECEIVER (struct `self`, no struct args)
    // via TryEmitStackReceiverSend, OR one-or-more struct ARGS (receiver not a struct) via
    // TryEmitStackArgSend. A Send that is BOTH (struct receiver AND struct args) has no lowering, so
    // drop its struct args, keeping the receiver. Fixpoint (dropping an object can relieve another
    // Send). Multiple struct args with a non-struct receiver are fine (handled).
    static void DropUndispatchableMultiStructSends(MRubyState state, RubyIRMethod exe, Dictionary<int, StackLayout> result)
    {
        var ins = exe.Instructions;
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var j = 0; j < ins.Length; j++)
            {
                if (ins[j].OpCode is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) continue;
                if (!result.ContainsKey(ins[j].Src0)) continue; // receiver not a struct -> arg path handles N args
                var argc = exe.GetCallSiteArgumentCount(ins[j].Aux);
                var victims = new List<StackLayout>();
                for (var a = 0; a < argc; a++)
                    if (result.TryGetValue(exe.GetCallSiteArgumentValueId(ins[j].Aux, a), out var alay)) victims.Add(alay);
                if (victims.Count == 0) continue; // struct receiver, no struct args -> receiver path handles it
                foreach (var victim in victims)
                    foreach (var id in new List<int>(result.Keys))
                        if (ReferenceEquals(result[id], victim)) result.Remove(id);
                changed = true;
                break; // result mutated; restart the scan
            }
        }
    }

    // Cascade: reading a Nested field of a stack object yields the inner struct as a value
    // (`ray.dir` -> a Stk_Vec). Track that result value-id as a stack object too (using the inner
    // layout) when it is itself used stack-safely, so e.g. `ray.dir.vdot(@n)` lowers to a struct-
    // receiver variant instead of forcing a reify. Fixpoint (a nested read can feed another). The
    // read itself is the dst's "def" (skipped via newIndex in the use check). Run BOTH after
    // FindStackEligible (method/block-local objects) AND after a variant's struct param is
    // registered (the param's own nested fields), so a `param.inner.m()` chain cascades too.
    internal static void PropagateNestedReads(MRubyState state, RubyIRMethod exe, int[] defIndex, Dictionary<int, StackLayout> result, RubyIREscapeSummary.Summary summary)
    {
        var ins = exe.Instructions;
        var grew = true;
        while (grew)
        {
            grew = false;
            for (var j = 0; j < ins.Length; j++)
            {
                if (ins[j].OpCode != RubyIROpCode.Send) continue;
                if (!result.TryGetValue(ins[j].Src0, out var olay)) continue;     // recv is a stack object
                if (result.ContainsKey(ins[j].Dst)) continue;                     // already tracked
                var fi = NestedFieldGetterIndex(state, olay, exe.GetCallSiteSymbol(ins[j].Aux));
                if (fi < 0) continue;                                             // not a nested-field getter
                var innerLay = olay.FieldNested[fi]!;
                // A nested-field read yields a COPY (`so90 = so30.f0`); mutating it would not write
                // back to the parent, so only cascade read-only (non-mutated) inner values.
                if (IsStackEligible(state, exe, defIndex, j, ins[j].Dst, innerLay.Cls, summary, out var caliases, out var cmut) && !cmut)
                {
                    foreach (var a in caliases) result[a] = innerLay;
                    grew = true;
                }
            }
        }
    }

    // The field index whose trivial getter is `sel` on `lay.Cls` AND which is a Nested field, or -1.
    static int NestedFieldGetterIndex(MRubyState state, StackLayout lay, Symbol sel)
    {
        if (!state.TryFindMethod(lay.Cls, sel, out var m, out _) || m.Proc is not { } proc) return -1;
        if (TryRecognizeTrivialAccessor(state, proc.Irep) is not { IsSetter: false } acc) return -1;
        for (var i = 0; i < lay.Fields.Count; i++)
            if (lay.FieldKinds[i] == StackFieldKind.Nested && lay.Fields[i] == acc.Field) return i;
        return -1;
    }

    static string StackRejectReason = "";

    static bool IsStackEligible(MRubyState state, RubyIRMethod exe, int[] defIndex, int newIndex, int objId, RClass klass, RubyIREscapeSummary.Summary summary, out HashSet<int> aliases, out bool mutated)
    {
        bool Reject(string r) { StackRejectReason = r; return false; }
        mutated = false;
        var ins = exe.Instructions;
        // Alias set via Move copies, assigned up front so it is valid on every return path. The
        // caller maps EVERY alias id to the struct layout, so a `b = a` copy of a (read-only,
        // Stage 1) stack object becomes a struct value-copy that shares the same fields.
        aliases = new HashSet<int> { objId };
        foreach (var captured in exe.ClosureCapturedValueIds)
        {
            if (captured == objId) return Reject("captured");
        }
        var grew = true;
        while (grew)
        {
            grew = false;
            for (var j = 0; j < ins.Length; j++)
            {
                if (ins[j].OpCode == RubyIROpCode.Move && aliases.Contains(ins[j].Src0))
                {
                    foreach (var captured in exe.ClosureCapturedValueIds) if (captured == ins[j].Dst) return false;
                    if (aliases.Add(ins[j].Dst)) grew = true;
                }
            }
        }

        for (var j = 0; j < ins.Length; j++)
        {
            if (j == newIndex) continue;
            var u = ins[j];
            var op = u.OpCode;
            if (op == RubyIROpCode.Move) continue; // pure alias copy; dest already in `aliases`
            var recvIsObj = aliases.Contains(u.Src0);
            // Receiver of its own accessor send (getter/setter on klass): a field access, ok.
            if (op == RubyIROpCode.Send && recvIsObj)
            {
                var sel = exe.GetCallSiteSymbol(u.Aux);
                var acc = state.TryFindMethod(klass, sel, out var mm, out _) && mm.Proc is { } pr
                    ? TryRecognizeTrivialAccessor(state, pr.Irep) : null;
                // A trivial accessor on the object lowers to a field access (a SETTER mutates the
                // struct -> by ref); a non-accessor method that uses self read-only (per
                // CalleeSelfReadOnly) lowers to a struct-RECEIVER variant call — both keep it stack.
                if (acc is { IsSetter: true }) mutated = true;
                if (acc is null && !CalleeSelfReadOnly(state, klass, sel))
                {
                    return Reject("recv-nonaccessor-send:" + state.NameOf(sel));
                }
                // Accessor arg (a setter's value) must not BE the object (would store it into itself
                // — that's a cycle we don't model). Checked by the arg scan below.
            }
            // Direct ivar op on the object: ok (a SET mutates the struct -> by ref).
            else if (recvIsObj && op is RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField or
                           RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField)
            {
                if (op is RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField) mutated = true;
                // SetInstanceVariable storing the object as a VALUE (Src1) is handled below.
            }
            else if (op == RubyIROpCode.GuardInlineClass && recvIsObj)
            {
                // no-op guard, ok
            }
            else if (recvIsObj)
            {
                // Receiver in any other op (non-accessor send, etc.) -> escape.
                return Reject("recv-other-op:" + op);
            }

            // The object must not appear as Src1/Src2 (stored as a value / index / etc.).
            if (op != RubyIROpCode.GuardInlineClass && (aliases.Contains(u.Src1) || aliases.Contains(u.Src2)))
            {
                return Reject("appears-src1/2:" + op);
            }
            // The object must not be put in a new array.
            if (op is RubyIROpCode.NewArray or RubyIROpCode.NewArray2 or RubyIROpCode.NewHash)
            {
                var c = exe.GetOperandListCount(u.Aux);
                for (var a = 0; a < c; a++) if (aliases.Contains(exe.GetOperandListValueId(u.Aux, a))) return Reject("into-array");
            }
            // Returned -> escape.
            if (op == RubyIROpCode.Return && aliases.Contains(u.Src0)) return Reject("returned");
            // Passed as a call/ctor argument: a ctor arg always retains; a send arg is ok only if
            // the (polymorphic) callee provably does not retain that position for `klass`.
            if (RubyIROpInfo.IsSendOp(op) || op is RubyIROpCode.PureUnarySend or RubyIROpCode.VirtualNew)
            {
                var sel = op == RubyIROpCode.VirtualNew ? default : exe.GetCallSiteSymbol(u.Aux);
                var argc = exe.GetCallSiteArgumentCount(u.Aux);
                for (var a = 0; a < argc; a++)
                {
                    if (!aliases.Contains(exe.GetCallSiteArgumentValueId(u.Aux, a))) continue;
                    // Only a real Send/SendSelf can be proven non-retaining via the summary; a ctor
                    // arg or a pure-unary-send arg is assumed to retain.
                    if (op is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) return Reject("ctor/unary-arg:" + op);
                    if (summary.SelectorRetains(sel, a + 1, klass)) return Reject("callee-retains:" + state.NameOf(sel) + "#" + (a + 1));
                    // A callee that mutates the arg is fine — it is passed by `ref` and the layout
                    // is marked Mutated (the reify fallback snapshots + copies back). (A)
                    if (summary.SelectorMutates(sel, a + 1, klass)) mutated = true;
                }
            }
        }
        return true;
    }

    // Ivars the defining class's `initialize` sets to a fixnum literal (@x = 3) — statically int,
    // so float speculation must skip them (else a guard misses every call and the method
    // constant-deopts). Cheap one-shot scan of the lowered initialize body.
    internal static HashSet<Symbol> CollectKnownFixnumIvars(MRubyState state, RClass? definingClass)
    {
        var result = new HashSet<Symbol>();
        if (definingClass is null) return result;
        if (!state.TryFindMethod(definingClass, state.Intern("initialize"u8), out var m, out _) ||
            m.Proc is not { } proc)
        {
            return result;
        }
        RubyIRMethod? exe;
        try { exe = RubyIRBuilder.Build(proc.Irep, 0, out _); }
        catch { return result; }
        if (exe is null) return result;
        var ins = exe.Instructions;
        var defIndex = new int[exe.ValueCount];
        for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
        for (var i = 0; i < ins.Length; i++) { var d = ins[i].Dst; if ((uint)d < (uint)defIndex.Length) defIndex[d] = i; }
        for (var i = 0; i < ins.Length; i++)
        {
            if (ins[i].OpCode is not (RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField)) continue;
            var v = ins[i].Src1; // value operand (Src0 is self)
            if ((uint)v < (uint)defIndex.Length && defIndex[v] >= 0 &&
                ins[defIndex[v]].OpCode == RubyIROpCode.LoadValue &&
                exe.GetLiteral(ins[defIndex[v]].Aux).IsFixnum)
            {
                result.Add(exe.GetSymbol(ins[i].Aux));
            }
        }
        return result;
    }



    internal static bool[] ComputeUnboxing(MRubyState state, RubyIRMethod exe, int argCount, ScalarContext? sc, out bool[] floatTaintOut, out bool[] isDoubleOut, out bool[] provesDoubleOut, out bool[] soundProvenOut, IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry = null, HashSet<Symbol>? knownFixnumIvars = null, bool classUsesFloat = true, RClass? definingClass = null)
    {
        var n = exe.ValueCount;
        var isLong = new bool[n];
        var hasDef = new bool[n];
        var nonArithDef = new bool[n];
        var boxedUse = new bool[n];
        var floatTaint = new bool[n];
        floatTaintOut = floatTaint;
        var ins = exe.Instructions;
        void MarkBoxed(int id)
        {
            if ((uint)id < (uint)n) boxedUse[id] = true;
        }
        void Taint(int id)
        {
            if ((uint)id < (uint)n) floatTaint[id] = true;
        }
        for (var i = 0; i < ins.Length; i++)
        {
            var op = ins[i].OpCode;
            var d = ins[i].Dst;
            if ((uint)d < (uint)n)
            {
                hasDef[d] = true;
                if (!RubyIROpInfo.IsFixnumArith(op)) nonArithDef[d] = true;
            }
            // Float-taint seeds: a float literal, a float-valued constant, or a send whose
            // result is always a float.
            if (op == RubyIROpCode.LoadValue && exe.GetLiteral(ins[i].Aux).IsFloat)
            {
                Taint(d);
            }
            else if (op == RubyIROpCode.GetConstant && IsFloatConstantName(state, exe.GetSymbol(ins[i].Aux)))
            {
                Taint(d);
            }
            else if ((op is RubyIROpCode.Send or RubyIROpCode.SendSelf) &&
                     IsFloatReturningMethod(state, exe.GetCallSiteSymbol(ins[i].Aux)))
            {
                Taint(d);
            }
            // Arith/compare read their operands as fixnums -> not a boxed use.
            if (RubyIROpInfo.IsFixnumArith(op) || RubyIROpInfo.IsFixnumCompare(op))
            {
                continue;
            }
            // Return reads its operand at a re-box boundary (BoxReadFull handles any unboxed kind),
            // so it does NOT force the value boxed — letting a returned double accumulator stay a
            // raw double across the method and re-box only here.
            if (op is RubyIROpCode.Return or RubyIROpCode.ReturnValue)
            {
                continue;
            }
            // Any other instruction consumes its value operands boxed. Over-marking a
            // non-operand source field is harmless (only loses an unboxing opportunity).
            MarkBoxed(ins[i].Src0);
            MarkBoxed(ins[i].Src1);
            MarkBoxed(ins[i].Src2);
            if (RubyIROpInfo.IsSendOp(op) || op == RubyIROpCode.PureUnarySend || op == RubyIROpCode.VirtualNew)
            {
                var argc = exe.GetCallSiteArgumentCount(ins[i].Aux);
                for (var a = 0; a < argc; a++) MarkBoxed(exe.GetCallSiteArgumentValueId(ins[i].Aux, a));
            }
            else if (op is RubyIROpCode.NewArray or RubyIROpCode.NewArray2 or RubyIROpCode.NewHash)
            {
                var c = exe.GetOperandListCount(ins[i].Aux);
                for (var a = 0; a < c; a++) MarkBoxed(exe.GetOperandListValueId(ins[i].Aux, a));
            }
        }
        // Propagate float taint forward through Move + arith to a fixpoint. The IR has
        // forward branches (loops via goto), so a single pass can miss back-edge feeders;
        // iterate until stable (bounded by value count, in practice 2-3 passes). Scalar-
        // replaced object fields are folded in: a field is float-typed if any value written
        // to it (ctor arg / setter arg) is tainted, and a getter read of a float field taints
        // its result — so arith over `o.x` is not wrongly unboxed to long and deopting on float.
        bool Tainted(int id) => (uint)id < (uint)n && floatTaint[id];
        var floatFields = new HashSet<(int Obj, int Field)>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < ins.Length; i++)
            {
                var op = ins[i].OpCode;
                var d = ins[i].Dst;

                if (sc is not null && op == RubyIROpCode.VirtualNew && sc.IsScalar(d))
                {
                    var o = sc.GetScalarObject(d);
                    for (var f = 0; f < o.Fields.Count; f++)
                    {
                        var argVid = exe.GetCallSiteArgumentValueId(ins[i].Aux, o.FieldArg[f]);
                        if (Tainted(argVid) && floatFields.Add((d, f))) changed = true;
                    }
                    continue;
                }
                if (sc is not null && op == RubyIROpCode.Send &&
                    sc.TryGetAccessorSend(exe, ins[i], out var objId, out var fieldIdx, out var isSetter))
                {
                    if (isSetter)
                    {
                        var valVid = exe.GetCallSiteArgumentValueId(ins[i].Aux, 0);
                        if (Tainted(valVid))
                        {
                            if (floatFields.Add((objId, fieldIdx))) changed = true;
                            if ((uint)d < (uint)n && !floatTaint[d]) { floatTaint[d] = true; changed = true; }
                        }
                    }
                    else if (floatFields.Contains((objId, fieldIdx)) && (uint)d < (uint)n && !floatTaint[d])
                    {
                        floatTaint[d] = true; changed = true;
                    }
                    continue;
                }
                if (sc is not null &&
                    sc.TryGetScalarFieldAccess(exe, ins[i], out var fieldObjId, out var scalarFieldIdx, out var isFieldSetter))
                {
                    if (isFieldSetter)
                    {
                        if (Tainted(ins[i].Src1) && floatFields.Add((fieldObjId, scalarFieldIdx)))
                        {
                            changed = true;
                        }
                    }
                    else if (floatFields.Contains((fieldObjId, scalarFieldIdx)) &&
                             (uint)d < (uint)n &&
                             !floatTaint[d])
                    {
                        floatTaint[d] = true;
                        changed = true;
                    }
                    continue;
                }

                if ((uint)d >= (uint)n || floatTaint[d])
                {
                    continue;
                }
                bool propagate = op switch
                {
                    RubyIROpCode.Move => Tainted(ins[i].Src0),
                    _ when RubyIROpInfo.IsFixnumArith(op) => Tainted(ins[i].Src0) || Tainted(ins[i].Src1) || Tainted(ins[i].Src2),
                    _ => false,
                };
                if (propagate)
                {
                    floatTaint[d] = true;
                    changed = true;
                }
            }
        }
        // ---- double-unboxing (the float twin of long-unboxing) ----
        // provesDouble[id] = id is PROVABLY a Float at runtime (a MUST analysis): seeded by float
        // literals / float constants / float-returning sends, then propagated through float arith
        // ONLY when every operand is itself provably double (intersection, vs taint's union).
        var provesDouble = new bool[n];
        provesDoubleOut = provesDouble;
        // soundProven[id] = provesDouble[id] was established by an UNGUARDED whole-program proof
        // (a float literal/const, a proven-Float send, or — new in Stage 1 — a class-wide-proven
        // Float ivar read), as opposed to the speculation block below which guesses + deopts. The
        // float-speculation guard (EmitFloatSpeculationGuard) is suppressed for sound dsts.
        var soundProven = new bool[n];
        soundProvenOut = soundProven;
        bool PD(int id) => (uint)id < (uint)n && provesDouble[id];
        for (var i = 0; i < ins.Length; i++)
        {
            var op = ins[i].OpCode;
            var d = ins[i].Dst;
            if ((uint)d >= (uint)n) continue;
            if ((op == RubyIROpCode.LoadValue && exe.GetLiteral(ins[i].Aux).IsFloat) ||
                (op == RubyIROpCode.GetConstant && IsFloatConstantName(state, exe.GetSymbol(ins[i].Aux))) ||
                ((op is RubyIROpCode.Send or RubyIROpCode.SendSelf) && IsFloatReturningMethod(state, exe.GetCallSiteSymbol(ins[i].Aux))))
            {
                provesDouble[d] = true;
                soundProven[d] = true; // a proven/builtin Float source needs no runtime guard
            }
        }

        // Stage 1 — SOUND Float unboxing from whole-program inference (env AOT_NOIVKIND, default ON).
        // An ivar read whose (definingClass, @name) is proven Float across the WHOLE program is a
        // Float at runtime WITHOUT a guard (vs the speculation block below, which guards + deopts).
        // Seeding provesDouble here makes downstream float arith read it via .FloatValue / d{} with
        // no `if(!v.IsFloat) return false`. Self ivar reads use definingClass (exactly right);
        // sends are already covered by the proven-Float seed above (selectorReturn is receiver-class
        // independent, so a per-class ivar lookup for accessor sends would be unsound — not done).
        var rt = Mrb2CsCompiler.CurrentReturnTypes;
        if (rt is not null && definingClass is not null && Environment.GetEnvironmentVariable("AOT_NOIVKIND") != "1")
        {
            for (var i = 0; i < ins.Length; i++)
            {
                var op = ins[i].OpCode;
                var d = ins[i].Dst;
                if ((uint)d >= (uint)n || provesDouble[d]) continue;
                if (op is RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField &&
                    rt.IvarReturnsFloat(definingClass, exe.GetSymbol(ins[i].Aux)))
                {
                    provesDouble[d] = true;
                    soundProven[d] = true;
                }
            }
        }

        // Q1.2 speculative float ivars (AOT_SPECFLOAT): ao's hot float arith is over UNTYPED ivars
        // (@x*b.x+...), which carry no static float proof. Speculate that an ivar/accessor read
        // feeding only float-capable arith is a Float — seed it provesDouble (a runtime IsFloat
        // guard at the read; a miss deopts). A deopt re-runs the bytecode from the method's top,
        // so it is safe ONLY if no observable side effect was committed before the read.
        //
        // Pre-side-effect window: instead of requiring the WHOLE method side-effect-free, speculate
        // only reads BEFORE the method's first COMMITTED side effect. The IR is loop-free (forward
        // branches only), so instruction-index order is execution order — a read at index i with no
        // committed side effect at index < i is deopt-safe. Transparent ops (writes to a freshly-
        // allocated scalar object, fresh allocations, pure float-Math sends, getters) don't commit
        // and don't close the window; a write to self/a pre-existing object or an arbitrary call
        // does. This reaches side-effecting hot methods (e.g. ao's intersect: the discriminant is
        // all computed before the @t/@hit setters) once their float producers (vdot) are inlined.
        if (knownFixnumIvars is not null && classUsesFloat && Environment.GetEnvironmentVariable("AOT_NOSPECFLOAT") != "1")
        {
            // The pre-side-effect WINDOW (SSA path) speculates prefixes of side-effecting methods,
            // which pays once float producers are inlined. With AOT_NOSSA the legacy whole-method
            // gate applies instead (any committed side effect disables speculation). Either way the
            // outer class-float-evidence guard already kept this off all-integer classes.
            int firstSideEffect;
            if (Mrb2CsCompiler.SsaEnabled)
            {
                firstSideEffect = ins.Length;
                for (var i = 0; i < ins.Length; i++)
                {
                    if (IsCommittedSideEffect(state, exe, sc, accessorRegistry, ins[i])) { firstSideEffect = i; break; }
                }
            }
            else
            {
                var sideEffectFree = true;
                for (var i = 0; i < ins.Length && sideEffectFree; i++)
                {
                    var op = ins[i].OpCode;
                    if (op is RubyIROpCode.Send or RubyIROpCode.SendSelf)
                    {
                        var sel = exe.GetCallSiteSymbol(ins[i].Aux);
                        if (!((accessorRegistry?.ContainsKey(sel) ?? false) || IsFloatReturningMethod(state, sel)))
                            sideEffectFree = false;
                    }
                    else if (!RubyIROpInfo.IsPureSpeculationOp(op))
                    {
                        sideEffectFree = false;
                    }
                }
                firstSideEffect = sideEffectFree ? ins.Length : 0;
            }

            // A read is speculatable only if every use is a float-capable arith/compare operand
            // (not a fixnum-typed/bitwise/index op, not a boxed use) — so integer ivars used in
            // `&`/`<<`/`[]` are never mis-speculated.
            var nonFloatCapableUse = new bool[n];
            void NFC(int id) { if ((uint)id < (uint)n) nonFloatCapableUse[id] = true; }
            for (var i = 0; i < ins.Length; i++)
            {
                var op = ins[i].OpCode;
                if (RubyIROpInfo.IsDoubleArith(op) || RubyIROpInfo.IsDoubleCompare(op)) continue;
                NFC(ins[i].Src0); NFC(ins[i].Src1); NFC(ins[i].Src2);
                if (RubyIROpInfo.IsSendOp(op) || op == RubyIROpCode.PureUnarySend || op == RubyIROpCode.VirtualNew)
                {
                    var argc = exe.GetCallSiteArgumentCount(ins[i].Aux);
                    for (var a = 0; a < argc; a++) NFC(exe.GetCallSiteArgumentValueId(ins[i].Aux, a));
                }
            }
            for (var i = 0; i < firstSideEffect; i++)
            {
                var op = ins[i].OpCode;
                var d = ins[i].Dst;
                if ((uint)d >= (uint)n || provesDouble[d] || boxedUse[d] || nonFloatCapableUse[d]) continue;
                var isRead = op is RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField;
                var isAccessor = (op is RubyIROpCode.Send or RubyIROpCode.SendSelf) &&
                                 (accessorRegistry?.ContainsKey(exe.GetCallSiteSymbol(ins[i].Aux)) ?? false);
                if (!isRead && !isAccessor) continue;
                // Don't speculate an ivar the defining class's initialize sets to a fixnum
                // literal (e.g. Point#@x = 3, or optcarrot counters @x = 0): those are int, so
                // a float guard would miss every call and the method would constant-deopt.
                var ivarName = isRead
                    ? exe.GetSymbol(ins[i].Aux)
                    : accessorRegistry![exe.GetCallSiteSymbol(ins[i].Aux)].Field;
                if (knownFixnumIvars?.Contains(ivarName) ?? false) continue;
                provesDouble[d] = true; // speculate Float; guard emitted at the read site
            }
        }

        // Def counts (all Dst occurrences; branches/stores write Dst 0 == self, irrelevant). A
        // value-id with exactly one def is single-assignment, so a Move into it is a pure copy.
        var defCount = new int[n];
        for (var i = 0; i < ins.Length; i++)
        {
            var d = ins[i].Dst;
            if ((uint)d < (uint)n) defCount[d]++;
        }

        changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < ins.Length; i++)
            {
                var op = ins[i].OpCode;
                var d = ins[i].Dst;
                if ((uint)d >= (uint)n || provesDouble[d]) continue;
                bool prove;
                if (RubyIROpInfo.IsDoubleArith(op))
                {
                    prove = PD(ins[i].Src0) && PD(ins[i].Src1) && (!RubyIROpInfo.IsDoubleFused(op) || PD(ins[i].Src2));
                }
                else if (op == RubyIROpCode.Move && defCount[d] == 1)
                {
                    // Single-def copy: provably double iff its source is (MUST holds — one def).
                    // Carries the proof across the copy chains SSA renumbering introduces, so an
                    // inlined float producer's result (e.g. ao's `b = rs.vdot(dir)`) stays proven
                    // double through the merge into `b` instead of relapsing to a fixnum isLong.
                    prove = PD(ins[i].Src0);
                }
                else
                {
                    continue;
                }
                if (prove)
                {
                    provesDouble[d] = true;
                    changed = true;
                }
            }
        }

        // isLong is computed AFTER provesDouble (incl. speculation) and excludes it: a provably/
        // speculated Float value must never be treated as a fixnum long (else its arith deopts on
        // the first float, or — under speculation — its accumulator is read as an uninitialized l{}).
        for (var id = argCount + 1; id < n; id++)
        {
            isLong[id] = hasDef[id] && !nonArithDef[id] && !boxedUse[id] && !floatTaint[id] && !provesDouble[id];
        }
        // Registers captured by a descendant block are passed to the inlined block by ref, so
        // they must be boxed MRubyValue locals (not unboxed longs).
        foreach (var captured in exe.ClosureCapturedValueIds)
        {
            if ((uint)captured < (uint)n) isLong[captured] = false;
        }

        // A value is held as a raw `double` iff it is provably double, DEFINED by a double-arith op
        // (seeds stay boxed and are read via .FloatValue), and used ONLY as an operand of a
        // pure-double op (all operands provably double). Any other use (Move, immediate, fixnum-
        // typed arith, Send arg, ivar store, return, ...) is "impure" and keeps the value boxed —
        // mirroring long-unboxing's boxedUse rule, so non-pure emission never sees a `d{}` local.
        var doubleDef = new bool[n];
        var impureUse = new bool[n];
        void Impure(int id) { if ((uint)id < (uint)n) impureUse[id] = true; }
        for (var i = 0; i < ins.Length; i++)
        {
            var op = ins[i].OpCode;
            var d = ins[i].Dst;
            if (RubyIROpInfo.IsDoubleArith(op) && (uint)d < (uint)n && provesDouble[d]) doubleDef[d] = true;
            if (!RubyIROpInfo.IsFixnumArith(op) && !RubyIROpInfo.IsFixnumCompare(op)) continue; // non-arith operands already boxedUse-marked
            var fused = RubyIROpInfo.IsDoubleFused(op);
            var pure = (RubyIROpInfo.IsDoubleArith(op) || RubyIROpInfo.IsDoubleCompare(op)) &&
                       PD(ins[i].Src0) && PD(ins[i].Src1) && (!fused || PD(ins[i].Src2));
            if (!pure) { Impure(ins[i].Src0); Impure(ins[i].Src1); if (fused) Impure(ins[i].Src2); }
        }
        var isDouble = new bool[n];
        isDoubleOut = isDouble;
        for (var id = argCount + 1; id < n; id++)
        {
            isDouble[id] = provesDouble[id] && doubleDef[id] && !boxedUse[id] && !impureUse[id] && !isLong[id];
        }
        foreach (var captured in exe.ClosureCapturedValueIds)
        {
            if ((uint)captured < (uint)n) isDouble[captured] = false;
        }
        return isLong;
    }

    // --- Phase 2: sound numeric type inference for LOOPING methods (cyclic dataflow) ---
    // The merge-slot loop IR gives each register ONE value-id with MULTIPLE defs (pre-loop init +
    // back-edge update), so the acyclic ComputeUnboxing above is unsound here (it seeds provesDouble
    // from a single def). This computes a per-id MUST type as a meet (over ALL defs) to a fixpoint,
    // so a value is proven Float/Fixnum only if EVERY def produces that type. Mixed int/float arith
    // is typed by Ruby semantics (Float op Numeric -> Float). Numerically-used method ARGS are
    // speculated Fixnum (guarded at entry, before any side effect -> deopt-safe; see argGuardsOut).
    //
    // Lattice (meet = "could be either def's type", monotone toward UNK so it terminates):
    //   TOP(unseen) -> {FIX, FLT} -> NUM(fix|flt) -> UNK(boxed/non-numeric).
    const byte TyTop = 0, TyFix = 1, TyFlt = 2, TyNum = 3, TyUnk = 4;

    static byte MeetTy(byte a, byte b)
    {
        if (a == TyTop) return b;
        if (b == TyTop) return a;
        if (a == b) return a;
        if (a == TyUnk || b == TyUnk) return TyUnk;
        // a,b in {FIX,FLT,NUM}, a!=b -> all numeric, so NUM
        return TyNum;
    }

    // Result type of `a OP b` (+,-,*,/) under Ruby coercion. Div is the same shape (Fixnum/Fixnum is
    // integer division -> Fixnum; any Float operand -> Float).
    static byte ArithTy(byte a, byte b)
    {
        if (a == TyTop || b == TyTop) return TyTop;          // defer until operands known
        if (a == TyUnk || b == TyUnk) return TyUnk;          // a non-numeric operand poisons
        if (a == TyFlt || b == TyFlt) return TyFlt;          // float op numeric -> float
        if (a == TyFix && b == TyFix) return TyFix;          // fixnum op fixnum -> fixnum
        return TyNum;                                          // involves a NUM, no float forcing
    }

    // Sound MUST numeric typing for a looping method. Outputs provesDouble (always Float) and
    // provesFixnum (always Fixnum, modulo fixnum-overflow->Bignum, matching the existing isLong
    // stance). floatTaint/soundProven are set consistently (no speculation guard for proven floats).
    // argGuardsOut: arg value-ids to guard `IsFixnum` at method entry (their typing assumed Fixnum).
    internal static void ComputeLoopUnboxing(
        MRubyState state, RubyIRMethod exe, int argCount, RClass? definingClass,
        out bool[] provesDouble, out bool[] provesFixnum, out bool[] floatTaint,
        out bool[] soundProven, out List<int> argGuardsOut, bool speculateArgs = true)
    {
        var n = exe.ValueCount;
        var ins = exe.Instructions;
        var ty = new byte[n];            // TyTop initially (0)
        var rt = Mrb2CsCompiler.CurrentReturnTypes;

        // Args whose value REACHES a numeric-arith operand (directly, or through Move copies — the
        // merge-slot IR loads operands into temps before arith) are speculated Fixnum (entry-guarded).
        // feedsArith is seeded at arith operands and propagated BACKWARD through `dst = Move(src)`.
        var feedsArith = new bool[n];
        void Feed(int id) { if ((uint)id < (uint)n) feedsArith[id] = true; }
        for (var i = 0; i < ins.Length; i++)
        {
            var op = ins[i].OpCode;
            if (RubyIROpInfo.IsFixnumArith(op) || RubyIROpInfo.IsFixnumCompare(op))
            {
                Feed(ins[i].Src0);
                Feed(ins[i].Src1);
                if (RubyIROpInfo.IsDoubleFused(op)) Feed(ins[i].Src2);
            }
        }
        bool feedChanged = true;
        var feedGuard = 0;
        while (feedChanged && feedGuard++ <= n + 4)
        {
            feedChanged = false;
            for (var i = 0; i < ins.Length; i++)
            {
                if (ins[i].OpCode != RubyIROpCode.Move) continue;
                var d = ins[i].Dst;
                var s = ins[i].Src0;
                if ((uint)d < (uint)n && feedsArith[d] && (uint)s < (uint)n && !feedsArith[s])
                {
                    feedsArith[s] = true;
                    feedChanged = true;
                }
            }
        }
        var argGuards = new List<int>();
        for (var v = 1; v <= argCount && v < n; v++)
        {
            // Block bodies (speculateArgs=false) can't guard args at entry — a block deopt re-runs
            // the PARENT method, double-applying prior loop iterations' side effects. So their args
            // stay boxed (Unknown) rather than speculated Fixnum.
            if (speculateArgs && feedsArith[v]) { ty[v] = TyFix; argGuards.Add(v); }
            else ty[v] = TyUnk;
        }
        ty[0] = TyUnk; // self
        var isArgOrSelf = new bool[n];
        for (var v = 0; v <= argCount && v < n; v++) isArgOrSelf[v] = true;

        // DefType: the type a single instruction's def produces (reads current operand types).
        byte DefTy(in RubyIRInstruction ix)
        {
            var op = ix.OpCode;
            switch (op)
            {
                case RubyIROpCode.LoadValue:
                {
                    var lit = exe.GetLiteral(ix.Aux);
                    return lit.IsFixnum ? TyFix : lit.IsFloat ? TyFlt : TyUnk;
                }
                case RubyIROpCode.GetConstant:
                    return IsFloatConstantName(state, exe.GetSymbol(ix.Aux)) ? TyFlt : TyUnk;
                case RubyIROpCode.Move:
                    return (uint)ix.Src0 < (uint)n ? ty[ix.Src0] : TyUnk;
                case RubyIROpCode.Send:
                    if (IsFloatReturningMethod(state, exe.GetCallSiteSymbol(ix.Aux))) return TyFlt;
                    // Fixnum bitwise sends `& | ^ >>` produce a Fixnum when both operands are Fixnum.
                    // `<<` is EXCLUDED here — it can overflow to Bignum, so its result isn't a proven
                    // Fixnum (it still gets the boxed C# fast path, just not raw-long typing).
                    if (exe.GetCallSiteArgumentCount(ix.Aux) == 1 &&
                        Emitter.TryFixnumBitwiseOp(state, exe.GetCallSiteSymbol(ix.Aux), out var bop, out _) && bop != "<<")
                    {
                        var rcv = ix.Src0;
                        var arg = exe.GetCallSiteArgumentValueId(ix.Aux, 0);
                        var rt2 = (uint)rcv < (uint)n ? ty[rcv] : TyUnk;
                        var at2 = (uint)arg < (uint)n ? ty[arg] : TyUnk;
                        if (rt2 == TyTop || at2 == TyTop) return TyTop; // defer until operands known
                        var rfix = rt2 == TyFix || Mrb2CsCompiler.ConstFix(rcv, out _);
                        var afix = at2 == TyFix || Mrb2CsCompiler.ConstFix(arg, out _);
                        return rfix && afix ? TyFix : TyUnk;
                    }
                    return TyUnk;
                case RubyIROpCode.SendSelf:
                    return IsFloatReturningMethod(state, exe.GetCallSiteSymbol(ix.Aux)) ? TyFlt : TyUnk;
                case RubyIROpCode.GetInstanceVariable:
                case RubyIROpCode.VirtualGetField:
                    return rt is not null && definingClass is not null &&
                           rt.IvarReturnsFloat(definingClass, exe.GetSymbol(ix.Aux)) ? TyFlt : TyUnk;
            }
            if (RubyIROpInfo.IsFixnumArith(op))
            {
                var a = (uint)ix.Src0 < (uint)n ? ty[ix.Src0] : TyUnk;
                // Immediate ops carry their constant in Aux (a fixnum for AddImmediate; the float
                // variants are explicitly float); a plain binary reads Src1.
                byte b;
                if (op is RubyIROpCode.AddImmediate or RubyIROpCode.SubImmediate
                       or RubyIROpCode.AddImmediateFixnum or RubyIROpCode.SubImmediateFixnum)
                    b = TyFix;
                else if (op is RubyIROpCode.AddImmediateFloat or RubyIROpCode.SubImmediateFloat)
                    b = TyFlt;
                else
                    b = (uint)ix.Src1 < (uint)n ? ty[ix.Src1] : TyUnk;
                var r = ArithTy(a, b);
                if (RubyIROpInfo.IsDoubleFused(op))
                {
                    var c = (uint)ix.Src2 < (uint)n ? ty[ix.Src2] : TyUnk;
                    r = ArithTy(r, c);
                }
                return r;
            }
            // Comparisons produce a boolean (non-numeric); everything else is opaque.
            return TyUnk;
        }

        // Fixpoint: ty[id] = meet over all defs of DefTy(def). Args/self are fixed (not re-meet).
        bool changed = true;
        var iterGuard = 0;
        while (changed && iterGuard++ <= n + 4)
        {
            changed = false;
            var acc = new byte[n];        // TyTop
            var seen = new bool[n];
            for (var i = 0; i < ins.Length; i++)
            {
                var d = ins[i].Dst;
                if ((uint)d >= (uint)n || isArgOrSelf[d]) continue;
                if (!Definecheck(ins[i].OpCode)) continue;
                var dt = DefTy(ins[i]);
                acc[d] = seen[d] ? MeetTy(acc[d], dt) : dt;
                seen[d] = true;
            }
            for (var v = 0; v < n; v++)
            {
                if (isArgOrSelf[v]) continue;
                var nt = seen[v] ? acc[v] : TyUnk; // no def -> boxed
                if (nt != ty[v]) { ty[v] = nt; changed = true; }
            }
        }

        provesDouble = new bool[n];
        provesFixnum = new bool[n];
        floatTaint = new bool[n];
        soundProven = new bool[n];
        for (var v = 0; v < n; v++)
        {
            if (ty[v] == TyFlt) { provesDouble[v] = true; floatTaint[v] = true; soundProven[v] = true; }
            else if (ty[v] == TyFix) { provesFixnum[v] = true; }
        }
        argGuardsOut = argGuards;
    }

    // Phase 3: pick which proven Float/Fixnum loop value-ids can live in a RAW `double`/`long` local
    // (FP/int register) instead of a boxed-but-guard-free MRubyValue. A value is raw-eligible iff it
    // is never used in a boxed position (Send arg / ivar store / branch cond / index / non-numeric
    // op), never used by a DUAL-path numeric op (whose float branch reads `v.FloatValue` and so needs
    // the boxed form), and every def writes a raw form (fully-typed arith / immediate / Move /
    // numeric LoadValue). Move and Return convert at the boundary, so they don't force boxing.
    internal static void ComputeLoopRawLocals(
        MRubyState state, RubyIRMethod exe, int argCount, bool[] provesDouble, bool[] provesFixnum,
        out bool[] isLong, out bool[] isDouble)
    {
        var ins = exe.Instructions;
        var nI = ins.Length;
        var n = exe.ValueCount;
        isLong = new bool[n];
        isDouble = new bool[n];
        var boxedUse = new bool[n];
        var impureUse = new bool[n];   // operand of a dual-path numeric op -> needs boxed storage
        var rawDefBad = new bool[n];   // a def writes a boxed v{} (so the id can't be a raw local)
        var hasDef = new bool[n];

        bool ProvenNum(int id)
        {
            if ((uint)id < (uint)n && (provesDouble[id] || provesFixnum[id])) return true;
            return Mrb2CsCompiler.ConstFix(id, out _) || Mrb2CsCompiler.ConstFloat(id, out _);
        }

        var tmp = new ulong[(n + 63) >> 6];
        for (var i = 0; i < nI; i++)
        {
            var op = ins[i].OpCode;
            var d = ins[i].Dst;
            var defines = Definecheck(op) && (uint)d < (uint)n;
            if (defines) hasDef[d] = true;

            if (RubyIROpInfo.IsFixnumArith(op) || RubyIROpInfo.IsFixnumCompare(op))
            {
                int o0 = ins[i].Src0, o1 = -1, o2 = -1;
                var imm = op is RubyIROpCode.AddImmediate or RubyIROpCode.SubImmediate
                    or RubyIROpCode.AddImmediateFixnum or RubyIROpCode.SubImmediateFixnum;
                if (!imm) o1 = ins[i].Src1;
                if (RubyIROpInfo.IsDoubleFused(op)) o2 = ins[i].Src2;
                var fullyTyped = ProvenNum(o0) && (o1 < 0 || ProvenNum(o1)) && (o2 < 0 || ProvenNum(o2));
                if (!fullyTyped)
                {
                    if ((uint)o0 < (uint)n) impureUse[o0] = true;
                    if (o1 >= 0 && (uint)o1 < (uint)n) impureUse[o1] = true;
                    if (o2 >= 0 && (uint)o2 < (uint)n) impureUse[o2] = true;
                    if (defines) rawDefBad[d] = true;      // dual path writes a boxed v{d}
                }
                // A float-receiver immediate (`f + 1` -> Float) is emitted boxed: EmitFixnumImmediate
                // reads its receiver via FloatRead (= v{}.FloatValue, NOT rep-aware) and writes a
                // boxed v{dst}. So both the float operand and the dst must stay boxed (the fixnum
                // immediate path IS rep-aware via FixRead/AssignFix, so isLong operands are fine).
                else if (imm && (uint)o0 < (uint)n && provesDouble[o0])
                {
                    impureUse[o0] = true;
                    if (defines) rawDefBad[d] = true;
                }
                continue;
            }
            if (op == RubyIROpCode.Move) continue;          // rep-aware Move converts at the assignment
            if (op is RubyIROpCode.Return or RubyIROpCode.ReturnValue or RubyIROpCode.ReturnSelf) continue;
            if (op == RubyIROpCode.LoadValue)
            {
                var lit = exe.GetLiteral(ins[i].Aux);
                if (!lit.IsFixnum && !lit.IsFloat && defines) rawDefBad[d] = true; // non-numeric -> boxed
                continue;                                    // no value operands
            }
            // Fixnum bitwise send (& | ^ >>): its operands are read via FixRead (rep-aware, under a
            // guard) so they never force boxing; the dst is a raw long iff both operands are proven
            // Fixnum (then the codegen emits l{} = ...), else it stays boxed.
            if (op == RubyIROpCode.Send && exe.GetCallSiteArgumentCount(ins[i].Aux) == 1 &&
                Emitter.TryFixnumBitwiseOp(state, exe.GetCallSiteSymbol(ins[i].Aux), out var bwOp, out _))
            {
                var rcv = ins[i].Src0;
                var arg = exe.GetCallSiteArgumentValueId(ins[i].Aux, 0);
                var rfix = ((uint)rcv < (uint)n && provesFixnum[rcv]) || Mrb2CsCompiler.ConstFix(rcv, out _);
                var afix = ((uint)arg < (uint)n && provesFixnum[arg]) || Mrb2CsCompiler.ConstFix(arg, out _);
                // A non-fixnum operand (Unknown / proven Float) must stay BOXED: FixRead reads it as
                // v{}.FixnumValue (under a guard), which has no raw l{}/d{} form. Only proven-fixnum
                // operands stay raw long.
                if (!rfix && (uint)rcv < (uint)n) impureUse[rcv] = true;
                if (!afix && (uint)arg < (uint)n) impureUse[arg] = true;
                // `<<` result can be Bignum -> dst must stay boxed; `& | ^ >>` give a raw-long dst iff
                // both operands are proven fixnum.
                if ((bwOp == "<<" || !(rfix && afix)) && defines) rawDefBad[d] = true;
                continue;
            }
            // Any other op consumes its value operands boxed and (if it defines) writes a boxed v{d}.
            Array.Clear(tmp, 0, tmp.Length);
            CollectUses(exe, ins[i], tmp, n);
            for (var w = 0; w < tmp.Length; w++)
            {
                var b = tmp[w];
                while (b != 0)
                {
                    var bit = b & (ulong)(-(long)b);
                    var id = (w << 6) + Log2Floor(bit);
                    b ^= bit;
                    if ((uint)id < (uint)n) boxedUse[id] = true;
                }
            }
            if (defines) rawDefBad[d] = true;
        }

        for (var id = argCount + 1; id < n; id++)
        {
            if (!hasDef[id] || boxedUse[id] || impureUse[id] || rawDefBad[id]) continue;
            if (provesDouble[id]) isDouble[id] = true;
            else if (provesFixnum[id]) isLong[id] = true;
        }
        // Captured registers are passed to inlined blocks by ref as boxed MRubyValue -> never raw.
        foreach (var c in exe.ClosureCapturedValueIds)
            if ((uint)c < (uint)n) { isLong[c] = false; isDouble[c] = false; }
    }

    // An opcode that defines a value (mirrors the producing cases the lattice models). A non-defining
    // op (branch/store/return) contributes no def to the meet.
    static bool Definecheck(RubyIROpCode op) => op switch
    {
        RubyIROpCode.Jump or RubyIROpCode.JumpIfTruthy or RubyIROpCode.JumpIfFalsy or
        RubyIROpCode.JumpIfNil or RubyIROpCode.GuardInlineClass or RubyIROpCode.Return or
        RubyIROpCode.ReturnSelf or RubyIROpCode.ReturnValue or RubyIROpCode.CheckArity or
        RubyIROpCode.SetUpVar or RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField or
        RubyIROpCode.ArraySet => false,
        _ => true,
    };

    // --- generated-local coalescing (source-size only; the C# JIT register-allocates anyway) ---
    // SSA renumbering gives every definition a fresh value-id, so a method can declare dozens of
    // short-lived single-use temps. This maps each value-id to a representative "local id", reusing
    // one C# local for value-ids that never live at the same time AND share a representation
    // (boxed / long / double). Params (v0..vArg) and captured ids keep their own id (they are method
    // parameters / by-ref cells). Returns id->repId, or null to leave naming unchanged.
    internal static int[]? CoalesceLocals(RubyIRMethod ir, int argCount, bool[] isLong, bool[] isDouble,
        Dictionary<int, (int Canon, int Size)>? scalarArrays = null,
        Dictionary<int, (int Canon, HashSet<string> Keys)>? scalarHashes = null)
    {
        var ins = ir.Instructions;
        var n = ins.Length;
        var v = ir.ValueCount;
        if (n == 0 || v == 0) return null;

        var kept = new bool[v];
        for (var i = 0; i <= argCount && i < v; i++) kept[i] = true;
        foreach (var c in ir.ClosureCapturedValueIds) if (c < v) kept[c] = true;
        // Scalar-replaced array value-ids never become a v{} local (they're element locals), so keep
        // them out of the coalescing pool — they must never be chosen as a shared representative.
        if (scalarArrays is not null) foreach (var id in scalarArrays.Keys) if ((uint)id < (uint)v) kept[id] = true;
        if (scalarHashes is not null) foreach (var id in scalarHashes.Keys) if ((uint)id < (uint)v) kept[id] = true;

        // Successor CFG (branch targets are instruction indices; index n is the trailing end label).
        var succ = new List<int>[n];
        for (var i = 0; i < n; i++) succ[i] = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var op = ins[i].OpCode;
            switch (op)
            {
                case RubyIROpCode.Return:
                case RubyIROpCode.ReturnSelf:
                case RubyIROpCode.ReturnValue:
                    break;
                case RubyIROpCode.Jump:
                    if (ins[i].Aux < n) succ[i].Add(ins[i].Aux);
                    break;
                case RubyIROpCode.JumpIfTruthy:
                case RubyIROpCode.JumpIfFalsy:
                case RubyIROpCode.JumpIfNil:
                case RubyIROpCode.GuardInlineClass:
                    if (ins[i].Aux < n) succ[i].Add(ins[i].Aux);
                    if (i + 1 < n) succ[i].Add(i + 1);
                    break;
                default:
                    if (i + 1 < n) succ[i].Add(i + 1);
                    break;
            }
        }

        var words = (v + 63) >> 6;
        var use = new ulong[n][];
        var defId = new int[n];
        for (var i = 0; i < n; i++)
        {
            use[i] = new ulong[words];
            defId[i] = Definecheck(ins[i].OpCode) && ins[i].Dst < v ? ins[i].Dst : -1;
            CollectUses(ir, ins[i], use[i], v);
        }

        // Backward liveness to a fixpoint (reverse-order passes; loops converge in a few rounds).
        var liveOut = new ulong[n][];
        for (var i = 0; i < n; i++) liveOut[i] = new ulong[words];
        var liveIn = new ulong[n][];
        for (var i = 0; i < n; i++) liveIn[i] = new ulong[words];
        var changed = true;
        var guard = 0;
        while (changed && guard++ <= n + 8)
        {
            changed = false;
            for (var i = n - 1; i >= 0; i--)
            {
                var outI = liveOut[i];
                foreach (var s in succ[i])
                    for (var w = 0; w < words; w++) outI[w] |= liveIn[s][w];
                // liveIn = (liveOut \ def) | use
                var inI = liveIn[i];
                var d = defId[i];
                for (var w = 0; w < words; w++)
                {
                    var nv = outI[w];
                    if (d >= 0 && w == d >> 6) nv &= ~(1UL << (d & 63));
                    nv |= use[i][w];
                    if (nv != inI[w]) { inI[w] = nv; changed = true; }
                }
            }
        }

        // Interference: a def interferes with everything live just after it (its live-out).
        var adj = new ulong[v][];
        for (var i = 0; i < v; i++) adj[i] = new ulong[words];
        for (var i = 0; i < n; i++)
        {
            var d = defId[i];
            if (d < 0) continue;
            var outI = liveOut[i];
            for (var w = 0; w < words; w++)
            {
                var bits = outI[w];
                while (bits != 0)
                {
                    var bit = bits & (ulong)(-(long)bits); // lowest set bit
                    var t = (w << 6) + Log2Floor(bit);
                    bits ^= bit;
                    if (t != d) { adj[d][t >> 6] |= 1UL << (t & 63); adj[t][d >> 6] |= 1UL << (d & 63); }
                }
            }
        }

        // Representation class: a long/double local can't hold a boxed value and vice-versa.
        byte Repr(int id) => isDouble[id] ? (byte)2 : isLong[id] ? (byte)1 : (byte)0;

        // Greedy coloring within each representation: assign each colorable id the first existing
        // representative whose members don't interfere with it, else open a new representative (=id).
        var slot = new int[v];
        for (var i = 0; i < v; i++) slot[i] = i;
        var reps = new List<(int Rep, byte R, ulong[] Members)>();
        var coalesced = false;
        for (var id = argCount + 1; id < v; id++)
        {
            if (kept[id]) continue;
            var r = Repr(id);
            var placed = false;
            foreach (var slotEntry in reps)
            {
                if (slotEntry.R != r) continue;
                var conflict = false;
                for (var w = 0; w < words; w++)
                    if ((adj[id][w] & slotEntry.Members[w]) != 0) { conflict = true; break; }
                if (conflict) continue;
                slot[id] = slotEntry.Rep;
                slotEntry.Members[id >> 6] |= 1UL << (id & 63);
                placed = true;
                if (slotEntry.Rep != id) coalesced = true;
                break;
            }
            if (!placed)
            {
                var members = new ulong[words];
                members[id >> 6] |= 1UL << (id & 63);
                reps.Add((id, r, members));
            }
        }
        return coalesced ? slot : null;
    }

    // Value-id uses of an instruction, set into `bits` (mirrors RubyIRMethod.CountValueUses /
    // RubyIRSsaRenumber.EnumerateUses: Src0 always; Src1/Src2 unless an index field; call-site args;
    // array operands; captured ids for LoadBlock / block-descriptor sends).
    internal static void CollectUses(RubyIRMethod ir, in RubyIRInstruction ix, ulong[] bits, int v)
    {
        void Add(int id) { if ((uint)id < (uint)v) bits[id >> 6] |= 1UL << (id & 63); }
        var op = ix.OpCode;
        Add(ix.Src0);
        if (op is not (RubyIROpCode.GuardInlineClass or RubyIROpCode.SendBlockDescriptor or RubyIROpCode.SendSelfBlockDescriptor))
        {
            Add(ix.Src1);
            Add(ix.Src2);
        }
        switch (op)
        {
            case RubyIROpCode.LoadBlock:
                foreach (var c in ir.ClosureCapturedValueIds) Add(c);
                break;
            case RubyIROpCode.SendBlockDescriptor:
            case RubyIROpCode.SendSelfBlockDescriptor:
                foreach (var c in ir.ClosureCapturedValueIds) Add(c);
                goto case RubyIROpCode.Send;
            case RubyIROpCode.Send:
            case RubyIROpCode.SendSelf:
            case RubyIROpCode.SendBlock:
            case RubyIROpCode.SendSelfBlock:
            case RubyIROpCode.PureUnarySend:
            case RubyIROpCode.VirtualNew:
            {
                var argc = ir.GetCallSiteArgumentCount(ix.Aux);
                for (var a = 0; a < argc; a++) Add(ir.GetCallSiteArgumentValueId(ix.Aux, a));
                break;
            }
            case RubyIROpCode.NewArray:
            case RubyIROpCode.NewArray2:
            case RubyIROpCode.NewHash:
            {
                var c = ir.GetOperandListCount(ix.Aux);
                for (var a = 0; a < c; a++) Add(ir.GetOperandListValueId(ix.Aux, a));
                break;
            }
        }
    }

    // Bit index of a power-of-two ulong (netstandard2.1 has no BitOperations). Used to iterate set
    // bits of a live-set word. De Bruijn sequence lookup.
    static readonly int[] DeBruijn64 =
    {
        0, 1, 48, 2, 57, 49, 28, 3, 61, 58, 50, 42, 38, 29, 17, 4,
        62, 55, 59, 36, 53, 51, 43, 22, 45, 39, 33, 30, 24, 18, 12, 5,
        63, 47, 56, 27, 60, 41, 37, 16, 54, 35, 52, 21, 44, 32, 23, 11,
        46, 26, 40, 15, 34, 20, 31, 10, 25, 14, 19, 9, 13, 8, 7, 6,
    };
    static int Log2Floor(ulong powerOfTwo) => DeBruijn64[(powerOfTwo * 0x03f79d71b4cb0a89UL) >> 58];

    internal static bool IsInlineCandidate(Irep calleeIrep)
    {
        try
        {
            var exe = RubyIRBuilder.Build(calleeIrep, 0, out _);
            return exe is not null && exe.Instructions.Length <= 48;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryReadInlineSelectorShape(Irep irep, out bool returnsNew)
    {
        returnsNew = false;
        try
        {
            var exe = RubyIRBuilder.Build(irep, 0, out _);
            if (exe is null) return false;
            foreach (var ins in exe.Instructions)
            {
                if (ins.OpCode is RubyIROpCode.SendSelf or RubyIROpCode.SendSelfBlock or RubyIROpCode.SendSelfBlockDescriptor)
                {
                    return false;
                }
                if (ins.OpCode == RubyIROpCode.VirtualNew)
                {
                    returnsNew = true;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static HashSet<int>? FindSpliceCandidatePcs(
        RubyIRMethod exe,
        IReadOnlyDictionary<Symbol, InlineSelectorTarget>? inlineSelectorRegistry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry)
    {
        var instructions = exe.Instructions;
        var defIndex = new int[exe.ValueCount];
        for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
        for (var i = 0; i < instructions.Length; i++)
        {
            var d = instructions[i].Dst;
            if ((uint)d < (uint)defIndex.Length) defIndex[d] = i;
        }

        HashSet<int>? result = null;
        for (var i = 0; i < instructions.Length; i++)
        {
            var ins = instructions[i];
            if (ins.OpCode is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) continue;
            var argc = exe.GetCallSiteArgumentCount(ins.Aux);
            var pc = exe.SourceBytecodePc(i);
            if (pc < 0) continue;

            if (ins.OpCode == RubyIROpCode.SendSelf)
            {
                for (var a = 0; a < argc; a++)
                {
                    if (ProducerReturnsNew(instructions, defIndex, exe, inlineSelectorRegistry, exe.GetCallSiteArgumentValueId(ins.Aux, a)))
                    {
                        result ??= [];
                        result.Add(pc);
                        break;
                    }
                }
            }
            else if (ins.OpCode == RubyIROpCode.Send &&
                     inlineSelectorRegistry is not null &&
                     inlineSelectorRegistry.TryGetValue(exe.GetCallSiteSymbol(ins.Aux), out var target) &&
                     target.ArgCount == argc)
            {
                var candidate =
                    (target.ReturnsNew &&
                     HasVirtualProducerConsumer(instructions, exe, inlineSelectorRegistry, accessorRegistry, ins.Dst)) ||
                    ProducerReturnsNew(instructions, defIndex, exe, inlineSelectorRegistry, ins.Src0);
                for (var a = 0; !candidate && a < argc; a++)
                {
                    candidate = ProducerReturnsNew(instructions, defIndex, exe, inlineSelectorRegistry, exe.GetCallSiteArgumentValueId(ins.Aux, a));
                }
                if (candidate)
                {
                    result ??= [];
                    result.Add(pc);
                }
            }
        }

        return result;
    }

    static bool HasVirtualProducerConsumer(
        ReadOnlySpan<RubyIRInstruction> instructions,
        RubyIRMethod exe,
        IReadOnlyDictionary<Symbol, InlineSelectorTarget>? inlineSelectorRegistry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry,
        int valueId)
    {
        if (IsClosureCaptured(exe, valueId)) return false;

        var aliases = new HashSet<int> { valueId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var u in instructions)
            {
                if (u.OpCode == RubyIROpCode.Move && aliases.Contains(u.Src0))
                {
                    if (IsClosureCaptured(exe, u.Dst)) return false;
                    if (aliases.Add(u.Dst)) changed = true;
                }
            }
        }

        foreach (var u in instructions)
        {
            if (u.OpCode is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) continue;
            var sym = exe.GetCallSiteSymbol(u.Aux);
            var inlineConsumer = inlineSelectorRegistry is not null && inlineSelectorRegistry.ContainsKey(sym);
            var accessorConsumer = accessorRegistry is not null && accessorRegistry.ContainsKey(sym);
            if (aliases.Contains(u.Src0) && (inlineConsumer || accessorConsumer))
            {
                return true;
            }

            if (inlineConsumer)
            {
                var argc = exe.GetCallSiteArgumentCount(u.Aux);
                for (var a = 0; a < argc; a++)
                {
                    if (aliases.Contains(exe.GetCallSiteArgumentValueId(u.Aux, a))) return true;
                }
            }
        }

        return false;
    }

    static bool ProducerReturnsNew(
        ReadOnlySpan<RubyIRInstruction> instructions,
        int[] defIndex,
        RubyIRMethod exe,
        IReadOnlyDictionary<Symbol, InlineSelectorTarget>? inlineSelectorRegistry,
        int valueId)
    {
        if (IsClosureCaptured(exe, valueId)) return false;
        if ((uint)valueId >= (uint)defIndex.Length) return false;
        var d = defIndex[valueId];
        if (d < 0) return false;
        var def = instructions[d];
        // Trace through copy chains: SSA-renumbered detection turns a reused merge id into a
        // unique id reached via Move copies (`rs` = vsub result -> Move -> Move -> receiver), so
        // the producer is behind one or more Moves. Bounded by instruction count (acyclic).
        for (var hops = 0; def.OpCode == RubyIROpCode.Move && hops < instructions.Length; hops++)
        {
            var src = def.Src0;
            if (IsClosureCaptured(exe, src) || (uint)src >= (uint)defIndex.Length) return false;
            var sd = defIndex[src];
            if (sd < 0) return false;
            def = instructions[sd];
        }
        if (def.OpCode == RubyIROpCode.VirtualNew) return true;
        return inlineSelectorRegistry is not null &&
               def.OpCode is RubyIROpCode.Send or RubyIROpCode.SendSelf &&
               inlineSelectorRegistry.TryGetValue(exe.GetCallSiteSymbol(def.Aux), out var target) &&
               target.ReturnsNew;
    }

    static bool IsClosureCaptured(RubyIRMethod exe, int valueId)
    {
        foreach (var captured in exe.ClosureCapturedValueIds)
        {
            if (captured == valueId) return true;
        }
        return false;
    }

    // Ops that commit no side effect and cannot re-enter arbitrary code, so a speculation guard's
    // deopt (which re-runs the whole method in the interpreter) is harmless. Sends are judged
    // separately (only trivial accessors / Math float calls are pure). Conservative whitelist: an
    // op not listed here marks the method non-speculatable.
    // Does this instruction COMMIT an observable side effect — i.e. one that a deopt (which
    // re-runs the bytecode from the method's top) would wrongly re-apply? Used to bound the
    // pre-side-effect float-speculation window. NOT side effects (a deopt safely re-does them):
    // fresh allocations (VirtualNew/MaterializeObject — re-created), writes to a freshly-allocated
    // scalar-replaced object (a method-local — discarded on deopt), pure float-Math sends, and
    // accessor getters / pure ops. ARE side effects: writes to self or a pre-existing object,
    // index/upvar/array writes, and any arbitrary (non-accessor, non-float-Math) call.
    internal static bool IsCommittedSideEffect(
        MRubyState state,
        RubyIRMethod exe,
        ScalarContext? sc,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry,
        in RubyIRInstruction ins)
    {
        var op = ins.OpCode;
        switch (op)
        {
            case RubyIROpCode.VirtualNew:
            case RubyIROpCode.MaterializeObject:
                return false;
            case RubyIROpCode.SetInstanceVariable:
            case RubyIROpCode.VirtualSetField:
                return !(sc?.IsScalar(ins.Src0) ?? false); // write to a fresh scalar object is transparent
            case RubyIROpCode.Send:
            case RubyIROpCode.SendSelf:
            {
                var sel = exe.GetCallSiteSymbol(ins.Aux);
                if (IsFloatReturningMethod(state, sel)) return false;
                if (accessorRegistry is not null && accessorRegistry.TryGetValue(sel, out var acc))
                    return acc.IsSetter && !(sc?.IsScalar(ins.Src0) ?? false); // getter pure; setter on fresh scalar transparent
                return true; // arbitrary call
            }
            default:
                return !RubyIROpInfo.IsPureSpeculationOp(op); // pure reads/arith/branches transparent; SetIndex/ArraySet/SetUpVar/... commit
        }
    }

    // Float-valued constant NAMES across the whole program, cached per state (codegen visits
    // each method's GetConstant ops and needs to know if a constant is a Float to seed taint).
    // Without this, `someInt / FLOAT_CONST` reads as same-taint and would deopt mid-method,
    // corrupting any side effect already committed. Names can collide across scopes; a name
    // that is float anywhere is treated as float-seeding, which is conservative (at worst it
    // forces a Send slow path that's always correct).
    [ThreadStatic] static MRubyState? floatConstState;
    [ThreadStatic] static HashSet<Symbol>? floatConstNames;

    internal static bool IsFloatConstantName(MRubyState state, Symbol name)
    {
        if (!ReferenceEquals(floatConstState, state))
        {
            var names = new HashSet<Symbol>();
            state.EnumerateConstants((sym, value) => { if (value.IsFloat) names.Add(sym); });
            floatConstNames = names;
            floatConstState = state;
        }
        return floatConstNames!.Contains(name);
    }

    // Cap on element count: `Array.new(1000)` shouldn't explode into 1000 locals.
    const int ScalarArrayMaxSize = 32;

    internal static Dictionary<int, (int, int)>? FindScalarArrays(MRubyState state, RubyIRMethod exe)
    {
        var ins = exe.Instructions;
        var n = exe.ValueCount;
        Dictionary<int, (int, int)>? result = null;
        var tmp = new ulong[(n + 63) >> 6];
        var defIndex = new int[n];
        for (var k = 0; k < n; k++) defIndex[k] = -1;
        for (var k = 0; k < ins.Length; k++) { var d = ins[k].Dst; if ((uint)d < (uint)n) defIndex[d] = k; }

        for (var i = 0; i < ins.Length; i++)
        {
            int arr, size;
            if (ins[i].OpCode == RubyIROpCode.NewArray) // [a, b, c] literal (NewArray2 splat not modeled)
            {
                arr = ins[i].Dst;
                size = exe.GetOperandListCount(ins[i].Aux);
            }
            else if (ins[i].OpCode == RubyIROpCode.VirtualNew &&
                     exe.GetCallSiteArgumentCount(ins[i].Aux) == 1 &&
                     IsArrayClassReceiver(state, exe, ins, defIndex, ins[i].Src0) &&
                     Mrb2CsCompiler.ConstFix(exe.GetCallSiteArgumentValueId(ins[i].Aux, 0), out var anSize) &&
                     anSize > 0 && anSize <= ScalarArrayMaxSize) // Array.new(const) -> nil-filled
            {
                arr = ins[i].Dst;
                size = (int)anSize;
            }
            else continue;
            if ((uint)arr >= (uint)n || IsClosureCaptured(exe, arr)) continue;
            if (size is 0 or > ScalarArrayMaxSize) continue;
            // The array's value flows through Move copies (register reuse); follow them so accesses
            // via any alias still count and the whole closure is replaced together.
            var aliases = Analyzer.MoveClosure(exe, arr);
            if (aliases.Contains(0)) continue; // would alias self/arg (shouldn't happen)

            var eligible = true;
            for (var u = 0; u < ins.Length && eligible; u++)
            {
                var op = ins[u].OpCode;
                if (op == RubyIROpCode.NewArray && ins[u].Dst == arr)
                {
                    // The defining literal: elements must not reference the array itself.
                    var c = exe.GetOperandListCount(ins[u].Aux);
                    for (var a = 0; a < c; a++) if (InAliases(exe.GetOperandListValueId(ins[u].Aux, a))) { eligible = false; break; }
                    continue;
                }
                if (op == RubyIROpCode.Move && InAliases(ins[u].Src0)) continue; // intra-alias copy
                if (op == RubyIROpCode.GetIndex0 && InAliases(ins[u].Src0)) continue;
                if (op == RubyIROpCode.GetIndex && InAliases(ins[u].Src0) &&
                    !InAliases(ins[u].Src1) && Mrb2CsCompiler.ConstFix(ins[u].Src1, out var gi) && gi >= 0 && gi < size) continue;
                if (op == RubyIROpCode.SetIndex && InAliases(ins[u].Src0) &&
                    !InAliases(ins[u].Src1) && !InAliases(ins[u].Src2) &&
                    Mrb2CsCompiler.ConstFix(ins[u].Src1, out var si) && si >= 0 && si < size) continue;
                // Any other appearance of an alias (non-const/oob index, Send arg, store, return,
                // used as an index/element) -> escapes or dynamic -> not replaceable.
                Array.Clear(tmp, 0, tmp.Length);
                Analyzer.CollectUses(exe, ins[u], tmp, n);
                foreach (var al in aliases)
                    if ((tmp[al >> 6] & (1UL << (al & 63))) != 0) { eligible = false; break; }
            }
            if (eligible)
            {
                result ??= new Dictionary<int, (int, int)>();
                foreach (var al in aliases) result[al] = (arr, size);
            }

            continue;

            bool InAliases(int id) => aliases.Contains(id);
        }
        return result;
    }

    // True iff `recvId`'s definition is `GetConstant :Array` — i.e. the receiver of a `.new` is the
    // core Array class, so `Array.new(n)` builds a plain n-element nil array we can scalar-replace.
    static bool IsArrayClassReceiver(MRubyState state, RubyIRMethod exe, ReadOnlySpan<RubyIRInstruction> ins, int[] defIndex, int recvId)
    {
        if ((uint)recvId >= (uint)defIndex.Length) return false;
        var di = defIndex[recvId];
        if (di < 0 || ins[di].OpCode != RubyIROpCode.GetConstant) return false;
        return state.NameOf(exe.GetSymbol(ins[di].Aux)).AsSpan().SequenceEqual("Array"u8);
    }

    // Constant key -> identifier-safe, collision-free tag (symbol intern id / fixnum value).
    static bool TryConstKeyTag(RubyIRMethod exe, int[] defIndex, int keyId, out string tag)
    {
        tag = "";
        if ((uint)keyId >= (uint)defIndex.Length) return false;
        var di = defIndex[keyId];
        if (di < 0 || exe.Instructions[di].OpCode != RubyIROpCode.LoadValue) return false;
        var lit = exe.GetLiteral(exe.Instructions[di].Aux);
        if (lit.IsFixnum) { tag = "i" + lit.FixnumValue; return true; }
        if (lit.IsSymbol) { tag = "s" + lit.SymbolValue.Value; return true; }
        return false; // string/float/other keys not modeled
    }

    internal static Dictionary<int, (int, HashSet<string>)>? FindScalarHashes(RubyIRMethod exe)
    {
        var ins = exe.Instructions;
        var n = exe.ValueCount;
        Dictionary<int, (int, HashSet<string>)>? result = null;
        var tmp = new ulong[(n + 63) >> 6];
        var defIndex = new int[n];
        for (var k = 0; k < n; k++) defIndex[k] = -1;
        for (var k = 0; k < ins.Length; k++) { var d = ins[k].Dst; if ((uint)d < (uint)n) defIndex[d] = k; }

        for (var i = 0; i < ins.Length; i++)
        {
            if (ins[i].OpCode != RubyIROpCode.NewHash) continue;
            var hash = ins[i].Dst;
            if ((uint)hash >= (uint)n || IsClosureCaptured(exe, hash)) continue;
            var pairs = exe.GetOperandListCount(ins[i].Aux) / 2;
            if (pairs == 0 || pairs > ScalarArrayMaxSize) continue;

            var aliases = Analyzer.MoveClosure(exe, hash);
            if (aliases.Contains(0)) continue;
            bool InAliases(int id) => aliases.Contains(id);

            var keys = new HashSet<string>();          // keys that HOLD a value (literal + set)
            var keyTags = new Dictionary<int, string>(); // const-key value-id -> tag
            var eligible = true;

            // Literal keys must all be constant.
            for (var p = 0; p < pairs && eligible; p++)
            {
                var keyId = exe.GetOperandListValueId(ins[i].Aux, 2 * p);
                if (!TryConstKeyTag(exe, defIndex, keyId, out var tag)) { eligible = false; break; }
                keys.Add(tag);
                keyTags[keyId] = tag;
            }

            for (var u = 0; u < ins.Length && eligible; u++)
            {
                var op = ins[u].OpCode;
                if (op == RubyIROpCode.NewHash && ins[u].Dst == hash) continue; // the literal itself
                if (op == RubyIROpCode.Move && InAliases(ins[u].Src0)) continue;
                if (op == RubyIROpCode.GetIndex0 && InAliases(ins[u].Src0)) continue; // h[0] -> tag i0
                if (op == RubyIROpCode.GetIndex && InAliases(ins[u].Src0) && !InAliases(ins[u].Src1) &&
                    TryConstKeyTag(exe, defIndex, ins[u].Src1, out var gtag))
                {
                    keyTags[ins[u].Src1] = gtag; // a get of an absent key reads nil (no local needed)
                    continue;
                }
                if (op == RubyIROpCode.SetIndex && InAliases(ins[u].Src0) && !InAliases(ins[u].Src1) &&
                    !InAliases(ins[u].Src2) && TryConstKeyTag(exe, defIndex, ins[u].Src1, out var stag))
                {
                    keys.Add(stag); // a set adds/holds the key
                    keyTags[ins[u].Src1] = stag;
                    continue;
                }
                // Any other appearance (dynamic key, iteration .each/.keys/.size, escape) -> bail.
                Array.Clear(tmp, 0, tmp.Length);
                Analyzer.CollectUses(exe, ins[u], tmp, n);
                foreach (var al in aliases)
                    if ((tmp[al >> 6] & (1UL << (al & 63))) != 0) { eligible = false; break; }
            }
            if (eligible)
            {
                result ??= new Dictionary<int, (int, HashSet<string>)>();
                foreach (var al in aliases) result[al] = (hash, keys);
                Mrb2CsCompiler.CurrentHashKeyTags ??= new Dictionary<int, string>();
                foreach (var (kid, t) in keyTags) Mrb2CsCompiler.CurrentHashKeyTags[kid] = t;
            }
        }
        return result;
    }

    internal static Dictionary<int, MRubyValue>? BuildConstLit(RubyIRMethod exe)
    {
        // Only a SINGLE-def LoadValue id is a stable constant — a register-reused id (non-SSA block
        // IR) could be reassigned, so it must be excluded for soundness.
        var defCount = new int[exe.ValueCount];
        foreach (var u in exe.Instructions) if ((uint)u.Dst < (uint)defCount.Length) defCount[u.Dst]++;
        Dictionary<int, MRubyValue>? map = null;
        foreach (var u in exe.Instructions)
            if (u.OpCode == RubyIROpCode.LoadValue && (uint)u.Dst < (uint)defCount.Length && defCount[u.Dst] == 1)
            {
                var lit = exe.GetLiteral(u.Aux);
                if (lit.IsFixnum || lit.IsFloat) (map ??= new())[u.Dst] = lit;
            }
        return map;
    }

    // A send whose result is always a Float -> seeds float taint / provesDouble. True for builtin
    // Float-returning methods (Math.* / to_f, which aren't in the AOT method set) OR a user method
    // whose body was INFERRED to always return Float (RubyIRReturnTypes; sound, no name guessing).
    internal static bool IsFloatReturningMethod(MRubyState state, Symbol sym) =>
        IsBuiltinFloatMethod(state, sym) || (Mrb2CsCompiler.CurrentReturnTypes?.ReturnsFloat(sym) ?? false);

    // Float-returning BUILTINS (not in the AOT method set, so they must be seeded): Math.* and
    // to_f. Everything else is inferred from method bodies, not matched by name.
    // Byte-exact symbol-name compare, the building block of the name-classification predicates below.
    internal static bool Matches(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceEqual(b);

    internal static bool IsBuiltinFloatMethod(MRubyState state, Symbol sym)
    {
        var name = state.NameOf(sym).AsSpan();
        return Matches(name, "to_f"u8) ||
               Matches(name, "sqrt"u8) || Matches(name, "cbrt"u8) ||
               Matches(name, "sin"u8) || Matches(name, "cos"u8) || Matches(name, "tan"u8) ||
               Matches(name, "asin"u8) || Matches(name, "acos"u8) || Matches(name, "atan"u8) ||
               Matches(name, "atan2"u8) || Matches(name, "hypot"u8) ||
               Matches(name, "exp"u8) || Matches(name, "log"u8) ||
               Matches(name, "log2"u8) || Matches(name, "log10"u8) ||
               Matches(name, "sinh"u8) || Matches(name, "cosh"u8) || Matches(name, "tanh"u8) ||
               Matches(name, "pow"u8);
    }

    internal static bool IsToFMethod(MRubyState state, Symbol sym) =>
        Matches(state.NameOf(sym).AsSpan(), "to_f"u8);

    // Methods that switch fibers/threads — unsafe to call from a compiled C# frame.
    internal static bool IsContextSwitchingMethod(MRubyState state, Symbol sym)
    {
        var name = state.NameOf(sym).AsSpan();
        return Matches(name, "yield"u8) || Matches(name, "resume"u8) ||
               Matches(name, "transfer"u8) || Matches(name, "sleep"u8) ||
               Matches(name, "pass"u8);
    }
}
