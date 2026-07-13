using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using static ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler;
using static ChibiRuby.JetPack.Mrb2Cs.Analyzer;

using static ChibiRuby.JetPack.RubyIROpInfo;
namespace ChibiRuby.JetPack.Mrb2Cs;

// C# emission: walks the analyzed RubyIR and emits the C# method body. Reads the per-method
// analysis facts (unboxing, scalar/stack replacement, const literals) that Mrb2CsCompiler's
// orchestration computes and stores in its (internal) per-method state, plus the shared pure
// helpers/predicates. Holds the emit-only caches (RObject-cast CSE, ivar-read CSE, block-emit
// state, struct-canonical aliasing). Mutual `using static` with Mrb2CsCompiler.
public static class Emitter
{
    [ThreadStatic]
    internal static BlockEmitState? BlockEmit;

    // CSE for `(RObject)v.Object` casts: maps a value-id to the C# local that already holds its
    // RObject cast, so repeated ivar ops on the same (unconditionally-RObject) receiver — self, or
    // a freshly-`new`ed object — cast once. Invalidated when the value-id is reassigned or at a
    // control-flow join (label), so the cached local always refers to the live value.
    [ThreadStatic]
    static Dictionary<int, string>? AsObjCache;

    [ThreadStatic]
    static int AsObjCounter;

    // Return the name of an RObject local holding `valueId`'s cast, emitting the one-time
    // `RObject __roN = vK.As<RObject>();` on first use. Only call for values known to be RObject.
    internal static string AsRObject(int valueId, string boxedRead, StringBuilder body)
    {
        AsObjCache ??= new Dictionary<int, string>();
        if (AsObjCache.TryGetValue(valueId, out var name)) return name;
        name = "__ro" + AsObjCounter++;
        Line(body, $"global::ChibiRuby.RObject {name} = {boxedRead}.As<global::ChibiRuby.RObject>();");
        AsObjCache[valueId] = name;
        return name;
    }

    internal static void InvalidateAsObj(int valueId) => AsObjCache?.Remove(valueId);
    internal static void ClearAsObjCache() => AsObjCache?.Clear();

    // CSE of self instance-variable reads: `(receiver value-id, field) -> a dedicated `__ivN` local
    // holding the result of the first `.InstanceVariables.Get(field)`. A re-read of the same field on
    // the same receiver (with no intervening write/call/branch) reuses that local instead of doing a
    // second hash-table lookup — e.g. `@x * @x` reads @x once. The temp is a dedicated local (like
    // asObjCache's `__roN`), never coalesced, so reusing it is sound regardless of value-id liveness.
    [ThreadStatic] internal static Dictionary<(int Recv, Symbol Field), string>? ivarGetCache;
    [ThreadStatic] internal static int ivCounter;
    // Pre-pass result: read dsts that have a later valid re-read, so are worth caching into a `__ivN`
    // temp. A read NOT in this set is emitted inline (no temp) to avoid bloating single-read fields.
    [ThreadStatic] internal static HashSet<int>? reusedIvarReads;

    internal static void ClearIvarGetCache() => ivarGetCache?.Clear();

    // Drop cached reads invalidated by instruction `ins`: any Ruby call or ivar write may change a
    // field's value (conservatively clear all), and reassigning a receiver value-id stales its reads.
    internal static void InvalidateIvarGet(in RubyIRInstruction ins)
    {
        if (ivarGetCache is not { Count: > 0 } cache) return;
        switch (ins.OpCode)
        {
            case RubyIROpCode.Send:
            case RubyIROpCode.SendSelf:
            case RubyIROpCode.SendBlock:
            case RubyIROpCode.SendSelfBlock:
            case RubyIROpCode.SendBlockDescriptor:
            case RubyIROpCode.SendSelfBlockDescriptor:
            case RubyIROpCode.PureUnarySend:
            case RubyIROpCode.VirtualNew:
            case RubyIROpCode.SetInstanceVariable:
            case RubyIROpCode.VirtualSetField:
                cache.Clear();
                return;
        }
        var d = ins.Dst;
        if (d >= 0)
        {
            List<(int, Symbol)>? drop = null;
            foreach (var k in cache.Keys) if (k.Recv == d) (drop ??= new()).Add(k);
            if (drop is not null) foreach (var k in drop) cache.Remove(k);
        }
    }

    // Pre-pass: which self-ivar read dsts are re-read later (same receiver+field, no intervening
    // write/call/branch) — those are cached into a `__ivN` temp; one-shot reads stay inline.
    internal static HashSet<int> ComputeReusedIvarReads(RubyIRMethod ir, HashSet<int> targets)
    {
        var reused = new HashSet<int>();
        if (Environment.GetEnvironmentVariable("AOT_NOIVARCSE") == "1") return reused; // diagnostic off-switch
        var seen = new Dictionary<(int, Symbol), int>();
        var ins = ir.Instructions;
        for (var i = 0; i < ins.Length; i++)
        {
            if (targets.Contains(i)) seen.Clear(); // control-flow join: reads above may not reach here
            ref readonly var x = ref ins[i];
            if (x.OpCode is RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField)
            {
                var key = (x.Src0, ir.GetSymbol(x.Aux));
                if (seen.TryGetValue(key, out var canon)) reused.Add(canon);
                else seen[key] = x.Dst;
                continue;
            }
            // Mirror InvalidateIvarGet's invalidation so the pre-pass agrees with emission.
            switch (x.OpCode)
            {
                case RubyIROpCode.Send:
                case RubyIROpCode.SendSelf:
                case RubyIROpCode.SendBlock:
                case RubyIROpCode.SendSelfBlock:
                case RubyIROpCode.SendBlockDescriptor:
                case RubyIROpCode.SendSelfBlockDescriptor:
                case RubyIROpCode.PureUnarySend:
                case RubyIROpCode.VirtualNew:
                case RubyIROpCode.SetInstanceVariable:
                case RubyIROpCode.VirtualSetField:
                    seen.Clear();
                    break;
                default:
                    if (x.Dst >= 0)
                    {
                        List<(int, Symbol)>? drop = null;
                        foreach (var k in seen.Keys) if (k.Item1 == x.Dst) (drop ??= new()).Add(k);
                        if (drop is not null) foreach (var k in drop) seen.Remove(k);
                    }
                    break;
            }
        }
        return reused;
    }

    // Assign a long-valued expression to dst in its representation (raw long or re-boxed).
    static void AssignFix(bool[] isLong, StringBuilder body, int dst, string longExpr)
    {
        Line(body, isLong[dst]
            ? $"l{Slot(dst)} = {longExpr};"
            : $"v{Slot(dst)} = new global::ChibiRuby.MRubyValue({longExpr});");
    }

    // Emit `if (!(g0 && g1 && ...)) deopt;` over the non-null guard parts (none -> nothing).
    static void EmitGuard(StringBuilder body, params string?[] guards)
    {
        var live = new List<string>();
        foreach (var g in guards)
        {
            if (g is not null) live.Add(g);
        }
        if (live.Count > 0)
        {
            Line(body, $"if (!({string.Join(" && ", live)})) {{ result = default; return false; }}");
        }
    }

    internal static bool EmitInstruction(MRubyState state, RubyIRMethod exe, in RubyIRInstruction ins, SymbolCache sym, StringBuilder body, InlineContext? ic, ScalarContext? sc, bool[] isLong, bool[] floatTaint, bool[] isDouble, bool[] provesDouble)
    {
        switch (ins.OpCode)
        {
            case RubyIROpCode.CheckArity:
                return true;

            // `Const.new(...)` proven non-escaping -> no allocation; initialize is inlined as
            // field-local assignments. Any other VirtualNew still bails (interpreter allocates).
            case RubyIROpCode.VirtualNew:
            {
                // Array.new(const) scalar-replaced: nil-fill the element locals, no allocation.
                if (TryScalarArray(ins.Dst, out var vnCanon, out var vnSize))
                {
                    for (var k = 0; k < vnSize; k++)
                        Line(body, $"{ArrElem(vnCanon, k)} = global::ChibiRuby.MRubyValue.Nil;");
                    return true;
                }
                // Stack-allocated object: build the struct in place (no heap alloc).
                if (CurrentStackObjects is { } cso && cso.TryGetValue(ins.Dst, out var stackLay))
                {
                    EmitStackConstruct(exe, ins, stackLay, body);
                    return true;
                }
                if (sc is not null && sc.IsScalar(ins.Dst))
                {
                    EmitScalarNew(sc, exe, ins, body);
                    return true;
                }
                // Escaping but inline-constructible: allocate + set ivars directly, skipping the
                // :new + :initialize double dispatch.
                if (sc is not null && sc.IsFastNew(ins.Dst))
                {
                    EmitFastNew(sc, exe, ins, body);
                    return true;
                }
                if (Environment.GetEnvironmentVariable("AOT_NONEW") == "1") { LastBail = "vnew:gated"; return false; }
                // Escaping allocation (returned / stored into another object): can't scalarize,
                // so allocate for real via `Class.new(...)`. Emitting it as a Send keeps the
                // rest of the method compiled (the whole body no longer bails to the
                // interpreter just because it constructs an object). Same dispatch the
                // interpreter would do; everything around it now runs as C#.
                var newSym = exe.GetCallSiteSymbol(ins.Aux);
                if (!TrySymbolStringLiteral(state, newSym, out var newName))
                {
                    LastBail = "vnew:sym";
                    return false;
                }
                var newArgc = exe.GetCallSiteArgumentCount(ins.Aux);
                if (newArgc > 4)
                {
                    LastBail = "vnew:argc>4";
                    return false;
                }
                var newCall = new StringBuilder();
                newCall.Append(Val(ins.Dst)).Append(" = state.Send(").Append(Val(ins.Src0))
                    .Append(", ").Append(sym.Reference(newName));
                for (var i = 0; i < newArgc; i++)
                {
                    newCall.Append(", ").Append(Val(exe.GetCallSiteArgumentValueId(ins.Aux, i)));
                }
                newCall.Append(");");
                Line(body, newCall.ToString());
                return true;
            }

            case RubyIROpCode.Move:
                // A copy of a scalar-replaced array/hash alias is a no-op (src and dst share the
                // element/key locals).
                if (TryScalarArray(ins.Src0, out _, out _) || TryScalarHash(ins.Src0, out _, out _)) return true;
                if (sc is not null && sc.TryEmitScalarMove(ins))
                {
                    return true;
                }
                // Looping methods: representation-aware copy (the back-edge `zr = tr` and pre-loop
                // `zr = 0.0` write the raw d{}/l{} local for raw loop-carried values). Reads convert
                // from src's representation; a boxed dst re-boxes a raw src.
                if (CurrentProvesFixnum is not null)
                {
                    var md = ins.Dst;
                    var ms = ins.Src0;
                    var lhs = isDouble[md] ? "d" + Slot(md) : isLong[md] ? "l" + Slot(md) : Val(md);
                    var rhs = isDouble[md] ? DoubleRead(isLong, isDouble, ms)
                        : isLong[md] ? FixRead(isLong, ms)
                        : BoxReadFull(isLong, isDouble, ms);
                    if (lhs != rhs) Line(body, $"{lhs} = {rhs};");
                    return true;
                }
                // Canonicalized mutated-struct aliases render to the same local -> a `soX = soX`
                // self-copy; skip it (and it must be skipped so the snapshot/copy ordering is right).
                if (Val(ins.Dst) != Val(ins.Src0)) Line(body, $"{Val(ins.Dst)} = {Val(ins.Src0)};");
                return true;

            case RubyIROpCode.LoadValue:
            {
                // Looping methods: a raw loop local is initialized directly (e.g. `d{} = 0D` for a
                // pre-loop `zr = 0.0`), no MRubyValue box.
                if (CurrentProvesFixnum is not null)
                {
                    if (isDouble[ins.Dst]) { Line(body, $"d{Slot(ins.Dst)} = {DoubleLitText(exe.GetLiteral(ins.Aux).FloatValue)};"); return true; }
                    if (isLong[ins.Dst]) { Line(body, $"l{Slot(ins.Dst)} = {exe.GetLiteral(ins.Aux).FixnumValue}L;"); return true; }
                }
                // Symbol literal (`:a`) — used as a hash key / symbol value. Intern once via the
                // method's SymbolCache.
                var lvLit = exe.GetLiteral(ins.Aux);
                if (lvLit.IsSymbol && TrySymbolStringLiteral(state, lvLit.SymbolValue, out var lvSymName))
                {
                    Line(body, $"{Val(ins.Dst)} = new global::ChibiRuby.MRubyValue({sym.Reference(lvSymName)});");
                    return true;
                }
                if (!TryEmitLiteral(lvLit, out var expr))
                {
                    return false;
                }
                Line(body, $"{Val(ins.Dst)} = {expr};");
                return true;
            }

            case RubyIROpCode.LoadSelf:
                Line(body, $"{Val(ins.Dst)} = {Val(0)};");
                return true;

            case RubyIROpCode.GetConstant:
            {
                if (!TrySymbolStringLiteral(state, exe.GetSymbol(ins.Aux), out var cname))
                {
                    return false;
                }
                // GetConstantUnsafe shares the interpreter's lexical resolution; the cached form
                // skips the scope-chain walk while no constant has been (re)assigned. Both paths
                // resolve identically on a miss/first call. AOT_NOCONSTCACHE forces the uncached form.
                if (ic is not null && Environment.GetEnvironmentVariable("AOT_NOCONSTCACHE") != "1")
                {
                    EmitGuardedConstantRead(ic, ins, cname, body);
                }
                else
                {
                    Line(body, $"{Val(ins.Dst)} = GetConstantUnsafe(state, {sym.Reference(cname)});");
                }
                return true;
            }

            case RubyIROpCode.GetModuleConstant:
            {
                if (!TrySymbolStringLiteral(state, exe.GetSymbol(ins.Aux), out var cname))
                {
                    return false;
                }
                // `Mod::Name` — resolve the constant in the module value (Src0), exactly
                // as the interpreter's OP_GetMCnst does. GetConst(Symbol, RClass) is public.
                Line(body, $"{Val(ins.Dst)} = state.GetConst({sym.Reference(cname)}, {Val(ins.Src0)}.As<global::ChibiRuby.RClass>());");
                return true;
            }

            case RubyIROpCode.GuardInlineClass:
            {
                if (sc is not null && TryEmitScalarInlineGuard(sc, exe, ins, body))
                {
                    return true;
                }
                if (ic is null ||
                    !exe.TryGetGuardInline(ins.Src1, out var fp) ||
                    fp == 0)
                {
                    LastBail = "guardinline:metadata";
                    return false;
                }
                if (!TrySymbolStringLiteral(state, exe.GetCallSiteSymbol(ins.Src1), out var mname))
                {
                    return false;
                }
                EmitGuardInlineClass(ic, ins, mname, fp, body);
                return true;
            }

            case RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField:
            {
                if (sc is not null && TryEmitScalarFieldAccess(sc, exe, ins, body))
                {
                    return true;
                }
                // Self is a stack struct (struct-receiver variant): read the field directly.
                if (CurrentStackObjects is { } gcso && gcso.TryGetValue(ins.Src0, out var glay) &&
                    glay.FieldIndexOf(exe.GetSymbol(ins.Aux)) is var gfi && gfi >= 0)
                {
                    var gso = Val(ins.Src0);
                    var gread = glay.FieldKinds[gfi] switch
                    {
                        StackFieldKind.Double or StackFieldKind.Long => $"new global::ChibiRuby.MRubyValue({gso}.f{gfi})",
                        _ => $"{gso}.f{gfi}",
                    };
                    Line(body, $"{Val(ins.Dst)} = {gread};");
                    EmitFloatSpeculationGuard(body, provesDouble, ins.Dst);
                    return true;
                }
                if (!TrySymbolStringLiteral(state, exe.GetSymbol(ins.Aux), out var name))
                {
                    return false;
                }
                // Direct ivar ops are always on self (cross-object goes via accessor sends), so the
                // receiver is unconditionally an RObject — cast it once and reuse via the cache.
                var ivKey = (ins.Src0, exe.GetSymbol(ins.Aux));
                if (ivarGetCache is { } ivc && ivc.TryGetValue(ivKey, out var ivCached))
                {
                    // Re-read of the same field on the same receiver -> reuse the cached value.
                    Line(body, $"{Val(ins.Dst)} = {ivCached};");
                }
                else if (reusedIvarReads is { } rir && rir.Contains(ins.Dst))
                {
                    // First of several reads: stash into a dedicated temp the later reads can reuse.
                    var t = "__iv" + ivCounter++;
                    Line(body, $"global::ChibiRuby.MRubyValue {t} = {IvarGet(AsRObject(ins.Src0, Val(ins.Src0), body), sym.Reference(name))};");
                    Line(body, $"{Val(ins.Dst)} = {t};");
                    ivarGetCache![ivKey] = t;
                }
                else
                {
                    // One-shot read: emit inline, no temp.
                    Line(body, $"{Val(ins.Dst)} = {IvarGet(AsRObject(ins.Src0, Val(ins.Src0), body), sym.Reference(name))};");
                }
                EmitFloatSpeculationGuard(body, provesDouble, ins.Dst);
                return true;
            }

            case RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField:
            {
                if (sc is not null && TryEmitScalarFieldAccess(sc, exe, ins, body))
                {
                    return true;
                }
                // Self is a stack struct (a by-ref struct-receiver variant): write the field
                // directly. Only reachable for a `ref` (Mutated) layout; read-only `in` self never
                // hits a SetInstanceVariable because CalleeSelfReadOnly rejects mutating callees.
                if (CurrentStackObjects is { } scso && scso.TryGetValue(ins.Src0, out var slay) &&
                    slay.FieldIndexOf(exe.GetSymbol(ins.Aux)) is var sfi && sfi >= 0)
                {
                    var sso = Val(ins.Src0);
                    var sval = slay.FieldKinds[sfi] switch
                    {
                        StackFieldKind.Double => Val(ins.Src1) + ".FloatValue",
                        StackFieldKind.Long => Val(ins.Src1) + ".IntegerValue",
                        _ => Val(ins.Src1),
                    };
                    Line(body, $"{sso}.f{sfi} = {sval};");
                    return true;
                }
                if (!TrySymbolStringLiteral(state, exe.GetSymbol(ins.Aux), out var name))
                {
                    return false;
                }
                Line(body, $"{IvarSet(AsRObject(ins.Src0, Val(ins.Src0), body), sym.Reference(name), Val(ins.Src1))};");
                return true;
            }

            // Generic (untyped) arith. The AOT lowering path never produces the typed
            // Float opcodes, so int and float both arrive here; emit a runtime dual path
            // (both-fixnum -> long, both-float -> double, else deopt -> interpreter handles
            // mixed/coercion). Long-dst values are float-untainted by construction, so their
            // path stays fixnum-only.
            case RubyIROpCode.Add or RubyIROpCode.AddFixnum:
                return EmitNumericBinary(sym, body, ins, "+", isLong, floatTaint, isDouble, provesDouble);
            case RubyIROpCode.Sub or RubyIROpCode.SubFixnum:
                return EmitNumericBinary(sym, body, ins, "-", isLong, floatTaint, isDouble, provesDouble);

            // `reg +/- <small int literal>` (AddI/SubI). Immediate is a fixnum in the
            // literal pool; guard the receiver is fixnum and fold the constant in.
            case RubyIROpCode.AddImmediate or RubyIROpCode.AddImmediateFixnum:
                return EmitFixnumImmediate(sym, body, exe, ins, "+", isLong);
            case RubyIROpCode.SubImmediate or RubyIROpCode.SubImmediateFixnum:
                return EmitFixnumImmediate(sym, body, exe, ins, "-", isLong);
            case RubyIROpCode.Mul or RubyIROpCode.MulFixnum:
                return EmitNumericBinary(sym, body, ins, "*", isLong, floatTaint, isDouble, provesDouble);
            case RubyIROpCode.Div or RubyIROpCode.DivFixnum:
                // Fixnum division matches the interpreter (C# truncating /); a zero divisor
                // takes the slow path (interpreter raises ZeroDivisionError). Float division
                // follows IEEE (x/0.0 -> Infinity, as Ruby Float#/), no zero guard.
                return EmitNumericBinary(sym, body, ins, "/", isLong, floatTaint, isDouble, provesDouble, isDiv: true);

            // Fused multiply-add/sub from the arithmetic-fusion pass (generic int or float).
            // Operands: src0*src1 combined with src2; dual fixnum/float path over all three.
            case RubyIROpCode.MulAdd:
                return EmitNumericFused(sym, body, ins, isLong, floatTaint, isDouble, provesDouble, "+", false);
            case RubyIROpCode.MulSub:
                return EmitNumericFused(sym, body, ins, isLong, floatTaint, isDouble, provesDouble, "-", false);
            case RubyIROpCode.SubMul:
                return EmitNumericFused(sym, body, ins, isLong, floatTaint, isDouble, provesDouble, "-", true);

            case RubyIROpCode.Lt or RubyIROpCode.LtFixnum:
                return EmitNumericCompare(sym, body, ins, "<", isLong, floatTaint, isDouble, provesDouble);
            case RubyIROpCode.Le or RubyIROpCode.LeFixnum:
                return EmitNumericCompare(sym, body, ins, "<=", isLong, floatTaint, isDouble, provesDouble);
            case RubyIROpCode.Gt or RubyIROpCode.GtFixnum:
                return EmitNumericCompare(sym, body, ins, ">", isLong, floatTaint, isDouble, provesDouble);
            case RubyIROpCode.Ge or RubyIROpCode.GeFixnum:
                return EmitNumericCompare(sym, body, ins, ">=", isLong, floatTaint, isDouble, provesDouble);
            case RubyIROpCode.Eq:
                return EmitNumericCompare(sym, body, ins, "==", isLong, floatTaint, isDouble, provesDouble);

            // Nested method call -> public state.Send (full dispatch; if the callee is
            // also AOT-compiled it uses its compiled body too). No-block sends only;
            // explicit Send overloads cover 0..4 args.
            case RubyIROpCode.Send or RubyIROpCode.SendSelf:
            {
                // In a stack-struct VARIANT: a trivial-accessor getter on the struct param lowers
                // to a struct-field read (no devirt, no boxing for typed fields).
                if (ins.OpCode == RubyIROpCode.Send && CurrentStackObjects is { Count: > 0 } &&
                    TryEmitStackAccessor(state, exe, ins, sym, body))
                {
                    return true;
                }
                // Caller side: a non-accessor Send whose RECEIVER is a stack object -> call the
                // callee's struct-`self` variant (class statically known), reify on miss/deopt.
                if (CurrentStackObjects is { Count: > 0 } && TryEmitStackReceiverSend(ic, ins, body))
                {
                    return true;
                }
                // Caller side: a Send whose argument is a stack object -> dispatch to the callee's
                // specialized struct variant (guarded per receiver class), reify on miss/deopt.
                if (CurrentStackObjects is { Count: > 0 } && TryEmitStackArgSend(ic, ins, body))
                {
                    return true;
                }
                // Trivial accessor on a scalar-replaced object -> direct field-local access.
                if (sc is not null && TryEmitAccessorSend(sc, exe, ins, body))
                {
                    return true;
                }
                var methodSym = exe.GetCallSiteSymbol(ins.Aux);
                // Context-switching sends (Fiber.yield/resume/transfer, sleep, Thread.pass)
                // cannot run from a compiled C# frame — the fiber can't be suspended/
                // resumed mid-C#-method ("resuming dead fiber"). Bail so they interpret.
                if (IsContextSwitchingMethod(state, methodSym))
                {
                    LastBail = "send:ctxswitch";
                    return false;
                }
                if (!TrySymbolStringLiteral(state, methodSym, out var mname))
                {
                    LastBail = "send:sym";
                    return false;
                }
                var argc = exe.GetCallSiteArgumentCount(ins.Aux);
                if (argc > 4)
                {
                    LastBail = "send:argc>4";
                    return false;
                }
                // Monomorphic self-send -> guarded inline call to the callee's __inline form.
                if (ins.OpCode == RubyIROpCode.SendSelf && ic is not null &&
                    TryInlineSelfSend(ic, ins, methodSym, mname, argc, body))
                {
                    return true;
                }
                // Cross-object 0-arg send to a constant-returning method -> guarded constant.
                if (ic is not null && TryEmitConstantDevirt(ic, ins, methodSym, mname, argc, body))
                {
                    return true;
                }
                // Cross-object accessor (recv.getter / recv.setter=) -> guarded field access.
                if (ic is not null && TryEmitAccessorDevirt(ic, ins, methodSym, mname, argc, body))
                {
                    // A speculated float accessor read (provesDouble) gets its IsFloat guard after
                    // both the devirt-hit and Send-fallback branches have written the result.
                    EmitFloatSpeculationGuard(body, provesDouble, ins.Dst);
                    return true;
                }
                // `to_f` is hot in numeric loops (e.g. ao-bench render). Inline the common
                // immediate Integer/Float cases and fall back for String/custom receivers.
                if (argc == 0 &&
                    Environment.GetEnvironmentVariable("AOT_NOTOF") != "1" &&
                    IsToFMethod(state, methodSym))
                {
                    var r = Val(ins.Src0);
                    var d = Val(ins.Dst);
                    Line(body, $"if ({r}.IsFixnum) {{ {d} = new global::ChibiRuby.MRubyValue((double){r}.FixnumValue); }} else if ({r}.IsFloat) {{ {d} = {r}; }} else {{ {d} = state.Send({r}, {sym.Reference(mname)}); }}");
                    return true;
                }
                // One-argument pure C# methods avoid building a Ruby call frame. Guard the
                // argument to numeric immediates so non-numeric error paths keep Send's frame.
                if (argc == 1 &&
                    Environment.GetEnvironmentVariable("AOT_NOPUREUNARY") != "1" &&
                    IsFloatReturningMethod(state, methodSym) &&
                    ic is not null)
                {
                    return TryEmitPureUnarySend(ic, ins, mname, body);
                }
                // Fixnum bitwise-operator sends (&, |, ^, >>) have no mruby opcode, so they
                // arrive as a full-dispatch Send — hot in integer code (NES masking/shifts).
                // Inline the fixnum case (guarded) with a Send fallback. &|^ can't overflow;
                // >> is guarded to a 0..63 shift (C# long >> is arithmetic, matching Ruby).
                // << / % stay as Send (overflow / floor-semantics differ from C#).
                if (argc == 1 && TryFixnumBitwiseOp(state, methodSym, out var binOp, out var isShift))
                {
                    var recvId = ins.Src0;
                    var argId = exe.GetCallSiteArgumentValueId(ins.Aux, 0);
                    // Drop the IsFixnum guard / use the C# literal for a constant operand (e.g.
                    // `@x & 0xFFFFF` -> `v1.FixnumValue & 1048575L`, only v1 guarded).
                    var condParts = new List<string>();
                    if (FixGuard(isLong, recvId) is { } gr) condParts.Add(gr);
                    if (FixGuard(isLong, argId) is { } gar) condParts.Add(gar);
                    if (isShift) condParts.Add($"(ulong){FixRead(isLong, argId)} <= 63UL");
                    var expr = isShift
                        ? $"{FixRead(isLong, recvId)} {binOp} (int){FixRead(isLong, argId)}"
                        : $"{FixRead(isLong, recvId)} {binOp} {FixRead(isLong, argId)}";
                    // & | ^ >> of fixnums always yield a fixnum, so a proven-fixnum dst lives in a raw
                    // long local (the else-Send is dead when operands are proven, but stays correct via
                    // .FixnumValue since the result is always a fixnum). Otherwise the dst stays boxed.
                    // The Send fallback needs boxed operands; BoxRead re-boxes a raw `long` operand.
                    var slowSend = $"state.Send({BoxRead(isLong, recvId)}, {sym.Reference(mname)}, {BoxRead(isLong, argId)})";
                    if (isLong[ins.Dst])
                    {
                        Line(body, condParts.Count == 0
                            ? $"l{Slot(ins.Dst)} = {expr};"
                            : $"if ({Cond(condParts)}) {{ l{Slot(ins.Dst)} = {expr}; }} else {{ l{Slot(ins.Dst)} = {slowSend}.FixnumValue; }}");
                        return true;
                    }
                    var d = Val(ins.Dst);
                    Line(body, $"if ({Cond(condParts)}) {{ {d} = new global::ChibiRuby.MRubyValue({expr}); }} else {{ {d} = {slowSend}; }}");
                    return true;
                }
                var call = new StringBuilder();
                call.Append(Val(ins.Dst)).Append(" = state.Send(").Append(Val(ins.Src0))
                    .Append(", ").Append(sym.Reference(mname));
                for (var i = 0; i < argc; i++)
                {
                    call.Append(", ").Append(Val(exe.GetCallSiteArgumentValueId(ins.Aux, i)));
                }
                call.Append(");");
                Line(body, call.ToString());
                return true;
            }

            // Indexed access. GetIndexUnsafe/SetIndexUnsafe inline the interpreter's
            // Array-fast-path (RArray + fixnum -> direct element) and fall back to
            // :[] / :[]= for everything else, so hot array code avoids full dispatch.
            case RubyIROpCode.GetIndex:
                if (TryScalarArray(ins.Src0, out var gCanon, out _) && ConstFix(ins.Src1, out var gidx))
                {
                    Line(body, $"{Val(ins.Dst)} = {ArrElem(gCanon, (int)gidx)};");
                    return true;
                }
                if (TryScalarHash(ins.Src0, out var ghCanon, out var ghKeys) && KeyTag(ins.Src1) is { } ghTag)
                {
                    Line(body, $"{Val(ins.Dst)} = {(ghKeys.Contains(ghTag) ? HashElem(ghCanon, ghTag) : "global::ChibiRuby.MRubyValue.Nil")};");
                    return true;
                }
                Line(body, $"{Val(ins.Dst)} = GetIndexUnsafe(state, {Val(ins.Src0)}, {Val(ins.Src1)});");
                return true;

            case RubyIROpCode.GetIndex0:
                if (TryScalarArray(ins.Src0, out var g0Canon, out _))
                {
                    Line(body, $"{Val(ins.Dst)} = {ArrElem(g0Canon, 0)};");
                    return true;
                }
                if (TryScalarHash(ins.Src0, out var gh0Canon, out var gh0Keys))
                {
                    Line(body, $"{Val(ins.Dst)} = {(gh0Keys.Contains("i0") ? HashElem(gh0Canon, "i0") : "global::ChibiRuby.MRubyValue.Nil")};");
                    return true;
                }
                Line(body, $"{Val(ins.Dst)} = GetIndexZeroUnsafe(state, {Val(ins.Src0)});");
                return true;

            // SetIdx writes the assigned value back to the receiver register (= Dst).
            case RubyIROpCode.SetIndex:
                if (TryScalarArray(ins.Src0, out var sCanon, out _) && ConstFix(ins.Src1, out var sidx))
                {
                    // a[i] = x  ->  element local = x;  (the op also yields x)
                    Line(body, $"{ArrElem(sCanon, (int)sidx)} = {Val(ins.Src2)};");
                    if (Val(ins.Dst) != Val(ins.Src2)) Line(body, $"{Val(ins.Dst)} = {Val(ins.Src2)};");
                    return true;
                }
                if (TryScalarHash(ins.Src0, out var shCanon, out _) && KeyTag(ins.Src1) is { } shTag)
                {
                    Line(body, $"{HashElem(shCanon, shTag)} = {Val(ins.Src2)};");
                    if (Val(ins.Dst) != Val(ins.Src2)) Line(body, $"{Val(ins.Dst)} = {Val(ins.Src2)};");
                    return true;
                }
                Line(body, $"{Val(ins.Dst)} = SetIndexUnsafe(state, {Val(ins.Src0)}, {Val(ins.Src1)}, {Val(ins.Src2)});");
                return true;

            // Array literal [a, b, c]. Elements are value ids in the operand list at Aux;
            // state.NewArray(ReadOnlySpan) is public and its RArray converts to MRubyValue.
            case RubyIROpCode.NewArray:
            {
                var n = exe.GetOperandListCount(ins.Aux);
                // Scalar-replaced literal: initialize the per-element locals, no RArray allocation.
                if (TryScalarArray(ins.Dst, out var nCanon, out _))
                {
                    for (var i = 0; i < n; i++)
                        Line(body, $"{ArrElem(nCanon, i)} = {Val(exe.GetOperandListValueId(ins.Aux, i))};");
                    return true;
                }
                var call = new StringBuilder();
                call.Append(Val(ins.Dst)).Append(" = state.NewArray(");
                if (n == 0)
                {
                    call.Append("global::System.ReadOnlySpan<global::ChibiRuby.MRubyValue>.Empty");
                }
                else
                {
                    // MRubyValue is a managed struct, so it can't be stackalloc'd; a heap
                    // temp is fine (NewArray copies the elements into the RArray anyway).
                    call.Append("new global::ChibiRuby.MRubyValue[] { ");
                    for (var i = 0; i < n; i++)
                    {
                        if (i > 0) call.Append(", ");
                        call.Append(Val(exe.GetOperandListValueId(ins.Aux, i)));
                    }
                    call.Append(" }");
                }
                call.Append(");");
                Line(body, call.ToString());
                return true;
            }

            // Hash literal {k0 => v0, ...} / scalar-replaced (constant-key) form.
            case RubyIROpCode.NewHash:
            {
                var nh = exe.GetOperandListCount(ins.Aux); // 2 * pairs
                var pairs = nh / 2;
                if (TryScalarHash(ins.Dst, out var hCanon, out _))
                {
                    for (var p = 0; p < pairs; p++)
                        Line(body, $"{HashElem(hCanon, KeyTag(exe.GetOperandListValueId(ins.Aux, 2 * p))!)} = {Val(exe.GetOperandListValueId(ins.Aux, 2 * p + 1))};");
                    return true;
                }
                var hb = new StringBuilder();
                hb.Append("{ var _h = state.NewHash(").Append(pairs).Append(");");
                for (var p = 0; p < pairs; p++)
                    hb.Append(" _h.Add(").Append(Val(exe.GetOperandListValueId(ins.Aux, 2 * p)))
                      .Append(", ").Append(Val(exe.GetOperandListValueId(ins.Aux, 2 * p + 1))).Append(");");
                hb.Append(' ').Append(Val(ins.Dst)).Append(" = new global::ChibiRuby.MRubyValue(_h); }");
                Line(body, hb.ToString());
                return true;
            }

            case RubyIROpCode.Jump:
                Line(body, $"goto L{ins.Aux};");
                return true;
            case RubyIROpCode.JumpIfTruthy:
                Line(body, $"if ({Val(ins.Src0)}.Truthy) goto L{ins.Aux};");
                return true;
            case RubyIROpCode.JumpIfFalsy:
                Line(body, $"if ({Val(ins.Src0)}.Falsy) goto L{ins.Aux};");
                return true;
            case RubyIROpCode.JumpIfNil:
                Line(body, $"if ({Val(ins.Src0)}.IsNil) goto L{ins.Aux};");
                return true;

            case RubyIROpCode.Return or RubyIROpCode.ReturnValue:
                Line(body, $"result = {BoxReadFull(isLong, isDouble, ins.Src0)}; return true;");
                return true;
            case RubyIROpCode.ReturnSelf:
                Line(body, $"result = {Val(0)}; return true;");
                return true;

            // Closure variable access, only valid while emitting an inlined block body where
            // the captured registers are ref-param cells. aux packs (register << 8) | depth.
            case RubyIROpCode.GetUpVar:
            {
                if (!TryResolveUpvarCell(ins.Aux, out var cell)) { LastBail = "upvar:unresolved"; return false; }
                Line(body, $"{Val(ins.Dst)} = {cell};");
                return true;
            }
            case RubyIROpCode.SetUpVar:
            {
                if (!TryResolveUpvarCell(ins.Aux, out var cell)) { LastBail = "upvar:unresolved"; return false; }
                Line(body, $"{cell} = {Val(ins.Src0)};");
                return true;
            }

            // `count.times do |i| ... end` -> a C# for loop calling the block body's __blk
            // method, with the block's captured method locals passed by ref.
            case RubyIROpCode.SendBlockDescriptor or RubyIROpCode.SendSelfBlockDescriptor:
                return TryEmitTimesLoop(state, exe, ins, sym, body);

            default:
                return false;
        }
    }

    // Resolve an upvar (register = aux >> 8, lv = aux & 0xff) to its ref-param cell. In a block
    // at level L, lv references the scope L-lv-1; the cell exists iff this block received it.
    static bool TryResolveUpvarCell(int aux, out string cell)
    {
        cell = "";
        if (BlockEmit is not { Cells: { } cells } || BlockEmit.CurrentLevel == 0) return false;
        var register = aux >> 8;
        var scope = BlockEmit.CurrentLevel - (aux & 0xff) - 1;
        if (scope < 0 || !cells.Contains((scope, register))) return false;
        cell = CellName(scope, register);
        return true;
    }

    static string CellName(int scope, int register) => $"cell_{scope}_{register}";

    static bool IsTimesSelector(MRubyState state, Symbol sym) =>
        state.NameOf(sym).AsSpan().SequenceEqual("times"u8);

    // `count.times do |i| ... end` -> a C# for loop. The block body becomes a separate __blk
    // method (so its value-ids never collide with the caller's). Variables it (or a nested
    // block) reads/writes from an enclosing scope are passed by ref as `cell_<scope>_<register>`.
    // The receiver is guarded fixnum (Integer#times); the loop returns the receiver. Nesting is
    // recursive: the child __blk's body re-enters this for its own nested times.
    static bool TryEmitTimesLoop(MRubyState state, RubyIRMethod exe, in RubyIRInstruction ins, SymbolCache sym, StringBuilder body)
    {
        if (BlockEmit is null) { LastBail = "block:noctx"; return false; }
        if (!IsTimesSelector(state, exe.GetCallSiteSymbol(ins.Aux))) { LastBail = "block:notTimes"; return false; }
        if (exe.GetCallSiteArgumentCount(ins.Aux) != 0) { LastBail = "block:iterArgs"; return false; }

        var level = BlockEmit.CurrentLevel;       // scope emitting the times
        var childLevel = level + 1;
        var child = exe.GetChildIrep(ins.Src1);
        if (!TryReadMandatoryArgCount(child, out var blockArgc) || blockArgc > 1) { LastBail = "block:argc"; return false; }

        // Coordinates the child (and its descendants) reference above the child's own scope.
        var needed = CollectBlockCells(state, child, childLevel);
        if (needed is null) return false; // LastBail set inside

        var n = BlockEmit.BlockCounter++;
        var blockName = BlockEmit.OwnerName + "__blk" + n;
        var src = TryGenerateBlockBody(state, child, blockName, needed, blockArgc, childLevel);
        if (src is null) return false; // LastBail set inside
        BlockEmit.AuxMethods.Add(src);

        var recv = ins.OpCode == RubyIROpCode.SendSelfBlockDescriptor ? Val(0) : Val(ins.Src0);
        var idx = "_bi" + n;
        // Pass each needed coordinate by ref: a coordinate in the CURRENT scope is one of this
        // body's own locals (v<register>); a coordinate above it is one of the cells THIS body
        // itself received (cell_<scope>_<register>).
        var refs = new StringBuilder();
        foreach (var (scope, register) in needed)
        {
            refs.Append(", ref ").Append(scope == level ? Val(register) : CellName(scope, register));
        }
        var argPass = blockArgc >= 1 ? $", new global::ChibiRuby.MRubyValue({idx})" : "";
        // The receiver-fixnum guard runs before the loop (no iteration has committed a side
        // effect yet), so deopting here is safe. A block body itself never deopts under
        // ForceSend except via a nested receiver guard, which is likewise pre-loop; propagate
        // it up so a (never-in-practice, Integer#times) miss can't silently skip iterations.
        Line(body, $"if (!{recv}.IsFixnum) {{ result = default; return false; }}");
        Line(body, $"for (long {idx} = 0; {idx} < {recv}.FixnumValue; {idx}++) {{ if (!{blockName}(state, {Val(0)}{argPass}{refs}, out var _bt{n})) {{ result = default; return false; }} }}");
        Line(body, $"{Val(ins.Dst)} = {recv};");
        return true;
    }

    // Collect the absolute coordinates (scope, register) that a block at `blockLevel` (and its
    // nested blocks) reads/writes from a scope ABOVE its own — these become the block's ref-param
    // cells. Returns null (and sets LastBail) if anything uninlinable appears (a non-times block,
    // a block passed as a proc, an unlowerable body).
    static SortedSet<(int Scope, int Register)>? CollectBlockCells(MRubyState state, Irep blockIrep, int blockLevel)
    {
        var result = new SortedSet<(int, int)>();
        return Walk(blockIrep, blockLevel) ? result : null;

        bool Walk(Irep irep, int level)
        {
            RubyIRMethod? exe;
            try { exe = RubyIRBuilder.Build(irep, 0, out _); }
            catch { exe = null; }
            if (exe is null) { LastBail = "block:childLower"; return false; }
            foreach (var bi in exe.Instructions)
            {
                switch (bi.OpCode)
                {
                    case RubyIROpCode.GetUpVar or RubyIROpCode.SetUpVar:
                        var scope = level - (bi.Aux & 0xff) - 1;
                        if (scope < 0) { LastBail = "block:badUpvar"; return false; }
                        if (scope < blockLevel) result.Add((scope, bi.Aux >> 8));
                        break;
                    case RubyIROpCode.SendBlockDescriptor or RubyIROpCode.SendSelfBlockDescriptor:
                        if (!IsTimesSelector(state, exe.GetCallSiteSymbol(bi.Aux)) ||
                            exe.GetCallSiteArgumentCount(bi.Aux) != 0)
                        {
                            LastBail = "block:nestedNonTimes";
                            return false;
                        }
                        if (!Walk(exe.GetChildIrep(bi.Src1), level + 1)) return false;
                        break;
                    case RubyIROpCode.LoadBlock or RubyIROpCode.SendBlock or RubyIROpCode.SendSelfBlock:
                        LastBail = "block:escapingBlock";
                        return false;
                }
            }
            return true;
        }
    }

    // Emit the block body as a standalone method: (state, self, [block param], ref cell_s_r...,
    // out result). Runs with ForceSend (no deopt — the body executes in a loop and must never
    // re-execute a partial iteration) and no unboxing/scalar replacement. Recurses for nested
    // times. Returns the full source (static sym fields + the method) or null if uncompilable.
    static string? TryGenerateBlockBody(MRubyState state, Irep blockIrep, string blockName, SortedSet<(int Scope, int Register)> cells, int blockArgc, int level)
    {
        RubyIRMethod? exe;
        try { exe = RubyIRBuilder.Build(blockIrep, 0, out _); }
        catch { exe = null; }
        if (exe is null) { LastBail = "block:childLower"; return null; }

        // Stack-allocate non-escaping objects constructed in THIS block body (ao's loop-local Ray
        // lives here, not in the enclosing method). SSA-renumber first so each value-id is single-
        // def (a clean struct-local mapping); Run keeps params (v0..vBlockArgc) and captured cell
        // ids stable, preserving the block param/cell-ref convention. CurrentStackObjects is thread-
        // static and shared with the enclosing method's emission (which continues after this block
        // returns), and block value-ids are a different numbering space than the parent's, so it is
        // saved here and restored before every return — never inherited into the block.
        var savedStackObjects = CurrentStackObjects;
        var savedCanonical = StructCanonical;
        var savedConstLit = CurrentConstLit;
        // Array scalar replacement is not wired into the block-body declaration path yet; disable it
        // here (the parent's map must not leak into the block's distinct value-id space).
        var savedScalarArrays = CurrentScalarArrays;
        var savedScalarHashes = CurrentScalarHashes;
        var savedHashKeyTags = CurrentHashKeyTags;
        var savedBlkProvesFixnum = CurrentProvesFixnum;
        var savedBlkSound = CurrentSoundProven;
        var savedBlkSlot = CurrentLocalSlot;
        CurrentScalarArrays = null;
        CurrentScalarHashes = null;
        CurrentLocalSlot = null; // no coalescing in block bodies (parent's slot map must not leak)
        if (StackObjEnabled && SsaEnabled && CurrentEscapeSummary is { } blockEsc)
        {
            exe = RubyIRSsaRenumber.Run(exe, blockArgc);
            CurrentStackObjects = FindStackEligible(state, exe, blockEsc);
            if (Environment.GetEnvironmentVariable("AOT_ESCAPE_DEBUG") == "1" && CurrentStackObjects is { Count: > 0 } dbg)
                foreach (var (objId, lay) in dbg)
                    System.Console.Error.WriteLine($"[stackobj] {blockName} v{objId} = new {state.NameOf(lay.ConstName)} -> {lay.StructType}");
        }
        else
        {
            CurrentStackObjects = null;
        }
        RebuildStructCanonical();
        CurrentConstLit = BuildConstLit(exe); // for the (possibly SSA-renumbered) block exe

        var instructions = exe.Instructions;
        var targets = new HashSet<int>();
        foreach (var ins in instructions)
        {
            if (ins.OpCode is RubyIROpCode.Jump or RubyIROpCode.JumpIfTruthy
                or RubyIROpCode.JumpIfFalsy or RubyIROpCode.JumpIfNil)
            {
                targets.Add(ins.Aux);
            }
        }

        // Sound, deopt-free unboxing for the block body. It is forward-only and runs under ForceSend
        // (numeric slow paths Send, never deopt); no speculation and no arg guards (a block deopt
        // re-runs the parent). Math-returning sends + float literals become raw double — this is
        // where ao's ambient_occlusion trig chain unboxes. Captured cells stay boxed (ref params).
        ComputeLoopUnboxing(state, exe, blockArgc, null,
            out var provesDouble, out var provesFixnum, out var floatTaint, out var soundProvenBlk, out _, speculateArgs: false);
        CurrentProvesFixnum = provesFixnum;
        CurrentSoundProven = soundProvenBlk;
        ComputeLoopRawLocals(state, exe, blockArgc, provesDouble, provesFixnum, out var isLong, out var isDouble);
        var bsym = new SymbolCache(blockName);

        var savedCells = BlockEmit!.Cells;
        var savedLevel = BlockEmit.CurrentLevel;
        var savedForce = BlockEmit.ForceSend;
        BlockEmit.Cells = new HashSet<(int, int)>(cells);
        BlockEmit.CurrentLevel = level;
        BlockEmit.ForceSend = true;
        // Block bodies never self-inline other Ruby methods (fiber-unsafe), but they can still
        // use guarded accessor devirt and pure-unary C# method caches.
        var bic = new InlineContext(state, null, null, BlockEmit.AccessorRegistry, CurrentConstReturns, blockName, bsym, exe);
        // The cast cache is per C# method; the block body is a separate method, so save the
        // parent's and give the block a fresh one (restored after).
        var savedAsObj = AsObjCache;
        var savedAsCounter = AsObjCounter;
        AsObjCache = new Dictionary<int, string>();
        AsObjCounter = 0;
        // Same for the ivar-read CSE cache: the block is a separate C# method.
        var savedIvarCache = ivarGetCache;
        var savedIvCounter = ivCounter;
        var savedReused = reusedIvarReads;
        ivarGetCache = new Dictionary<(int, Symbol), string>();
        ivCounter = 0;
        reusedIvarReads = ComputeReusedIvarReads(exe, targets);
        var body = new StringBuilder();
        var ok = true;
        for (var i = 0; i < instructions.Length; i++)
        {
            if (targets.Contains(i)) { body.Append("    L").Append(i).Append(": ;\n"); ClearAsObjCache(); ClearIvarGetCache(); }
            if (!EmitInstruction(state, exe, instructions[i], bsym, body, ic: bic, sc: null, isLong, floatTaint, isDouble, provesDouble))
            {
                LastBail ??= "block-op:" + instructions[i].OpCode;
                ok = false;
                break;
            }
            InvalidateAsObj(instructions[i].Dst);
            InvalidateIvarGet(instructions[i]);
        }
        AsObjCache = savedAsObj;
        AsObjCounter = savedAsCounter;
        ivarGetCache = savedIvarCache;
        ivCounter = savedIvCounter;
        reusedIvarReads = savedReused;
        BlockEmit.Cells = savedCells;
        BlockEmit.CurrentLevel = savedLevel;
        BlockEmit.ForceSend = savedForce;
        if (!ok) { CurrentStackObjects = savedStackObjects; StructCanonical = savedCanonical; CurrentConstLit = savedConstLit; CurrentScalarArrays = savedScalarArrays; CurrentScalarHashes = savedScalarHashes; CurrentHashKeyTags = savedHashKeyTags; CurrentProvesFixnum = savedBlkProvesFixnum; CurrentSoundProven = savedBlkSound; CurrentLocalSlot = savedBlkSlot; return null; }

        var sb = new StringBuilder();
        EmitSymbolFields(bsym, sb);
        EmitInlineFields(bic, sb);
        sb.Append("public static bool ").Append(blockName).Append("(global::ChibiRuby.MRubyState state, global::ChibiRuby.MRubyValue ").Append(Val(0));
        if (blockArgc >= 1) sb.Append(", global::ChibiRuby.MRubyValue ").Append(Val(1));
        foreach (var (scope, register) in cells) sb.Append(", ref global::ChibiRuby.MRubyValue ").Append(CellName(scope, register));
        sb.Append(", out global::ChibiRuby.MRubyValue result)\n{\n");

        // Locals for the block's own non-param value-ids (self is v0, params are v1..vBlockArgc).
        // Captured variables are separate cell_s_r ref params, not value-ids. Stack objects are
        // struct locals (declared separately below). Proven-numeric temps are raw long/double.
        var blkBoxed = new List<int>();
        var blkLong = new List<int>();
        var blkDouble = new List<int>();
        for (var v = blockArgc + 1; v < exe.ValueCount; v++)
        {
            if (CurrentStackObjects is { } cso0 && cso0.ContainsKey(v)) continue;
            (isDouble[v] ? blkDouble : isLong[v] ? blkLong : blkBoxed).Add(v);
        }
        if (blkBoxed.Count > 0)
        {
            sb.Append("    global::ChibiRuby.MRubyValue ");
            for (var i = 0; i < blkBoxed.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Val(blkBoxed[i])).Append(" = default"); }
            sb.Append(";\n");
        }
        if (blkLong.Count > 0)
        {
            sb.Append("    long ");
            for (var i = 0; i < blkLong.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('l').Append(blkLong[i]).Append(" = 0"); }
            sb.Append(";\n");
        }
        if (blkDouble.Count > 0)
        {
            sb.Append("    double ");
            for (var i = 0; i < blkDouble.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('d').Append(blkDouble[i]).Append(" = 0"); }
            sb.Append(";\n");
        }
        if (CurrentStackObjects is { } cso1)
        {
            var declaredStructs = new HashSet<string>(); // canonical mutated aliases share one local
            foreach (var (v, lay) in cso1)
                if (v > blockArgc && declaredStructs.Add(Val(v)))
                    sb.Append("    ").Append(lay.StructType).Append(' ').Append(Val(v)).Append(" = default;\n");
        }

        EmitSymbolInit(bsym, sb);
        sb.Append(body);
        if (targets.Contains(instructions.Length)) sb.Append("    L").Append(instructions.Length).Append(": ;\n");
        sb.Append("    result = default; return false;\n}\n");
        CurrentStackObjects = savedStackObjects;
        StructCanonical = savedCanonical;
        CurrentConstLit = savedConstLit;
        CurrentScalarArrays = savedScalarArrays;
        CurrentScalarHashes = savedScalarHashes;
        CurrentHashKeyTags = savedHashKeyTags;
        CurrentProvesFixnum = savedBlkProvesFixnum;
        CurrentSoundProven = savedBlkSound;
        CurrentLocalSlot = savedBlkSlot;
        return sb.ToString();
    }

    // Read value-id `id` as a double in a float context: the constant literal if known, else the
    // boxed value's .FloatValue. (A Long-unboxed value never reaches a float path.)
    static string FloatRead(int id) =>
        ConstFloat(id, out var cv) ? DoubleLitText(cv) : "v" + Slot(id) + ".FloatValue";

    // Guard asserting `id` is a float, or null for a known float constant (a non-float constant
    // keeps `v.IsFloat`, which folds to false, so the float branch stays correctly unreachable).
    static string? FloatGuard(int id) => ConstFloat(id, out _) ? null : "v" + Slot(id) + ".IsFloat";
    static string FloatCond(params int[] ids)
    {
        var parts = new List<string>();
        foreach (var id in ids) if (FloatGuard(id) is { } g) parts.Add(g);
        return Cond(parts);
    }

    // C# text for a double constant: a plain `ldc.r8` literal via round-trippable formatting, or the
    // bit-exact reconstruction for NaN/Inf/non-roundtrippable values. (Shared with TryEmitLiteral.)
    internal static string DoubleLitText(double d)
    {
        if (double.IsFinite(d))
        {
            var s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rt) &&
                BitConverter.DoubleToInt64Bits(rt) == BitConverter.DoubleToInt64Bits(d))
            {
                return s + "D";
            }
        }
        var bits = BitConverter.DoubleToInt64Bits(d);
        return $"global::System.BitConverter.Int64BitsToDouble(unchecked((long)0x{(ulong)bits:x16}UL))";
    }

    // Read value-id `id` as a double in a PURE-double op (all operands provably Float). A raw
    // `double` local reads directly; an unboxed long is a proven fixnum promoted to double; any
    // other (provably-float but boxed, e.g. a literal/ivar seed) reads .FloatValue without a guard.
    static string DoubleRead(bool[] isLong, bool[] isDouble, int id) =>
        isDouble[id] ? "d" + Slot(id) : isLong[id] ? "(double)l" + Slot(id) : "v" + Slot(id) + ".FloatValue";

    // Read value-id `id` as a boxed MRubyValue, re-boxing any unboxed (long/double) kind — for the
    // return boundary, where a value of any representation crosses back into the boxed world.
    static string BoxReadFull(bool[] isLong, bool[] isDouble, int id) =>
        isDouble[id] ? Box("d" + Slot(id)) : isLong[id] ? Box("l" + Slot(id)) : Val(id);

    // Q1.2: after a speculated float ivar/accessor read, assert it really is a Float; a miss deopts
    // (safe — only seeded in side-effect-free methods, so re-running in the interpreter is harmless).
    static void EmitFloatSpeculationGuard(StringBuilder body, bool[] provesDouble, int dst)
    {
        // Sound proofs (a proven-Float ivar read / send / literal) need no guard — only an actual
        // SPECULATION (the pre-side-effect-window guess) does.
        var sound = CurrentSoundProven;
        if ((uint)dst < (uint)provesDouble.Length && provesDouble[dst] &&
            !(sound is not null && (uint)dst < (uint)sound.Length && sound[dst]))
        {
            Line(body, $"if (!{Val(dst)}.IsFloat) {{ result = default; return false; }}");
        }
    }

    static bool ProvesFixnum(int id) =>
        CurrentProvesFixnum is { } pf && (uint)id < (uint)pf.Length && pf[id];

    // Read id as a `double` in a guard-free mixed-numeric float expression: a proven Float via
    // .FloatValue (or its float constant / raw d-local / (double)l-local), or a proven Fixnum via
    // (double)v.FixnumValue (or (double) of its fixnum constant / l-local).
    static string DoubleReadNumeric(bool[] isLong, bool[] isDouble, bool[] provesDouble, int id)
    {
        if (provesDouble[id]) return DoubleRead(isLong, isDouble, id);
        if (ConstFix(id, out var cv)) return "(double)" + cv + "L";
        if (isLong[id]) return "(double)l" + Slot(id);
        return "(double)v" + Slot(id) + ".FixnumValue";
    }

    // True iff every operand is provably numeric (Float or Fixnum, in any representation) and at
    // least one is provably Float — so `a OP b` is guard-free `double` arith under Ruby coercion.
    static bool MixedNumericFloat(bool[] isLong, bool[] provesDouble, params int[] ids)
    {
        var anyFloat = false;
        foreach (var id in ids)
        {
            var isFloat = provesDouble[id] || ConstFloat(id, out _);
            var isFix = ProvesFixnum(id) || isLong[id] || ConstFix(id, out _);
            if (!isFloat && !isFix) return false;
            anyFloat |= isFloat;
        }
        return anyFloat;
    }

    static string True => "global::ChibiRuby.MRubyValue.True";
    static string False => "global::ChibiRuby.MRubyValue.False";
    static string Box(string expr) => $"new global::ChibiRuby.MRubyValue({expr})";

    // Direct internal-runtime expressions the generated code emits (it has InternalsVisibleTo).
    // Emit calls to the AotGeneratedMethods base-class helpers (inherited by the generated class),
    // not raw internal access — so RObject.InstanceVariables can stay internal. The helpers are
    // aggressively inlined, so this is free at runtime.
    // recv must be an RObject-typed expression (a cached __ro local, a fresh `new RObject`, or
    // `x.As<RObject>()`). InstanceVariables is public, so emit the table access directly.
    internal static string IvarGet(string recv, string symRef) =>
        $"{recv}.InstanceVariables.Get({symRef})";
    internal static string IvarSet(string recv, string symRef, string value) =>
        $"{recv}.InstanceVariables.Set({symRef}, {value})";

    // Read value-id `id` as a boxed MRubyValue (re-box an unboxed long) — for the slow-path
    // Send, whose arguments must be boxed.
    static string BoxRead(bool[] isLong, int id) => isLong[id] ? Box("l" + Slot(id)) : Val(id);

    // C# string literal for an operator selector (+, -, *, /, <, ==, ...). All chars are
    // printable ASCII with no quote/backslash, so no escaping is needed.
    static string OpLit(string op) => $"\"{op}\"";

    // Slow path for a numeric binary whose two fast branches (both-fixnum, both-float) missed.
    // Two strategies, chosen by whether the op is genuinely mixed-type:
    //  - Send fallback (mixedType): a real Send to the operator. Keeps the method RUNNING — a
    //    deopt re-runs the whole method in the interpreter, double-applying any side effect
    //    already committed (e.g. an RNG that advanced its ivars before a `fixnum / float`).
    //    Used only when the operands have mismatched float-taint, i.e. the slow path actually
    //    fires every call. A Send is a call site, so emitting it on the HOT (always-fast)
    //    monomorphic ops would force the JIT to spill around a never-taken branch (~2x slower
    //    on optcarrot), hence:
    //  - deopt (return false) otherwise: the operands are same-typed (both fixnum, or both
    //    float), so this branch is cold/unreached in monomorphic code and re-execution is
    //    harmless. This is the original, fast behavior for integer/float-homogeneous methods.
    static string NumericSlow(SymbolCache sym, bool[] isLong, bool mixedType, int dst, string op, int a, int b) =>
        mixedType || (BlockEmit?.ForceSend ?? false)
            ? $"{Val(dst)} = state.Send({BoxRead(isLong, a)}, {sym.Reference(OpLit(op))}, {BoxRead(isLong, b)});"
            : "result = default; return false;";

    // True when the op's operands carry mismatched float-taint — one is statically float, the
    // other isn't — so the dual fast path provably misses every call (genuine fixnum/float mix).
    static bool MixedTaint(bool[] floatTaint, int a, int b) => floatTaint[a] != floatTaint[b];
    static bool MixedTaint(bool[] floatTaint, int a, int b, int c) =>
        !(floatTaint[a] == floatTaint[b] && floatTaint[b] == floatTaint[c]);

    // Generic numeric binary: runtime dual path. A Long-typed dst is float-untainted by
    // construction, so it keeps the fixnum-only path. For a boxed dst, emit both-fixnum and
    // both-float branches; the miss (mixed / non-numeric / fixnum div-by-zero) takes the slow
    // path (Send for genuinely-mixed ops, deopt otherwise). The float branch is suppressed when
    // any operand is an unboxed long (provably fixnum, so it can never pair with a float here).
    static bool EmitNumericBinary(SymbolCache sym, StringBuilder body, in RubyIRInstruction ins, string op, bool[] isLong, bool[] floatTaint, bool[] isDouble, bool[] provesDouble, bool isDiv = false)
    {
        int a = ins.Src0, b = ins.Src1, d = ins.Dst;
        // Pure-double: both operands provably Float -> guard-free raw-double arith. dst is a raw
        // `double` if it too is double-unboxed, else re-boxed at this boundary.
        if (provesDouble[a] && provesDouble[b])
        {
            var dexpr = $"{DoubleRead(isLong, isDouble, a)} {op} {DoubleRead(isLong, isDouble, b)}";
            Line(body, isDouble[d] ? $"d{Slot(d)} = {dexpr};" : $"{Val(d)} = {Box(dexpr)};");
            return true;
        }
        // Looping-method mixed numeric: one operand proven Float, the other proven Fixnum -> Ruby
        // coerces to Float, so emit guard-free double arith (reading the fixnum operand as (double)).
        if (CurrentProvesFixnum is not null && !isDiv && MixedNumericFloat(isLong, provesDouble, a, b))
        {
            var dexpr = $"{DoubleReadNumeric(isLong, isDouble, provesDouble, a)} {op} {DoubleReadNumeric(isLong, isDouble, provesDouble, b)}";
            Line(body, isDouble[d] ? $"d{Slot(d)} = {dexpr};" : $"{Val(d)} = {Box(dexpr)};");
            return true;
        }
        // Mixed numeric division: Float result iff a Float operand is present (Ruby), so the same
        // guard-free double path applies — but only when NOT both-fixnum (integer division differs).
        if (CurrentProvesFixnum is not null && isDiv && MixedNumericFloat(isLong, provesDouble, a, b))
        {
            var dexpr = $"{DoubleReadNumeric(isLong, isDouble, provesDouble, a)} / {DoubleReadNumeric(isLong, isDouble, provesDouble, b)}";
            Line(body, isDouble[d] ? $"d{Slot(d)} = {dexpr};" : $"{Val(d)} = {Box(dexpr)};");
            return true;
        }
        var fixExpr = $"{FixRead(isLong, a)} {op} {FixRead(isLong, b)}";
        if (isLong[d])
        {
            EmitGuard(body, FixGuard(isLong, a), FixGuard(isLong, b),
                isDiv ? $"{FixRead(isLong, b)} != 0" : null);
            AssignFix(isLong, body, d, fixExpr);
            return true;
        }
        var fixParts = new List<string>();
        if (FixGuard(isLong, a) is { } ga) fixParts.Add(ga);
        if (FixGuard(isLong, b) is { } gb) fixParts.Add(gb);
        if (isDiv) fixParts.Add($"{FixRead(isLong, b)} != 0");
        var line = new StringBuilder(
            $"if ({Cond(fixParts)}) {{ {Val(d)} = {Box(fixExpr)}; }}");
        if (!isLong[a] && !isLong[b])
        {
            line.Append($" else if ({FloatCond(a, b)}) {{ {Val(d)} = {Box($"{FloatRead(a)} {op} {FloatRead(b)}")}; }}");
        }
        line.Append($" else {{ {NumericSlow(sym, isLong, MixedTaint(floatTaint, a, b), d, op, a, b)} }}");
        Line(body, line.ToString());
        return true;
    }

    // SubMul (reverse=true) is src2 - src0*src1; MulAdd/MulSub are src0*src1 +/- src2.
    static bool EmitNumericFused(SymbolCache sym, StringBuilder body, in RubyIRInstruction ins, bool[] isLong, bool[] floatTaint, bool[] isDouble, bool[] provesDouble, string op, bool reverse)
    {
        int a = ins.Src0, b = ins.Src1, c = ins.Src2, d = ins.Dst;
        // Pure-double fused multiply-add/sub: all three operands provably Float -> raw double.
        if (provesDouble[a] && provesDouble[b] && provesDouble[c])
        {
            var prod = $"{DoubleRead(isLong, isDouble, a)} * {DoubleRead(isLong, isDouble, b)}";
            var dexpr = reverse ? $"{DoubleRead(isLong, isDouble, c)} - {prod}" : $"{prod} {op} {DoubleRead(isLong, isDouble, c)}";
            Line(body, isDouble[d] ? $"d{Slot(d)} = {dexpr};" : $"{Val(d)} = {Box(dexpr)};");
            return true;
        }
        // Looping-method mixed numeric fused (>=1 Float, rest Fixnum) -> guard-free double, reading
        // each fixnum operand as (double). `a*b OP c` / `c - a*b` is Float when any operand is Float.
        if (CurrentProvesFixnum is not null && MixedNumericFloat(isLong, provesDouble, a, b, c))
        {
            var prod = $"{DoubleReadNumeric(isLong, isDouble, provesDouble, a)} * {DoubleReadNumeric(isLong, isDouble, provesDouble, b)}";
            var cExpr = DoubleReadNumeric(isLong, isDouble, provesDouble, c);
            var dexpr = reverse ? $"{cExpr} - {prod}" : $"{prod} {op} {cExpr}";
            Line(body, isDouble[d] ? $"d{Slot(d)} = {dexpr};" : $"{Val(d)} = {Box(dexpr)};");
            return true;
        }
        string Fixed()
        {
            var product = $"{FixRead(isLong, a)} * {FixRead(isLong, b)}";
            return reverse ? $"{FixRead(isLong, c)} - {product}" : $"{product} {op} {FixRead(isLong, c)}";
        }
        if (isLong[d])
        {
            EmitGuard(body, FixGuard(isLong, a), FixGuard(isLong, b), FixGuard(isLong, c));
            AssignFix(isLong, body, d, Fixed());
            return true;
        }
        var fixParts = new List<string>();
        foreach (var id in new[] { a, b, c })
        {
            if (FixGuard(isLong, id) is { } g) fixParts.Add(g);
        }
        var line = new StringBuilder($"if ({Cond(fixParts)}) {{ {Val(d)} = {Box(Fixed())}; }}");
        if (!isLong[a] && !isLong[b] && !isLong[c])
        {
            var fp = $"{FloatRead(a)} * {FloatRead(b)}";
            var fexpr = reverse ? $"{FloatRead(c)} - {fp}" : $"{fp} {op} {FloatRead(c)}";
            line.Append($" else if ({FloatCond(a, b, c)}) {{ {Val(d)} = {Box(fexpr)}; }}");
        }
        if (MixedTaint(floatTaint, a, b, c) || (BlockEmit?.ForceSend ?? false))
        {
            // Slow path: a*b then op c, both via Send (matches the unfused bytecode order).
            var prod = $"state.Send({BoxRead(isLong, a)}, {sym.Reference(OpLit("*"))}, {BoxRead(isLong, b)})";
            var slow = reverse
                ? $"{Val(d)} = state.Send({BoxRead(isLong, c)}, {sym.Reference(OpLit(op))}, {prod});"
                : $"{Val(d)} = state.Send({prod}, {sym.Reference(OpLit(op))}, {BoxRead(isLong, c)});";
            line.Append($" else {{ {slow} }}");
        }
        else
        {
            line.Append(" else { result = default; return false; }");
        }
        Line(body, line.ToString());
        return true;
    }

    static bool EmitFixnumImmediate(SymbolCache sym, StringBuilder body, RubyIRMethod exe, in RubyIRInstruction ins, string op, bool[] isLong)
    {
        var imm = exe.GetLiteral(ins.Aux);
        if (!imm.IsFixnum)
        {
            // Float immediate (rare for AddI/SubI) -> let it bail so the method interprets.
            return false;
        }
        int a = ins.Src0, d = ins.Dst;
        var fixExpr = $"{FixRead(isLong, a)} {op} {imm.FixnumValue}L";
        if (isLong[d])
        {
            EmitGuard(body, FixGuard(isLong, a));
            AssignFix(isLong, body, d, fixExpr);
            return true;
        }
        var fixParts = new List<string>();
        if (FixGuard(isLong, a) is { } ga) fixParts.Add(ga);
        var line = new StringBuilder($"if ({Cond(fixParts)}) {{ {Val(d)} = {Box(fixExpr)}; }}");
        if (!isLong[a])
        {
            // Float receiver + fixnum immediate -> Ruby coerces to float (e.g. 2.5 + 3 == 5.5).
            line.Append($" else if ({FloatCond(a)}) {{ {Val(d)} = {Box($"{FloatRead(a)} {op} {imm.FixnumValue}d")}; }}");
        }
        // Both fixnum and float receivers are covered above, so the miss is non-numeric (rare).
        // Under ForceSend (loop/block bodies) the miss must Send, not deopt: a deopt re-runs the
        // whole method from the start, double-applying any side effect already committed earlier in
        // the iteration. Otherwise deopt is fine (re-execution only matters when the miss fires).
        if (BlockEmit?.ForceSend ?? false)
        {
            line.Append($" else {{ {Val(d)} = state.Send({BoxRead(isLong, a)}, {sym.Reference(OpLit(op))}, {Box($"{imm.FixnumValue}L")}); }}");
        }
        else
        {
            line.Append(" else { result = default; return false; }");
        }
        Line(body, line.ToString());
        return true;
    }

    static bool EmitNumericCompare(SymbolCache sym, StringBuilder body, in RubyIRInstruction ins, string op, bool[] isLong, bool[] floatTaint, bool[] isDouble, bool[] provesDouble)
    {
        int a = ins.Src0, b = ins.Src1, d = ins.Dst; // dst is a bool result, never long
        // Pure-double compare: both operands provably Float -> guard-free raw-double compare.
        if (provesDouble[a] && provesDouble[b])
        {
            Line(body, $"{Val(d)} = ({DoubleRead(isLong, isDouble, a)} {op} {DoubleRead(isLong, isDouble, b)}) ? {True} : {False};");
            return true;
        }
        // Looping-method mixed numeric compare (one Float, one Fixnum) -> guard-free double compare.
        if (CurrentProvesFixnum is not null && MixedNumericFloat(isLong, provesDouble, a, b))
        {
            Line(body, $"{Val(d)} = ({DoubleReadNumeric(isLong, isDouble, provesDouble, a)} {op} {DoubleReadNumeric(isLong, isDouble, provesDouble, b)}) ? {True} : {False};");
            return true;
        }
        var fixParts = new List<string>();
        if (FixGuard(isLong, a) is { } ga) fixParts.Add(ga);
        if (FixGuard(isLong, b) is { } gb) fixParts.Add(gb);
        var fixExpr = $"({FixRead(isLong, a)} {op} {FixRead(isLong, b)})";
        var line = new StringBuilder(
            $"if ({Cond(fixParts)}) {{ {Val(d)} = {fixExpr} ? {True} : {False}; }}");
        if (!isLong[a] && !isLong[b])
        {
            line.Append($" else if ({FloatCond(a, b)}) {{ {Val(d)} = ({FloatRead(a)} {op} {FloatRead(b)}) ? {True} : {False}; }}");
        }
        line.Append($" else {{ {NumericSlow(sym, isLong, MixedTaint(floatTaint, a, b), d, op, a, b)} }}");
        Line(body, line.ToString());
        return true;
    }

    // Join guard parts with && for a branch condition; "true" when there are none (both
    // operands unboxed long -> the fixnum branch is unconditional).
    static string Cond(List<string> parts) => parts.Count > 0 ? string.Join(" && ", parts) : "true";

    // For a MUTATED (by-ref) stack object, its Move-copy aliases must share ONE struct local, or a
    // mutation through one alias (`ref so90`) wouldn't be visible at a read through another (`so89`)
    // — value copies don't alias. Map every mutated-object alias id to a canonical id (the min of
    // the group, grouped by shared layout INSTANCE = object identity). Read-only objects keep
    // separate value-copy aliases (cheaper, and correct since they're never mutated). Rebuilt
    // whenever CurrentStackObjects is finalized.
    [ThreadStatic] internal static Dictionary<int, int>? StructCanonical;
    internal static void RebuildStructCanonical()
    {
        StructCanonical = null;
        if (CurrentStackObjects is not { Count: > 0 } cso) return;
        var mut = new List<int>();
        foreach (var (id, lay) in cso) if (lay.Mutated) mut.Add(id);
        if (mut.Count == 0) return;
        var map = new Dictionary<int, int>();
        foreach (var id in mut)
        {
            var lay = cso[id];
            var canon = id;
            foreach (var other in mut) if (ReferenceEquals(cso[other], lay) && other < canon) canon = other;
            map[id] = canon;
        }
        StructCanonical = map;
    }

    // Build a stack struct in place: each field <- its ctor arg, per FieldKind (Stage 1: all Boxed).
    static void EmitStackConstruct(RubyIRMethod exe, in RubyIRInstruction ins, StackLayout lay, StringBuilder body)
    {
        // Any struct we BUILD needs its type declared, even if it is consumed by an inlined/spliced
        // reader (no variant dispatch) rather than passed as an arg — TryEmitStackArgSend would
        // otherwise be the only registrar and miss this object.
        NeededStructs ??= new();
        NeededStructs[lay.ClassFp] = lay;
        var so = Val(ins.Dst);
        for (var f = 0; f < lay.Fields.Count; f++)
        {
            // Nested field: build the inner struct in place (literal-filled, no heap alloc).
            if (lay.FieldKinds[f] == StackFieldKind.Nested)
            {
                EmitNestedFill($"{so}.f{f}", lay.FieldNested[f]!, body);
                continue;
            }
            // A literal-initialized field (FieldArg == -1) builds from the constant; otherwise
            // from ctor arg FieldArg[f]. Boxed fields take the value as-is; ② unboxes.
            var src = lay.FieldArg[f] < 0
                ? (TryEmitLiteral(lay.FieldLiteral[f], out var lit) ? lit : "global::ChibiRuby.MRubyValue.Nil")
                : Val(exe.GetCallSiteArgumentValueId(ins.Aux, lay.FieldArg[f]));
            var rhs = lay.FieldKinds[f] switch
            {
                StackFieldKind.Double => src + ".FloatValue",       // ②
                StackFieldKind.Long => src + ".IntegerValue",       // ②
                _ => src,
            };
            Line(body, $"{so}.f{f} = {rhs};");
        }
    }

    // Fill a (literal-only) nested struct field in place: `target.fj = <literal>` for each inner
    // field, recursing into deeper Nested fields. `lay` is a TryBuildNestedFill clone (all fields
    // literal or Nested).
    static void EmitNestedFill(string target, StackLayout lay, StringBuilder body)
    {
        for (var j = 0; j < lay.Fields.Count; j++)
        {
            if (lay.FieldKinds[j] == StackFieldKind.Nested)
            {
                EmitNestedFill($"{target}.f{j}", lay.FieldNested[j]!, body);
                continue;
            }
            var lit = TryEmitLiteral(lay.FieldLiteral[j], out var l) ? l : "global::ChibiRuby.MRubyValue.Nil";
            Line(body, lay.FieldKinds[j] switch
            {
                StackFieldKind.Double => $"    {target}.f{j} = {lit}.FloatValue;",
                StackFieldKind.Long => $"    {target}.f{j} = {lit}.IntegerValue;",
                _ => $"    {target}.f{j} = {lit};",
            });
        }
    }

    // Materialize a stack struct into a real heap RObject (reify) — for escape/deopt fallback.
    // Recursive: Nested fields reify inner structs, Double/Long re-box. Returns a C# MRubyValue
    // expression; `rootLocal` is the top-level RObject local (for copy-back after a mutating Send).
    internal static string EmitReify(MRubyState state, StackLayout lay, string structExpr, SymbolCache sym, StringBuilder body, ref int tmpCounter, out string rootLocal)
    {
        var tmp = "__reify" + tmpCounter++;
        rootLocal = tmp;
        Line(body, $"var {tmp} = new global::ChibiRuby.RObject(GetConstantUnsafe(state, {sym.Reference(StringLit(state, lay.ConstName))}).As<global::ChibiRuby.RClass>());");
        for (var f = 0; f < lay.Fields.Count; f++)
        {
            if (!TrySymbolStringLiteral(state, lay.Fields[f], out var fieldLit)) continue;
            var fieldExpr = lay.FieldKinds[f] switch
            {
                StackFieldKind.Double => $"new global::ChibiRuby.MRubyValue({structExpr}.f{f})",
                StackFieldKind.Long => $"new global::ChibiRuby.MRubyValue({structExpr}.f{f})",
                StackFieldKind.Nested => EmitReify(state, lay.FieldNested[f]!, $"{structExpr}.f{f}", sym, body, ref tmpCounter, out _),
                _ => $"{structExpr}.f{f}",
            };
            Line(body, $"{tmp}.InstanceVariables.Set({sym.Reference(fieldLit)}, {fieldExpr});");
        }
        return $"new global::ChibiRuby.MRubyValue({tmp})";
    }

    // Copy a (possibly mutated) heap RObject's fields back into a stack struct — the deopt/reify
    // fallback for a by-`ref` mutated arg (A), so post-call reads of the struct see the Send's
    // mutations. Mirror of EmitReify (heap -> struct). Nested fields recurse through the heap
    // object's nested ivar. `roLocal` is an RObject-typed local.
    internal static void EmitCopyBack(MRubyState state, StackLayout lay, string structExpr, string roLocal, SymbolCache sym, StringBuilder body, ref int tmpCounter)
    {
        for (var f = 0; f < lay.Fields.Count; f++)
        {
            if (!TrySymbolStringLiteral(state, lay.Fields[f], out _)) continue;
            var get = $"{roLocal}.InstanceVariables.Get({sym.Reference(StringLit(state, lay.Fields[f]))})";
            if (lay.FieldKinds[f] == StackFieldKind.Nested)
            {
                var inner = "__cb" + tmpCounter++;
                Line(body, $"var {inner} = {get}.As<global::ChibiRuby.RObject>();");
                EmitCopyBack(state, lay.FieldNested[f]!, $"{structExpr}.f{f}", inner, sym, body, ref tmpCounter);
                continue;
            }
            Line(body, lay.FieldKinds[f] switch
            {
                StackFieldKind.Double => $"    {structExpr}.f{f} = {get}.FloatValue;",
                StackFieldKind.Long => $"    {structExpr}.f{f} = {get}.IntegerValue;",
                _ => $"    {structExpr}.f{f} = {get};",
            });
        }
    }

    static string StringLit(MRubyState state, Symbol s) => TrySymbolStringLiteral(state, s, out var lit) ? lit : "\"\"";

    // In a variant: a trivial-accessor send on the struct param lowers to a struct-field read/write.
    // A trivial accessor send whose RECEIVER is a stack object (a variant's struct param / its
    // alias, or a block/method stack-allocated object) -> direct struct field read/write.
    static bool TryEmitStackAccessor(MRubyState state, RubyIRMethod exe, in RubyIRInstruction ins, SymbolCache sym, StringBuilder body)
    {
        if (CurrentStackObjects is not { } cso || !cso.TryGetValue(ins.Src0, out var lay)) return false;
        var sel = exe.GetCallSiteSymbol(ins.Aux);
        if (!state.TryFindMethod(lay.Cls, sel, out var m, out _) || m.Proc is not { } pr) return false;
        if (TryRecognizeTrivialAccessor(state, pr.Irep) is not { } acc) return false;
        var fi = lay.FieldIndexOf(acc.Field);
        if (fi < 0) return false;
        var so = Val(ins.Src0); // the receiver's struct local
        if (acc.IsSetter)
        {
            if (!lay.Mutated) return false; // read-only `in` param can't be written (Stage 1)
            var arg = exe.GetCallSiteArgumentValueId(ins.Aux, 0);
            if (lay.FieldKinds[fi] == StackFieldKind.Nested)
            {
                if (cso.ContainsKey(arg))
                {
                    Line(body, $"{so}.f{fi} = {Val(arg)};"); // arg is itself a stack struct -> value copy
                }
                else
                {
                    // arg is a heap object (e.g. a computed Vec) -> copy its fields into the nested
                    // struct field. Block-scoped temps so repeated setters don't collide.
                    Line(body, "{");
                    Line(body, $"    var __ci = {Val(arg)}.As<global::ChibiRuby.RObject>();");
                    var t = 0;
                    EmitCopyBack(state, lay.FieldNested[fi]!, $"{so}.f{fi}", "__ci", sym, body, ref t);
                    Line(body, "}");
                }
                Line(body, $"{Val(ins.Dst)} = {Val(arg)};");
                return true;
            }
            var rhs = lay.FieldKinds[fi] switch
            {
                StackFieldKind.Double => Val(arg) + ".FloatValue",
                StackFieldKind.Long => Val(arg) + ".IntegerValue",
                _ => Val(arg),
            };
            Line(body, $"{so}.f{fi} = {rhs};");
            Line(body, $"{Val(ins.Dst)} = {Val(arg)};");
            return true;
        }
        if (lay.FieldKinds[fi] == StackFieldKind.Nested)
        {
            if (cso.ContainsKey(ins.Dst))
            {
                Line(body, $"{Val(ins.Dst)} = {so}.f{fi};"); // dst tracked as a stack struct -> value copy
            }
            else
            {
                // dst is boxed -> reify the nested struct into a heap object. Block-scoped temps.
                Line(body, "{");
                var t = 0;
                var reified = EmitReify(state, lay.FieldNested[fi]!, $"{so}.f{fi}", sym, body, ref t, out _);
                Line(body, $"    {Val(ins.Dst)} = {reified};");
                Line(body, "}");
            }
            return true;
        }
        var read = lay.FieldKinds[fi] switch
        {
            StackFieldKind.Double => $"new global::ChibiRuby.MRubyValue({so}.f{fi})",
            StackFieldKind.Long => $"new global::ChibiRuby.MRubyValue({so}.f{fi})",
            _ => $"{so}.f{fi}",
        };
        Line(body, $"{Val(ins.Dst)} = {read};");
        return true;
    }


    // Assemble the full C# method source for one analyzed Ruby method: init the emit-only caches,
    // walk the instructions (EmitInstruction) into the body, then wrap it in the entry/inline-form
    // signature with arg marshalling + local declarations. Returns null when an op bails (LastBail
    // set); isLeaf reports whether the body makes no outbound Ruby call. Reads the per-method
    // analysis facts off Mrb2CsCompiler's state (CurrentScalarArrays/Hashes/StackObjects).
    internal static string? EmitMethod(
        MRubyState state, RubyIRMethod ir, Irep irep, string methodName,
        IReadOnlyDictionary<ulong, int>? inlineRegistry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry,
        SymbolCache sym, InlineContext ic, ScalarContext? sc,
        Dictionary<int, StackLayout>? structParams,
        bool[] isLong, bool[] floatTaint, bool[] isDouble, bool[] provesDouble,
        HashSet<int> targets, List<int>? loopArgGuards, bool looping, int argCount,
        out bool isLeaf)
    {
        var instructions = ir.Instructions;
        BlockEmit = new BlockEmitState { OwnerName = methodName, AccessorRegistry = accessorRegistry, ForceSend = looping };
        AsObjCache = new Dictionary<int, string>();
        AsObjCounter = 0;
        ivarGetCache = new Dictionary<(int, Symbol), string>();
        ivCounter = 0;
        reusedIvarReads = ComputeReusedIvarReads(ir, targets);
        var body = new StringBuilder();
        // Loop arg-type guards: assert each numerically-used arg is a Fixnum at method entry, before
        // any instruction runs (no side effect committed yet -> a miss deopts safely to the
        // interpreter). This lets the lattice type those args Fixnum, unboxing mixed int/float arith.
        if (loopArgGuards is { Count: > 0 })
        {
            foreach (var argId in loopArgGuards)
                body.Append("    if (!").Append(Val(argId)).Append(".IsFixnum) { result = default; return false; }\n");
        }
        isLeaf = true;
        for (var i = 0; i < instructions.Length; i++)
        {
            // Accessor sends on a scalar-replaced object lower to field reads, not real calls,
            // so they don't make the method non-leaf.
            if (IsSendOp(instructions[i].OpCode) &&
                !(sc?.IsAccessorSendOnScalar(instructions[i]) ?? false))
            {
                isLeaf = false;
            }
            if (targets.Contains(i))
            {
                body.Append("    L").Append(i).Append(": ;\n");
                ClearAsObjCache(); // control-flow join: a cached cast may not reach here
                ClearIvarGetCache(); // ...nor a cached ivar read
            }
            if (!EmitInstruction(state, ir, instructions[i], sym, body, ic, sc, isLong, floatTaint, isDouble, provesDouble))
            {
                LastBail ??= "op:" + instructions[i].OpCode;
                return null;
            }
            InvalidateAsObj(instructions[i].Dst); // the value-id was just reassigned
            InvalidateIvarGet(instructions[i]);   // a write/call stales cached ivar reads
        }

        var sb = new StringBuilder();
        EmitSymbolFields(sym, sb);
        EmitInlineFields(ic, sb);
        if (sc is not null) EmitScalarFields(sc, sb);

        // Inline form: self (v0) + mandatory args (v1..vN) are parameters, so an inlining
        // caller can call it frameless with values (no stack/CallInfo). Remaining value-ids
        // are locals (default-init for goto / SSA-merge). The arity check lives in the wrapper.
        // A stack-struct VARIANT (structParams set) is named directly (no __inline/wrapper) and
        // takes its struct parameters as `in/ref <struct>` — called C#->C# from the caller.
        var isVariant = structParams is not null;
        // The frameless `__inline` form (self+args as C# params, callable without a frame) is emitted
        // only when some self-send actually inlines this method — i.e. its fingerprint is in the inline
        // registry AND it isn't a trivial accessor (those devirtualize to direct field access at every
        // call site, self or cross-object, so they're never `__inline`-called). Otherwise the irep
        // wrapper IS the body (args marshalled into locals), with no redundant `__inline` + forward.
        // `__inline` is a misnomer for "frameless direct-call form": the C# JIT, not this codegen, is
        // what actually inlines it into a caller; emitting it for non-targets was pure dead weight.
        var selfFp = isVariant ? 0UL : state.ComputeIrepFingerprint(irep);
        var needsInlineForm = !isVariant && inlineRegistry is not null && inlineRegistry.ContainsKey(selfFp)
            && !(AccessorFingerprints?.Contains(selfFp) ?? false);
        var merged = !isVariant && !needsInlineForm;
        var entryName = needsInlineForm ? methodName + "__inline" : methodName;
        sb.Append("public static bool ").Append(entryName).Append("(global::ChibiRuby.MRubyState state");
        if (merged)
        {
            // Single irep-bound method: check arity, marshal self/args off the frame into locals, run body.
            sb.Append(", int sp, out global::ChibiRuby.MRubyValue result)\n{\n");
            sb.Append("    if (ArgumentCountUnsafe(state) != ").Append(argCount).Append(") { result = default; return false; }\n");
            for (var v = 0; v <= argCount; v++)
            {
                sb.Append("    global::ChibiRuby.MRubyValue ").Append(Val(v))
                  .Append(" = RegisterUnsafe(state, sp, ").Append(v).Append(");\n");
            }
        }
        else
        {
            for (var v = 0; v <= argCount; v++)
            {
                if (isVariant && structParams!.TryGetValue(v, out var play))
                {
                    sb.Append(play.Mutated ? ", ref " : ", in ")
                      .Append(play.StructType).Append(' ').Append(Val(v));
                }
                else
                {
                    sb.Append(", global::ChibiRuby.MRubyValue ").Append(Val(v));
                }
            }
            sb.Append(", out global::ChibiRuby.MRubyValue result)\n{\n");
        }

        // Non-arg value-ids: boxed ones as MRubyValue locals, unboxed ones as raw long.
        // Scalar-replaced object value-ids are never materialized (their fields are locals).
        // Stack objects are struct locals (declared separately below).
        var boxedLocals = new List<int>();
        var longLocals = new List<int>();
        var doubleLocals = new List<int>();
        var stackLocals = new List<int>();
        for (var v = argCount + 1; v < ir.ValueCount; v++)
        {
            if (sc is not null && sc.IsScalar(v)) continue;
            if (CurrentScalarArrays is { } sa && sa.ContainsKey(v)) continue; // replaced by element locals
            if (CurrentScalarHashes is { } sh && sh.ContainsKey(v)) continue; // replaced by key locals
            if (CurrentStackObjects is { } cso && cso.ContainsKey(v)) { stackLocals.Add(v); continue; }
            if (Slot(v) != v) continue; // coalesced into another local's storage
            (isDouble[v] ? doubleLocals : isLong[v] ? longLocals : boxedLocals).Add(v);
        }
        // Per-element locals for scalar-replaced array literals (boxed; array elements are dynamic).
        // One declaration per CANONICAL array (aliases share its element locals).
        if (CurrentScalarArrays is { Count: > 0 } scalarArrays)
        {
            var declaredArrays = new HashSet<int>();
            foreach (var (_, info) in scalarArrays)
            {
                if (!declaredArrays.Add(info.Canon)) continue;
                sb.Append("    global::ChibiRuby.MRubyValue ");
                for (var k = 0; k < info.Size; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append(ArrElem(info.Canon, k)).Append(" = default");
                }
                sb.Append(";\n");
            }
        }
        // Per-key locals for scalar-replaced hashes (one per distinct key, init nil = Hash default).
        if (CurrentScalarHashes is { Count: > 0 } scalarHashes)
        {
            var declaredHashes = new HashSet<int>();
            foreach (var (_, info) in scalarHashes)
            {
                if (!declaredHashes.Add(info.Canon) || info.Keys.Count == 0) continue;
                sb.Append("    global::ChibiRuby.MRubyValue ");
                var first = true;
                foreach (var tag in info.Keys)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(HashElem(info.Canon, tag)).Append(" = global::ChibiRuby.MRubyValue.Nil");
                }
                sb.Append(";\n");
            }
        }
        if (boxedLocals.Count > 0)
        {
            sb.Append("    global::ChibiRuby.MRubyValue ");
            for (var i = 0; i < boxedLocals.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Val(boxedLocals[i])).Append(" = default");
            }
            sb.Append(";\n");
        }
        if (longLocals.Count > 0)
        {
            sb.Append("    long ");
            for (var i = 0; i < longLocals.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("l").Append(longLocals[i]).Append(" = 0");
            }
            sb.Append(";\n");
        }
        if (doubleLocals.Count > 0)
        {
            sb.Append("    double ");
            for (var i = 0; i < doubleLocals.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("d").Append(doubleLocals[i]).Append(" = 0");
            }
            sb.Append(";\n");
        }

        if (sc is not null) EmitScalarFieldLocals(sc, sb);

        var declaredStructs = new HashSet<string>(); // canonical mutated aliases share one local
        // A struct param is declared in the signature; a local that canonicalizes to it (a mutated
        // param's alias) must NOT be redeclared.
        if (structParams is not null)
            foreach (var pidx in structParams.Keys) declaredStructs.Add(Val(pidx));
        foreach (var v in stackLocals)
        {
            var name = Val(v);
            if (!declaredStructs.Add(name)) continue;
            sb.Append("    ").Append(CurrentStackObjects![v].StructType).Append(' ').Append(name).Append(" = default;\n");
        }

        EmitSymbolInit(sym, sb);
        sb.Append(body);

        if (targets.Contains(instructions.Length))
        {
            sb.Append("    L").Append(instructions.Length).Append(": ;\n");
        }
        sb.Append("    result = default; return false;\n");
        sb.Append("}\n");

        // Only the split `__inline` form needs a separate wrapper. Variants are called directly
        // C#->C# (never irep-keyed); merged methods already are the irep-bound entry.
        if (needsInlineForm)
        {
            // Stack wrapper: bound to the irep by fingerprint (Irep.CompiledBody). Checks arity,
            // marshals self/args off the frame, and calls the inline form.
            sb.Append("public static bool ").Append(methodName)
              .Append("(global::ChibiRuby.MRubyState state, int sp, out global::ChibiRuby.MRubyValue result)\n{\n");
            sb.Append("    if (ArgumentCountUnsafe(state) != ").Append(argCount)
              .Append(") { result = default; return false; }\n");
            sb.Append("    return ").Append(methodName).Append("__inline(state");
            for (var v = 0; v <= argCount; v++)
            {
                sb.Append(", RegisterUnsafe(state, sp, ").Append(v).Append(")");
            }
            sb.Append(", out result);\n}\n");
        }

        return sb.ToString();
    }

    // Assemble the generated source file: the auto-generated header, an optional block-scoped
    // namespace, the AotGeneratedMethods-derived class, the stack struct declarations the methods
    // use, then the method sources. `structs` is the transitive set the driver gathered from
    // NeededStructs (nested field types included); `sources` are the per-method bodies + aux methods.
    internal static string EmitProgram(string className, string? namespaceName,
        IEnumerable<StackLayout> structs, IReadOnlyList<string> sources)
    {
        var sb = new StringBuilder();
        // Roslyn/csc honor `// <auto-generated/>` to suppress analyzers & StyleCop on the file.
        sb.Append("// <auto-generated/>\n");
        sb.Append("// Generated by ChibiRuby mrb2cs (ChibiRuby.JetPack.Mrb2Cs.Mrb2CsCompiler.Compile). Do not edit.\n");
        // Block-scoped namespace (not file-scoped `namespace X;`, which is C# 10) so the generated
        // source compiles under C# 9 — required for Unity / IL2CPP.
        var hasNamespace = namespaceName is { Length: > 0 };
        if (hasNamespace) sb.Append("namespace ").Append(namespaceName).Append("\n{\n");
        sb.Append("public sealed class ").Append(className).Append(" : global::ChibiRuby.AotGeneratedMethods\n{\n");
        // Stack struct types declared before the methods that use them.
        foreach (var lay in structs)
        {
            sb.Append("public struct ").Append(lay.StructType).Append(" { ");
            for (var f = 0; f < lay.Fields.Count; f++) sb.Append("public ").Append(lay.CsFieldType(f)).Append(" f").Append(f).Append("; ");
            sb.Append("}\n");
        }
        foreach (var s in sources) sb.Append(s).Append('\n');
        sb.Append("}\n");
        if (hasNamespace) sb.Append("}\n");
        return sb.ToString();
    }

    // Emit a Symbol's name as a C# string literal. Covers ivar names
    // (@x) and method names incl. operators ([], +, !=, %). Bails on empty or any
    // non-printable / non-ASCII byte.
    internal static bool TrySymbolStringLiteral(MRubyState state, Symbol sym, out string stringLiteral)
    {
        var name = state.NameOf(sym).AsSpan();
        if (name.Length == 0)
        {
            stringLiteral = "";
            return false;
        }
        var sb = new StringBuilder("\"");
        foreach (var b in name)
        {
            if (b < 0x20 || b > 0x7E)
            {
                stringLiteral = "";
                return false;
            }
            if (b == (byte)'"' || b == (byte)'\\')
            {
                sb.Append('\\');
            }
            sb.Append((char)b);
        }
        sb.Append('"');
        stringLiteral = sb.ToString();
        return true;
    }

    // C# string literal for a class's registered name (used to re-resolve the class at
    // runtime via a top-level constant in ResolveGuardClassUnsafe). Fails on non-printable bytes
    // or a `::`-qualified path (the runtime resolver only walks top-level constants — a
    // nested candidate simply isn't class-switched and falls through to reify+Send).
    internal static bool TryClassNameLiteral(MRubyState state, RClass c, out string stringLiteral)
    {
        var name = state.NameOf(c).AsSpan();
        if (name.Length == 0)
        {
            stringLiteral = "";
            return false;
        }
        var sb = new StringBuilder("\"");
        foreach (var b in name)
        {
            if (b < 0x20 || b > 0x7E || b == (byte)':')
            {
                stringLiteral = "";
                return false;
            }
            if (b == (byte)'"' || b == (byte)'\\')
            {
                sb.Append('\\');
            }
            sb.Append((char)b);
        }
        sb.Append('"');
        stringLiteral = sb.ToString();
        return true;
    }

    internal static void Line(StringBuilder body, string text) => body.Append("    ").Append(text).Append('\n');

    // ---- Name mangling: Ruby names/fingerprints -> C# identifiers (emitted into the source) ----
    // Maps an arbitrary Ruby name (a symbol, method, or class name) to a snake_case C# identifier
    // fragment: alphanumeric runs are kept as-is, and each character that isn't legal in a C#
    // identifier is spelled out as a lowercase word, with segments joined by `_`
    // (e.g. `@x` -> `at_x`, `empty?` -> `empty_q`, `<=>` -> `lt_eq_gt`). Returns the body only — no
    // leading separator — so the caller attaches it (symbol slots / method / class names prefix `_`).
    internal static string SanitizeToIdentifier(string raw)
    {
        var segments = new List<string>();
        var run = new StringBuilder();
        foreach (var c in raw)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                run.Append(c);
                continue;
            }
            FlushRun();
            var word = c switch
            {
                '@' => "at",
                '?' => "q",
                '!' => "bang",
                '=' => "eq",
                '<' => "lt",
                '>' => "gt",
                '+' => "plus",
                '-' => "minus",
                '*' => "star",
                '/' => "slash",
                '%' => "percent",
                '[' => "lbracket",
                ']' => "rbracket",
                '&' => "amp",
                '|' => "pipe",
                '^' => "caret",
                '~' => "tilde",
                ' ' => "",
                _ => "u" + ((int)c).ToString("x2"),
            };
            if (word.Length > 0) segments.Add(word);
        }
        FlushRun();
        return string.Join("_", segments);

        void FlushRun()
        {
            if (run.Length > 0)
            {
                segments.Add(run.ToString()); run.Clear();

            }
        }
    }

    // A `_name` identifier suffix for a Ruby name, or "" when the name sanitizes to nothing
    // (so an unnamed/anonymous entity just keeps its fingerprint-only base name).
    internal static string NameSuffixFor(string raw)
    {
        var body = SanitizeToIdentifier(raw);
        return body.Length == 0 ? "" : "_" + body;
    }

    // The C# name for a compiled method body, derived from its irep fingerprint plus (when known)
    // the original Ruby method name. Used both where the body is DEFINED and at every inline/variant
    // call site, so they always agree.
    internal static string MethodCsName(ulong fp)
    {
        var name = "M_" + fp.ToString("x16");
        if (MethodNameSuffixes is { } m && m.TryGetValue(fp, out var suffix)) name += suffix;
        return name;
    }

    // Variant name suffix for a set of struct params: sorted `__s<idx>[r]_<classfp>` per param.
    internal static string StructParamSuffix(Dictionary<int, StackLayout> structParams)
    {
        var sb = new StringBuilder();
        foreach (var idx in new List<int>(structParams.Keys).OrderBy(x => x))
            sb.Append("__s").Append(idx).Append(structParams[idx].Mutated ? "r" : "")
              .Append('_').Append(structParams[idx].ClassFp.ToString("x16"));
        return sb.ToString();
    }


    // ---- SymbolCache emission: per-method interned-symbol static fields + the intern prologue.
    // SymbolCache owns the data (slot -> field name / literal); these turn it into C# source.

    // Class-scope: per-symbol UTF-8 name bytes + state cache field + one Symbol field per slot.
    internal static void EmitSymbolFields(SymbolCache sym, StringBuilder sb)
    {
        if (sym.Count == 0) return;
        // UTF-8 name bytes, statically initialized. We avoid both a UTF-16 string literal in
        // the generated assembly and the per-state UTF-16->UTF-8 reencode `Intern(string)` does.
        // (`"name"u8` would be ideal but doesn't work under Unity/IL2CPP, so emit an explicit
        // `new byte[] { ... }`.)
        for (var i = 0; i < sym.Count; i++)
        {
            sb.Append("static readonly byte[] ").Append(Utf8Field(sym.FieldNames[i])).Append(" = ")
              .Append(Utf8ArrayLiteral(sym.Literals[i])).Append("; // ").Append(CommentText(RawName(sym.Literals[i]))).Append('\n');
        }
        sb.Append("static global::ChibiRuby.MRubyState ").Append(sym.MethodName).Append("__symState;\n");
        sb.Append("static global::ChibiRuby.Symbol ");
        for (var i = 0; i < sym.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(sym.FieldNames[i]);
        }
        sb.Append(";\n");
    }

    // Method-prologue: (re)intern all symbols when the state changed (once per state). Interns
    // from the static UTF-8 bytes (ReadOnlySpan<byte> overload), not a UTF-16 string.
    internal static void EmitSymbolInit(SymbolCache sym, StringBuilder sb)
    {
        if (sym.Count == 0) return;
        sb.Append("    if (!object.ReferenceEquals(").Append(sym.MethodName).Append("__symState, state)) { ");
        for (var i = 0; i < sym.Count; i++)
        {
            sb.Append(sym.FieldNames[i]).Append(" = state.Intern(").Append(Utf8Field(sym.FieldNames[i])).Append("); ");
        }
        sb.Append(sym.MethodName).Append("__symState = state; }\n");
    }

    static string Utf8Field(string fieldName) => fieldName + "_u8";

    // `new byte[] { .. }` of the symbol's UTF-8 bytes, for `state.Intern(ReadOnlySpan<byte>)`.
    static string Utf8ArrayLiteral(string stringLiteral)
    {
        var bytes = Encoding.UTF8.GetBytes(RawName(stringLiteral));
        var sb = new StringBuilder("new byte[] { ");
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(bytes[i]);
        }
        return sb.Append(" }").ToString();
    }

    // The raw symbol name from a C# string literal: strip the outer quotes and undo the
    // `\"`/`\\` escapes that TrySymbolStringLiteral introduced.
    internal static string RawName(string stringLiteral)
    {
        var inner = stringLiteral is ['"', _, ..] && stringLiteral[^1] == '"'
            ? stringLiteral.Substring(1, stringLiteral.Length - 2)
            : stringLiteral;
        var sb = new StringBuilder();
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '\\' && i + 1 < inner.Length) c = inner[++i]; // unescape \" and \\
            sb.Append(c);
        }
        return sb.ToString();
    }

    // The original symbol text, made safe for a `//` line comment (no embedded newlines).
    static string CommentText(string raw) => raw.Replace("\r", " ").Replace("\n", " ");

    // ==== InlineContext emission (moved out of InlineContext; bodies unchanged, ic.* aliased) ====

    // A constant read (`OP_GetConst`) cached per call site: resolve once, then re-resolve only
    // when ConstCacheVersion changes. dst <- the constant's value.
    internal static void EmitGuardedConstantRead(InlineContext ic, in RubyIRInstruction ins, string cname, StringBuilder body)
    {
        var state = ic.state;
        var methodName = ic.methodName;
        var sym = ic.sym;
        ref var constReadCount = ref ic.constReadCount;
        var n = constReadCount++;
        var valField = methodName + "__gc" + n + "val";
        var verField = methodName + "__gc" + n + "ver";
        var stateField = methodName + "__gc" + n + "state";
        Line(body, $"{Val(ins.Dst)} = GetConstantCachedUnsafe(state, {sym.Reference(cname)}, ref {valField}, ref {verField}, ref {stateField});");
    }

    // Caller side: a Send whose argument is a stack-allocated object. Dispatch to the callee's
    // struct variant (guarded per receiver class) and reify on miss/deopt. Stage 1 handles a
    // single struct arg (ao's ray into intersect); other shapes return false (normal path).
    internal static bool TryEmitStackArgSend(InlineContext ic, in RubyIRInstruction ins, StringBuilder body)
    {
        var state = ic.state;
        var methodName = ic.methodName;
        var sym = ic.sym;
        var exe = ic.exe;
        ref var candGroupCount = ref ic.candGroupCount;
        var candGroupSizes = ic.candGroupSizes;
        if (CurrentStackObjects is not { } cso || CurrentEscapeSummary is not { } summary) return false;
        if (ins.OpCode is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) return false;
        // The receiver must not itself be a stack object (that is the struct-receiver path).
        if (cso.ContainsKey(ins.Src0)) return false;
        var argc = exe.GetCallSiteArgumentCount(ins.Aux);
        // Gather ALL struct args (each lowers to a struct param of the variant; in/ref by Mutated).
        var sargs = new List<(int Pos, int ObjId, StackLayout Lay)>();
        for (var a = 0; a < argc; a++)
        {
            var aid = exe.GetCallSiteArgumentValueId(ins.Aux, a);
            if (cso.TryGetValue(aid, out var alay)) sargs.Add((a, aid, alay));
        }
        if (sargs.Count == 0) return false;
        var sel = exe.GetCallSiteSymbol(ins.Aux);
        if (!TrySymbolStringLiteral(state, sel, out var selName)) return false;
        var defs = summary.DefiningClasses(sel);
        if (defs.Count == 0) return false;

        // The variant's struct params: paramIndex (pos+1, v0 is self) -> layout.
        var structParamsMap = new Dictionary<int, StackLayout>();
        foreach (var sa in sargs) structParamsMap[sa.Pos + 1] = sa.Lay;
        var suffix = StructParamSuffix(structParamsMap);

        var cands = new List<(RClass Cls, string NameLit, ulong Fp, string VariantName)>();
        foreach (var c in defs)
        {
            if (!state.TryFindMethod(c, sel, out var m, out _) || m.Proc is not { } proc) continue;
            if (!TryClassNameLiteral(state, c, out var nameLit)) continue;
            var fp = state.ComputeIrepFingerprint(proc.Irep);
            cands.Add((c, nameLit, fp, MethodCsName(fp) + suffix));
        }
        if (cands.Count == 0) return false;

        NeededStructs ??= new Dictionary<ulong, StackLayout>(); NeededVariants ??= new();
        foreach (var sa in sargs) NeededStructs[sa.Lay.ClassFp] = sa.Lay;
        foreach (var cand in cands)
            NeededVariants[cand.VariantName] = (cand.Cls, sel, structParamsMap);

        var recv = Val(ins.Src0);
        var dst = Val(ins.Dst);
        var symRef = sym.Reference(selName);
        var pos2sa = new Dictionary<int, (int Pos, int ObjId, StackLayout Lay)>();
        foreach (var sa in sargs) pos2sa[sa.Pos] = sa;
        var aux = ins.Aux; // can't capture the `in` param in the local function below
        // Build the arg list: struct args use `structAt(sa)`, the rest are plain value reads.
        string Args(System.Func<(int Pos, int ObjId, StackLayout Lay), string> structAt)
        {
            var b = new StringBuilder();
            for (var a = 0; a < argc; a++)
                b.Append(", ").Append(pos2sa.TryGetValue(a, out var sa) ? structAt(sa) : Val(exe.GetCallSiteArgumentValueId(aux, a)));
            return b.ToString();
        }

        // Class-switch dispatch: resolve candidate classes once per method-cache version into
        // per-site static fields, then pointer-compare the receiver's class.
        var grp = candGroupCount++;
        candGroupSizes.Add(cands.Count);
        var verField = methodName + "__cand" + grp + "ver";
        string ClsField(int i) => methodName + "__cand" + grp + "_" + i + "cls";

        Line(body, $"if ({verField} != state.MethodCacheVersion) {{");
        for (var i = 0; i < cands.Count; i++)
            Line(body, $"    {ClsField(i)} = ResolveGuardClassUnsafe(state, {sym.Reference(cands[i].NameLit)}, {symRef}, 0x{cands[i].Fp:x16}UL);");
        Line(body, $"    {verField} = state.MethodCacheVersion;");
        Line(body, "}");

        var done = "__sdone" + grp;
        var rc = "__src" + grp;
        // Snapshot each mutated (ref) struct arg, so the reify fallback can restore the pre-call
        // state (a variant may partially mutate it, then deopt). (A)
        foreach (var sa in sargs) if (sa.Lay.Mutated) Line(body, $"var __snap{grp}_{sa.ObjId} = {Val(sa.ObjId)};");
        Line(body, $"bool {done} = false;");
        Line(body, $"var {rc} = {recv}.Object?.Class;");
        for (var i = 0; i < cands.Count; i++)
        {
            var kw = i == 0 ? "if" : "else if";
            var callArgs = Args(sa => (sa.Lay.Mutated ? "ref " : "in ") + Val(sa.ObjId));
            Line(body, $"{kw} ({rc} != null && {rc} == {ClsField(i)}) {{ if ({cands[i].VariantName}(state, {recv}{callArgs}, out {dst})) {done} = true; }}");
        }
        // Reify + normal Send on unknown class / variant deopt: restore ref args, reify each
        // struct arg, Send, then copy back the mutated heap objects into their structs.
        Line(body, $"if (!{done}) {{");
        foreach (var sa in sargs) if (sa.Lay.Mutated) Line(body, $"{Val(sa.ObjId)} = __snap{grp}_{sa.ObjId};");
        var tmp = 0;
        var reifyAt = new Dictionary<int, string>();
        var refRos = new List<(StackLayout Lay, int ObjId, string Ro)>();
        foreach (var sa in sargs)
        {
            reifyAt[sa.Pos] = EmitReify(state, sa.Lay, Val(sa.ObjId), sym, body, ref tmp, out var ro);
            if (sa.Lay.Mutated) refRos.Add((sa.Lay, sa.ObjId, ro));
        }
        Line(body, $"{dst} = state.Send({recv}, {symRef}{Args(sa => reifyAt[sa.Pos])});");
        foreach (var (rlay, roid, ro) in refRos) EmitCopyBack(state, rlay, Val(roid), ro, sym, body, ref tmp);
        Line(body, "}");
        return true;
    }

    // Caller side: a non-accessor Send whose RECEIVER is a stack object (`ray.dir.vdot(@n)`).
    // The receiver class is statically the object's layout class, so there is no class-switch —
    // guard that the class/method are unchanged, then call the callee's struct-`self` variant
    // (structParamIndex 0); reify + normal Send on a guard/variant miss. Stage C-step1 keeps it
    // read-only `in` and requires plain (non-struct) args; nested/ref args come later.
    internal static bool TryEmitStackReceiverSend(InlineContext ic, in RubyIRInstruction ins, StringBuilder body)
    {
        var state = ic.state;
        var methodName = ic.methodName;
        var sym = ic.sym;
        var exe = ic.exe;
        ref var icCount = ref ic.icCount;
        if (CurrentStackObjects is not { } cso) return false;
        if (ins.OpCode is not RubyIROpCode.Send) return false;
        if (!cso.TryGetValue(ins.Src0, out var lay)) return false;
        var sel = exe.GetCallSiteSymbol(ins.Aux);
        if (!state.TryFindMethod(lay.Cls, sel, out var m, out _) || m.Proc is not { } proc) return false;
        if (TryRecognizeTrivialAccessor(state, proc.Irep) is not null) return false; // accessor -> field path
        var argc = exe.GetCallSiteArgumentCount(ins.Aux);
        for (var a = 0; a < argc; a++)
            if (cso.ContainsKey(exe.GetCallSiteArgumentValueId(ins.Aux, a))) return false; // struct arg: beyond step1
        if (!TrySymbolStringLiteral(state, sel, out var selName)) return false;
        if (!TrySymbolStringLiteral(state, lay.ConstName, out var constLit)) return false;

        var fp = state.ComputeIrepFingerprint(proc.Irep);
        var structParamsMap = new Dictionary<int, StackLayout> { [0] = lay }; // self is the struct (paramIndex 0)
        var variantName = MethodCsName(fp) + StructParamSuffix(structParamsMap);
        NeededStructs ??= new(); NeededVariants ??= new();
        NeededStructs[lay.ClassFp] = lay;
        NeededVariants[variantName] = (lay.Cls, sel, structParamsMap);

        var recv = Val(ins.Src0);
        var dst = Val(ins.Dst);
        var symRef = sym.Reference(selName);
        var selfKw = lay.Mutated ? "ref " : "in "; // method is read-only (CalleeSelfReadOnly), but the
        var args = new StringBuilder();             // param decl is keyed on Mutated, so match it; no copy-back needed.
        for (var a = 0; a < argc; a++) args.Append(", ").Append(Val(exe.GetCallSiteArgumentValueId(ins.Aux, a)));
        var n = icCount++;
        var icCls = methodName + "__ic" + n + "cls";
        var icVer = methodName + "__ic" + n + "ver";
        var done = "__rdone" + n;
        Line(body, $"bool {done} = false;");
        Line(body,
            $"if (ClassMethodGuardUnsafe(state, {sym.Reference(constLit)}, {symRef}, 0x{fp:x16}UL, ref {icCls}, ref {icVer})) {{ if ({variantName}(state, {selfKw}{recv}{args}, out {dst})) {done} = true; }}");
        Line(body, $"if (!{done}) {{");
        var tmp = 0;
        var reified = EmitReify(state, lay, recv, sym, body, ref tmp, out _);
        Line(body, $"{dst} = state.Send({reified}, {symRef}{args});");
        Line(body, "}");
        return true;
    }

    internal static bool TryInlineSelfSend(InlineContext ic, in RubyIRInstruction ins, Symbol methodSym, string mname, int argc, StringBuilder body)
    {
        var state = ic.state;
        var definingClass = ic.definingClass;
        var registry = ic.registry;
        var accessors = ic.accessors;
        if (definingClass is null || registry is null) return false;
        // A self-send to a trivial accessor devirtualizes to direct field access (handled by
        // TryEmitAccessorDevirt, which now also accepts SendSelf) — never an `__inline` call.
        if (accessors is not null && accessors.ContainsKey(methodSym)) return false;
        // Resolve the target against the defining class (self's class). Only inline if
        // it lands on an RProc method that the first pass marked safe (compiled + leaf +
        // small) and whose arity matches the call site.
        if (!state.TryFindMethod(definingClass, methodSym, out var method, out _) ||
            method.Proc is not { } proc)
        {
            return false;
        }
        var fp = state.ComputeIrepFingerprint(proc.Irep);
        if (!registry.TryGetValue(fp, out var calleeArgc) || calleeArgc != argc)
        {
            return false;
        }

        EmitGuardedInlineCall(ic, ins, mname, fp, argc, body);
        return true;
    }

    // if (guard(recv class) && callee__inline(state, recv, args, out dst)) {} else dst = Send(...)
    internal static void EmitGuardedInlineCall(InlineContext ic, in RubyIRInstruction ins, string mname, ulong fp, int argc, StringBuilder body)
    {
        var methodName = ic.methodName;
        var sym = ic.sym;
        var exe = ic.exe;
        ref var icCount = ref ic.icCount;
        var n = icCount++;
        var icCls = methodName + "__ic" + n + "cls";
        var icVer = methodName + "__ic" + n + "ver";
        var calleeInline = MethodCsName(fp) + "__inline";
        var recv = Val(ins.Src0);
        var dst = Val(ins.Dst);
        var symRef = sym.Reference(mname);
        var args = new StringBuilder();
        for (var i = 0; i < argc; i++)
        {
            args.Append(", ").Append(Val(exe.GetCallSiteArgumentValueId(ins.Aux, i)));
        }
        // Capture the receiver in a temp: register reuse can make dst == recv (e.g.
        // `v = v.org; v = v.vsub(..)`). The callee's __inline writes `out dst` = nil before
        // returning false on a guard miss, which would clobber the receiver the Send
        // fallback then reads. The temp holds the original receiver across both paths.
        var tmp = "_r" + n;
        // Guard hit + inline body succeeds -> dst holds the inline result. Guard miss
        // (polymorphic / overriding subclass / redefinition) or inline deopt -> Send.
        Line(body,
            $"{{ var {tmp} = {recv}; if (InlineGuardUnsafe(state, {tmp}, {symRef}, 0x{fp:x16}UL, ref {icCls}, ref {icVer}) && {calleeInline}(state, {tmp}{args}, out {dst})) {{ }} else {{ {dst} = state.Send({tmp}, {symRef}{args}); }} }}");
    }

    // `recv.getter` / `recv.setter=` -> guarded direct field access, for both cross-object (Send)
    // and self (SendSelf) receivers. The guard (InlineGuardUnsafe) confirms recv's class still
    // resolves the selector to the exact trivial-accessor body we devirtualized against; a miss
    // falls back to a real Send (so a subclass override of the accessor still runs). We speculate
    // the receiver is that class — a wrong receiver simply takes the Send path.
    internal static bool TryEmitAccessorDevirt(InlineContext ic, in RubyIRInstruction ins, Symbol methodSym, string mname, int argc, StringBuilder body)
    {
        var state = ic.state;
        var methodName = ic.methodName;
        var sym = ic.sym;
        var exe = ic.exe;
        var accessors = ic.accessors;
        ref var icCount = ref ic.icCount;
        if (accessors is null || ins.OpCode is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) return false;
        if (Environment.GetEnvironmentVariable("AOT_NODEVIRT") == "1") return false;
        if (!accessors.TryGetValue(methodSym, out var target)) return false;
        if (target.IsSetter ? argc != 1 : argc != 0) return false;
        if (!TrySymbolStringLiteral(state, target.Field, out var fieldLit)) return false;

        var n = icCount++;
        var icCls = methodName + "__ic" + n + "cls";
        var icVer = methodName + "__ic" + n + "ver";
        var recv = Val(ins.Src0);
        // The guard confirms recv's class, so inside the hit branch recv is an RObject.
        var recvObj = $"{recv}.As<global::ChibiRuby.RObject>()";
        var dst = Val(ins.Dst);
        var symM = sym.Reference(mname);
        var symF = sym.Reference(fieldLit);
        var guard = $"InlineGuardUnsafe(state, {recv}, {symM}, 0x{target.Fingerprint:x16}UL, ref {icCls}, ref {icVer})";
        if (target.IsSetter)
        {
            var arg = Val(exe.GetCallSiteArgumentValueId(ins.Aux, 0));
            Line(body,
                $"if ({guard}) {{ {IvarSet(recvObj, symF, arg)}; {dst} = {arg}; }} else {{ {dst} = state.Send({recv}, {symM}, {arg}); }}");
        }
        else
        {
            Line(body,
                $"if ({guard}) {{ {dst} = {IvarGet(recvObj, symF)}; }} else {{ {dst} = state.Send({recv}, {symM}); }}");
        }
        return true;
    }

    // A cross-object 0-arg send to a method that returns an immediate constant (multi-level via
    // delegation) -> emit the constant directly, guarded by the callee's fingerprint; a guard
    // miss (different class / redefinition) falls back to a normal Send.
    internal static bool TryEmitConstantDevirt(InlineContext ic, in RubyIRInstruction ins, Symbol methodSym, string mname, int argc, StringBuilder body)
    {
        var state = ic.state;
        var methodName = ic.methodName;
        var sym = ic.sym;
        var constReturns = ic.constReturns;
        ref var icCount = ref ic.icCount;
        if (constReturns is null || ins.OpCode is not RubyIROpCode.Send || argc != 0) return false;
        if (Environment.GetEnvironmentVariable("AOT_NODEVIRT") == "1") return false;
        if (!constReturns.TryGetValue(methodSym, out var target)) return false;
        if (!TryEmitLiteral(target.Value, out var constExpr)) return false;
        var n = icCount++;
        var icCls = methodName + "__ic" + n + "cls";
        var icVer = methodName + "__ic" + n + "ver";
        var recv = Val(ins.Src0);
        var dst = Val(ins.Dst);
        var symM = sym.Reference(mname);
        Line(body,
            $"if (InlineGuardUnsafe(state, {recv}, {symM}, 0x{target.Fingerprint:x16}UL, ref {icCls}, ref {icVer})) {{ {dst} = {constExpr}; }} else {{ {dst} = state.Send({recv}, {symM}); }}");
        return true;
    }

    internal static bool TryEmitPureUnarySend(InlineContext ic, in RubyIRInstruction ins, string mname, StringBuilder body)
    {
        var methodName = ic.methodName;
        var sym = ic.sym;
        var exe = ic.exe;
        ref var pureUnaryCount = ref ic.pureUnaryCount;
        var n = pureUnaryCount++;
        var icCls = methodName + "__pu" + n + "cls";
        var icVer = methodName + "__pu" + n + "ver";
        var icMethod = methodName + "__pu" + n + "method";
        var recv = Val(ins.Src0);
        var arg = Val(exe.GetCallSiteArgumentValueId(ins.Aux, 0));
        var dst = Val(ins.Dst);
        var symRef = sym.Reference(mname);
        Line(body, $"if ({arg}.IsFixnum || {arg}.IsFloat) {{ {dst} = PureUnarySendUnsafe(state, {recv}, {symRef}, {arg}, ref {icCls}, ref {icVer}, ref {icMethod}); }} else {{ {dst} = state.Send({recv}, {symRef}, {arg}); }}");
        return true;
    }

    internal static void EmitGuardInlineClass(InlineContext ic, in RubyIRInstruction ins, string mname, ulong fp, StringBuilder body)
    {
        var methodName = ic.methodName;
        var sym = ic.sym;
        ref var icCount = ref ic.icCount;
        var n = icCount++;
        var icCls = methodName + "__ic" + n + "cls";
        var icVer = methodName + "__ic" + n + "ver";
        var symRef = sym.Reference(mname);
        Line(body,
            $"if (InlineGuardUnsafe(state, {Val(ins.Src0)}, {symRef}, 0x{fp:x16}UL, ref {icCls}, ref {icVer})) goto L{ins.Aux}; else {{ result = default; return false; }}");
    }

    internal static void EmitInlineFields(InlineContext ic, StringBuilder sb)
    {
        var methodName = ic.methodName;
        var icCount = ic.icCount;
        var pureUnaryCount = ic.pureUnaryCount;
        var constReadCount = ic.constReadCount;
        var candGroupSizes = ic.candGroupSizes;
        for (var i = 0; i < icCount; i++)
        {
            sb.Append("static global::ChibiRuby.RClass ").Append(methodName).Append("__ic").Append(i).Append("cls;\n");
            sb.Append("static int ").Append(methodName).Append("__ic").Append(i).Append("ver;\n");
        }
        for (var i = 0; i < pureUnaryCount; i++)
        {
            sb.Append("static global::ChibiRuby.RClass ").Append(methodName).Append("__pu").Append(i).Append("cls;\n");
            sb.Append("static int ").Append(methodName).Append("__pu").Append(i).Append("ver;\n");
            sb.Append("static global::ChibiRuby.MRubyMethod ").Append(methodName).Append("__pu").Append(i).Append("method;\n");
        }
        for (var g = 0; g < candGroupSizes.Count; g++)
        {
            // -1 sentinel: MethodCacheVersion is >= 0, so the first call always resolves
            // (a 0 default would collide with the initial version and never resolve).
            sb.Append("static int ").Append(methodName).Append("__cand").Append(g).Append("ver = -1;\n");
            for (var i = 0; i < candGroupSizes[g]; i++)
            {
                sb.Append("static global::ChibiRuby.RClass ").Append(methodName).Append("__cand").Append(g).Append('_').Append(i).Append("cls;\n");
            }
        }
        for (var i = 0; i < constReadCount; i++)
        {
            sb.Append("static global::ChibiRuby.MRubyValue ").Append(methodName).Append("__gc").Append(i).Append("val;\n");
            // -1 sentinel as above: never equals a real ConstCacheVersion (>= 0).
            sb.Append("static int ").Append(methodName).Append("__gc").Append(i).Append("ver = -1;\n");
            sb.Append("static global::ChibiRuby.MRubyState ").Append(methodName).Append("__gc").Append(i).Append("state;\n");
        }
    }

    // ==== ScalarContext emission (moved out of ScalarContext; bodies unchanged, sc.* aliased) ====

    internal static void EmitScalarFieldLocals(ScalarContext sc, StringBuilder sb)
    {
        var objects = sc.objects;
        var seen = new HashSet<int>();
        foreach (var o in objects.Values)
        {
            if (!seen.Add(o.ValueId)) continue;
            for (var f = 0; f < o.Fields.Count; f++)
            {
                sb.Append("    global::ChibiRuby.MRubyValue ").Append(ScalarContext.FieldLocal(o.ValueId, f)).Append(" = default;\n");
            }
        }
    }

    // VirtualNew of a scalar object: guard validity, then set each field local from the
    // matching ctor arg (initialize inlined). No allocation.
    internal static void EmitScalarNew(ScalarContext sc, RubyIRMethod exe, in RubyIRInstruction ins, StringBuilder body)
    {
        var state = sc.state;
        var objects = sc.objects;
        var o = objects[ins.Dst];
        // Guard: constant still resolves to this class and initialize + every accessor we
        // inline still have the fingerprints we compiled against. Miss -> deopt.
        EmitScalarGuardCall(sc, body, o, state.Intern("initialize"u8), o.InitFingerprint);
        foreach (var acc in o.Accessors)
        {
            EmitScalarGuardCall(sc, body, o, acc.Key, acc.Value.Fingerprint);
        }
        for (var f = 0; f < o.Fields.Count; f++)
        {
            var argVid = exe.GetCallSiteArgumentValueId(ins.Aux, o.FieldArg[f]);
            Line(body, $"{ScalarContext.FieldLocal(o.ValueId, f)} = {Val(argVid)};");
        }
    }

    // Escaping `Const.new(args)` -> inline construction: allocate the object and store its
    // fields directly, skipping the `:new` + `:initialize` double dispatch. Guarded so a
    // redefined `:new`/`:initialize` deopts to the real dispatch (InlineNewGuardUnsafe).
    internal static void EmitFastNew(ScalarContext sc, RubyIRMethod exe, in RubyIRInstruction ins, StringBuilder body)
    {
        var state = sc.state;
        var methodName = sc.methodName;
        var sym = sc.sym;
        var fastNew = sc.fastNew;
        ref var guardCount = ref sc.guardCount;
        var o = fastNew[ins.Dst];
        if (!TrySymbolStringLiteral(state, state.Intern("new"u8), out var newLit) ||
            !TrySymbolStringLiteral(state, state.Intern("initialize"u8), out var initLit))
        {
            Line(body, $"{Val(ins.Dst)} = state.Send({Val(ins.Src0)}, {sym.Reference("\"new\"")});");
            return;
        }
        var slot = guardCount++;
        var cls = methodName + "__sc" + slot + "cls";
        var ver = methodName + "__sc" + slot + "ver";
        var classVal = Val(ins.Src0);
        Line(body,
            $"if (!InlineNewGuardUnsafe(state, {classVal}, {sym.Reference(newLit)}, {sym.Reference(initLit)}, 0x{o.InitFingerprint:x16}UL, ref {cls}, ref {ver})) {{ result = default; return false; }}");
        Line(body, $"{Val(ins.Dst)} = new global::ChibiRuby.MRubyValue(new global::ChibiRuby.RObject({classVal}.As<global::ChibiRuby.RClass>()));");
        // The dst is the freshly-allocated RObject; cast it once for all field stores.
        var newObj = o.Fields.Count > 0 ? AsRObject(ins.Dst, Val(ins.Dst), body) : Val(ins.Dst);
        for (var f = 0; f < o.Fields.Count; f++)
        {
            if (!TrySymbolStringLiteral(state, o.Fields[f], out var fieldLit)) continue;
            var argVid = exe.GetCallSiteArgumentValueId(ins.Aux, o.FieldArg[f]);
            Line(body, $"{IvarSet(newObj, sym.Reference(fieldLit), Val(argVid))};");
        }
    }

    internal static void EmitScalarGuardCall(ScalarContext sc, StringBuilder body, ScalarObject o, Symbol methodSym, ulong fp)
    {
        var state = sc.state;
        var methodName = sc.methodName;
        var sym = sc.sym;
        var guardSlot = sc.guardSlot;
        ref var guardCount = ref sc.guardCount;
        if (!guardSlot.TryGetValue((o.ValueId, methodSym), out var slot))
        {
            slot = guardCount++;
            guardSlot[(o.ValueId, methodSym)] = slot;
        }
        if (!TrySymbolStringLiteral(state, o.ConstName, out var constLit) ||
            !TrySymbolStringLiteral(state, methodSym, out var methLit))
        {
            // Should not happen for resolved class/method names; be safe and never elide.
            Line(body, "{ result = default; return false; }");
            return;
        }
        var cls = methodName + "__sc" + slot + "cls";
        var ver = methodName + "__sc" + slot + "ver";
        Line(body,
            $"if (!ClassMethodGuardUnsafe(state, {sym.Reference(constLit)}, {sym.Reference(methLit)}, 0x{fp:x16}UL, ref {cls}, ref {ver})) {{ result = default; return false; }}");
    }

    // Accessor send on a scalar object -> direct field-local read/write. Returns false if
    // `ins` is not such a send (caller falls back to normal Send emission).
    internal static bool TryEmitAccessorSend(ScalarContext sc, RubyIRMethod exe, in RubyIRInstruction ins, StringBuilder body)
    {
        var objects = sc.objects;
        if (ins.OpCode is not RubyIROpCode.Send) return false;
        if (!objects.TryGetValue(ins.Src0, out var o)) return false;
        var msym = exe.GetCallSiteSymbol(ins.Aux);
        if (!o.Accessors.TryGetValue(msym, out var acc)) return false;
        var local = ScalarContext.FieldLocal(o.ValueId, acc.FieldIndex);
        if (acc.IsSetter)
        {
            var arg = exe.GetCallSiteArgumentValueId(ins.Aux, 0);
            Line(body, $"{local} = {Val(arg)};");
            Line(body, $"{Val(ins.Dst)} = {Val(arg)};"); // o.x = v evaluates to v
        }
        else
        {
            Line(body, $"{Val(ins.Dst)} = {local};");
        }
        return true;
    }

    internal static bool TryEmitScalarFieldAccess(ScalarContext sc, RubyIRMethod exe, in RubyIRInstruction ins, StringBuilder body)
    {
        if (!sc.TryGetScalarFieldAccess(exe, ins, out var objId, out var fieldIndex, out var isSetter))
        {
            return false;
        }

        var local = ScalarContext.FieldLocal(objId, fieldIndex);
        if (isSetter)
        {
            Line(body, $"{local} = {Val(ins.Src1)};");
        }
        else
        {
            Line(body, $"{Val(ins.Dst)} = {local};");
        }
        return true;
    }

    internal static bool TryEmitScalarInlineGuard(ScalarContext sc, RubyIRMethod exe, in RubyIRInstruction ins, StringBuilder body)
    {
        var objects = sc.objects;
        if (ins.OpCode != RubyIROpCode.GuardInlineClass ||
            !objects.TryGetValue(ins.Src0, out var o) ||
            !exe.TryGetGuardInline(ins.Src1, out var fp) ||
            fp == 0)
        {
            return false;
        }

        EmitScalarGuardCall(sc, body, o, exe.GetCallSiteSymbol(ins.Src1), fp);
        Line(body, $"goto L{ins.Aux};");
        return true;
    }

    internal static void EmitScalarFields(ScalarContext sc, StringBuilder sb)
    {
        var methodName = sc.methodName;
        var guardCount = sc.guardCount;
        for (var i = 0; i < guardCount; i++)
        {
            sb.Append("static global::ChibiRuby.RClass ").Append(methodName).Append("__sc").Append(i).Append("cls;\n");
            sb.Append("static int ").Append(methodName).Append("__sc").Append(i).Append("ver;\n");
        }
    }

    internal static bool TryEmitLiteral(MRubyValue v, out string expr)
    {
        if (v.IsFixnum)
        {
            expr = $"new global::ChibiRuby.MRubyValue({v.FixnumValue}L)";
            return true;
        }
        if (v.IsFloat)
        {
            // Plain `ldc.r8` literal for finite values (folds with no JIT budget, unlike
            // Int64BitsToDouble(const)); bit-exact reconstruction for NaN/Inf/non-roundtrippable.
            expr = $"new global::ChibiRuby.MRubyValue({Emitter.DoubleLitText(v.FloatValue)})";
            return true;
        }
        // Exact VType match only — symbols/strings are truthy immediates/objects
        // too, so a loose check would mis-emit them as `true`. Bail on those.
        switch (v.VType)
        {
            case MRubyVType.Nil:
                expr = "global::ChibiRuby.MRubyValue.Nil";
                return true;
            case MRubyVType.True:
                expr = "global::ChibiRuby.MRubyValue.True";
                return true;
            case MRubyVType.False:
                expr = "global::ChibiRuby.MRubyValue.False";
                return true;
            default:
                expr = "";
                return false;
        }
    }

    // Recognizes fixnum bitwise-operator method names with a safe C# equivalent.
    internal static bool TryFixnumBitwiseOp(MRubyState state, Symbol sym, out string op, out bool isShift)
    {
        op = "";
        isShift = false;
        var name = state.NameOf(sym).AsSpan();
        if (Matches(name, "&"u8)) { op = "&"; return true; }
        if (Matches(name, "|"u8)) { op = "|"; return true; }
        if (Matches(name, "^"u8)) { op = "^"; return true; }
        if (Matches(name, ">>"u8)) { op = ">>"; isShift = true; return true; }
        // Left shift: C# `long <<` matches Ruby for a 0..63 shift, modulo the fixnum-overflow->Bignum
        // promotion the AOT already ignores for `*`. The 0..63 guard routes negative/large shifts
        // (which Ruby treats as right-shift / Bignum) to the Send fallback.
        if (Matches(name, "<<"u8)) { op = "<<"; isShift = true; return true; }
        return false;
    }
}
