using System.Collections.Generic;
using ChibiRuby;

using ChibiRuby.JetPack.Mrb2Cs;
namespace ChibiRuby.JetPack;

// Numeric kind of a value / method result / ivar. Unknown also serves as the lattice bottom
// (a conflict between two known kinds), so a meet of disagreeing kinds collapses to Unknown.
enum RubyNumKind : byte { Unknown = 0, Integer, Float }

// Whole-program numeric return-type inference over the AOT method set — the principled replacement
// for matching method NAMES. It infers, to a fixpoint:
//   - each selector's return kind (Float / Integer), but ONLY when every method defining that
//     selector provably returns that kind (a disagreement -> Unknown; mirrors accessor ambiguity);
//   - each (class, ivar)'s kind, from every self-write in the class's methods, with the ivar's
//     `initialize` param fed by the arg kinds at every resolvable `Class.new` call site.
// kind(value) is computed from the IR (literals, ivar reads, arith promotion, sends-by-inferred-
// selector). Everything is conservative: any unresolved send / ivar / `.new` leaves the result
// Unknown. Soundness matters because the float result feeds provesDouble, which emits unguarded
// double reads — a wrong "Float" would silently miscompile, so we only ever claim a kind we prove.
public static class RubyIRReturnTypes
{
    public sealed class Registry
    {
        readonly Dictionary<Symbol, RubyNumKind> selectorReturn;
        readonly HashSet<RClass> floatUsingClasses;
        readonly Dictionary<(RClass, Symbol), RubyNumKind> ivarKind;
        internal Registry(
            Dictionary<Symbol, RubyNumKind> selectorReturn,
            HashSet<RClass> floatUsingClasses,
            Dictionary<(RClass, Symbol), RubyNumKind> ivarKind)
        {
            this.selectorReturn = selectorReturn;
            this.floatUsingClasses = floatUsingClasses;
            this.ivarKind = ivarKind;
        }
        public bool ReturnsFloat(Symbol selector) => selectorReturn.GetValueOrDefault(selector) == RubyNumKind.Float;
        public bool ReturnsInteger(Symbol selector) => selectorReturn.GetValueOrDefault(selector) == RubyNumKind.Integer;
        // Does this class touch floats anywhere? Float speculation is only safe to attempt here.
        public bool ClassUsesFloat(RClass? cls) => cls is not null && floatUsingClasses.Contains(cls);
        // The whole-program-proven numeric kind of (class, @ivar) — Unknown unless EVERY self-write
        // in the class agrees. Sound enough to feed UNGUARDED unboxing (see Build's least-fixpoint).
        internal RubyNumKind IvarKind(RClass? cls, Symbol ivar) =>
            cls is null ? RubyNumKind.Unknown : ivarKind.GetValueOrDefault((cls, ivar));
        public bool IvarReturnsFloat(RClass? cls, Symbol ivar) => IvarKind(cls, ivar) == RubyNumKind.Float;
        public bool IvarReturnsInteger(RClass? cls, Symbol ivar) => IvarKind(cls, ivar) == RubyNumKind.Integer;
    }

    sealed class Method
    {
        public RClass Cls = null!;
        public Symbol Selector;
        public RubyIRMethod Ir = null!;
        public int ArgCount;
        public int[] DefIndex = null!;
        public bool IsInitialize;
    }

    public static Registry Build(MRubyState state)
    {
        var initializeSym = state.Intern("initialize"u8);
        var methods = new List<Method>();
        state.EnumerateAotMethods((cls, methodId, irep) =>
        {
            if (!TryReadArgCount(irep, out var argCount)) return;
            RubyIRMethod? ir;
            try
            {
                ir = RubyIRBuilder.Build(irep, 0, out _);
                // Analyze SSA-renumbered IR: otherwise a conflated merge slot (e.g. `t` in
                // intersect) reads as Unknown and poisons everything that flows from it.
                if (ir is not null) ir = RubyIRSsaRenumber.Run(ir, argCount);
            }
            catch { ir = null; }
            if (ir is null) return;
            var ins = ir.Instructions;
            var defIndex = new int[ir.ValueCount];
            for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
            for (var i = 0; i < ins.Length; i++) { var d = ins[i].Dst; if ((uint)d < (uint)defIndex.Length) defIndex[d] = i; }
            methods.Add(new Method
            {
                Cls = cls,
                Selector = methodId,
                Ir = ir,
                ArgCount = argCount,
                DefIndex = defIndex,
                IsInitialize = methodId == initializeSym,
            });
        });

        // Ivars written on a non-self target anywhere are not attributable to a single class, so
        // they are never trusted (kept Unknown) — keeps the (class, ivar) inference sound.
        var untrustedIvars = new HashSet<Symbol>();
        // A `Class.new` whose class we cannot resolve could feed any initialize; poison all
        // initialize-param inference rather than risk an unseen non-float arg.
        var hasUnresolvedNew = false;
        // Resolvable `.new` sites: (target class, the caller method, the VirtualNew callsite aux).
        var newSites = new List<(RClass Target, Method Caller, int Aux)>();
        // Classes that demonstrably touch floats anywhere (a float literal/const or a Math/to_f
        // send). Float speculation is enabled ONLY for these — an all-integer class (e.g. every
        // optcarrot class) never speculates an int ivar as Float and constant-deopts.
        var floatUsingClasses = new HashSet<RClass>();
        foreach (var m in methods)
        {
            var ins = m.Ir.Instructions;
            for (var i = 0; i < ins.Length; i++)
            {
                var op = ins[i].OpCode;
                if (op is RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField)
                {
                    if (ins[i].Src0 != 0) untrustedIvars.Add(m.Ir.GetSymbol(ins[i].Aux));
                }
                else if (op == RubyIROpCode.VirtualNew)
                {
                    if (TryResolveNewClass(state, m, i, out var target)) newSites.Add((target, m, ins[i].Aux));
                    else hasUnresolvedNew = true;
                }
                else if ((op == RubyIROpCode.LoadValue && m.Ir.GetLiteral(ins[i].Aux).IsFloat) ||
                         (op == RubyIROpCode.GetConstant && Analyzer.IsFloatConstantName(state, m.Ir.GetSymbol(ins[i].Aux))) ||
                         (op is (RubyIROpCode.Send or RubyIROpCode.SendSelf) && Analyzer.IsBuiltinFloatMethod(state, m.Ir.GetCallSiteSymbol(ins[i].Aux))))
                {
                    floatUsingClasses.Add(m.Cls);
                }
            }
        }

        // Least fixpoint (sound by construction): start with no proofs and only ADD a Float/Integer
        // proof once a slot's sources establish it; anything cyclic or unresolved stays Unknown.
        // This is the safe choice because the Float result feeds provesDouble, which emits UNGUARDED
        // double reads — an over-claimed Float would silently miscompile. (A precise cyclic-float
        // proof — e.g. Vec#@x written by its own `x=` whose arg reads @x — needs a greatest fixpoint
        // with a speculation guard at the use; that's the next step. Here cyclic ivars stay Unknown
        // and the pre-side-effect speculation covers them instead.)
        var selectorReturn = new Dictionary<Symbol, RubyNumKind>();
        var ivarKind = new Dictionary<(RClass, Symbol), RubyNumKind>();
        var initParam = new Dictionary<(RClass, int), RubyNumKind>();
        var selectorParam = new Dictionary<(Symbol, int), RubyNumKind>();

        // Jacobi fixpoint: recompute each registry from the previous iteration. Monotone (proofs
        // only accumulate) so it converges; capped for safety.
        for (var pass = 0; pass < 64; pass++)
        {
            var ctx = new Ctx(state, selectorReturn, ivarKind, initParam, selectorParam);

            var newInitParam = new Dictionary<(RClass, int), RubyNumKind>();
            if (!hasUnresolvedNew)
            {
                foreach (var (target, caller, aux) in newSites)
                {
                    var argc = caller.Ir.GetCallSiteArgumentCount(aux);
                    for (var a = 0; a < argc; a++)
                    {
                        // `.new` arg a maps to initialize's param register a+1 (v0 is self).
                        MeetInto(newInitParam, (target, a + 1), ctx.KindOf(caller, caller.Ir.GetCallSiteArgumentValueId(aux, a)));
                    }
                }
            }

            // Every other selector's params from its call sites (the arg kinds at each `recv.sel(args)`).
            var newSelectorParam = new Dictionary<(Symbol, int), RubyNumKind>();
            foreach (var m in methods)
            {
                var ins = m.Ir.Instructions;
                for (var i = 0; i < ins.Length; i++)
                {
                    if (ins[i].OpCode is not (RubyIROpCode.Send or RubyIROpCode.SendSelf)) continue;
                    var sel = m.Ir.GetCallSiteSymbol(ins[i].Aux);
                    var argc = m.Ir.GetCallSiteArgumentCount(ins[i].Aux);
                    for (var a = 0; a < argc; a++)
                    {
                        MeetInto(newSelectorParam, (sel, a + 1), ctx.KindOf(m, m.Ir.GetCallSiteArgumentValueId(ins[i].Aux, a)));
                    }
                }
            }

            var newIvar = new Dictionary<(RClass, Symbol), RubyNumKind>();
            foreach (var m in methods)
            {
                var ins = m.Ir.Instructions;
                for (var i = 0; i < ins.Length; i++)
                {
                    if (ins[i].OpCode is not (RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField)) continue;
                    if (ins[i].Src0 != 0) continue; // non-self target: handled via untrustedIvars
                    var ivar = m.Ir.GetSymbol(ins[i].Aux);
                    MeetInto(newIvar, (m.Cls, ivar), untrustedIvars.Contains(ivar) ? RubyNumKind.Unknown : ctx.KindOf(m, ins[i].Src1));
                }
            }

            var newSelector = new Dictionary<Symbol, RubyNumKind>();
            foreach (var m in methods)
            {
                MeetInto(newSelector, m.Selector, ctx.ReturnKindOf(m));
            }

            if (DictEq(newSelector, selectorReturn) && DictEq(newIvar, ivarKind) &&
                DictEq(newInitParam, initParam) && DictEq(newSelectorParam, selectorParam))
            {
                break;
            }
            selectorReturn = newSelector;
            ivarKind = newIvar;
            initParam = newInitParam;
            selectorParam = newSelectorParam;
        }

        return new Registry(selectorReturn, floatUsingClasses, ivarKind);
    }

    // Per-pass kind computation, reading the previous pass's registries.
    sealed class Ctx(
        MRubyState state,
        Dictionary<Symbol, RubyNumKind> selectorReturn,
        Dictionary<(RClass, Symbol), RubyNumKind> ivarKind,
        Dictionary<(RClass, int), RubyNumKind> initParam,
        Dictionary<(Symbol, int), RubyNumKind> selectorParam)
    {
        public RubyNumKind ReturnKindOf(Method m)
        {
            var ins = m.Ir.Instructions;
            var acc = (RubyNumKind?)null;
            var any = false;
            for (var i = 0; i < ins.Length; i++)
            {
                switch (ins[i].OpCode)
                {
                    case RubyIROpCode.Return:
                        acc = Meet(acc, KindOf(m, ins[i].Src0)); any = true; break;
                    case RubyIROpCode.ReturnValue:
                    {
                        var lit = m.Ir.GetLiteral(ins[i].Aux);
                        acc = Meet(acc, lit.IsFloat ? RubyNumKind.Float : lit.IsFixnum ? RubyNumKind.Integer : RubyNumKind.Unknown);
                        any = true; break;
                    }
                    case RubyIROpCode.ReturnSelf:
                        acc = Meet(acc, RubyNumKind.Unknown); any = true; break;
                }
            }
            return any ? acc ?? RubyNumKind.Unknown : RubyNumKind.Unknown;
        }

        public RubyNumKind KindOf(Method m, int v) => KindOf(m, v, new RubyNumKind?[m.Ir.ValueCount]);

        RubyNumKind KindOf(Method m, int v, RubyNumKind?[] memo)
        {
            if ((uint)v >= (uint)memo.Length) return RubyNumKind.Unknown;
            if (memo[v] is { } cached) return cached;
            memo[v] = RubyNumKind.Unknown; // break cycles conservatively
            var kind = ComputeKind(m, v, memo);
            memo[v] = kind;
            return kind;
        }

        RubyNumKind ComputeKind(Method m, int v, RubyNumKind?[] memo)
        {
            var d = m.DefIndex[v];
            if (d < 0)
            {
                // No instruction defines it: a parameter. initialize's params are typed from .new
                // call sites (class-keyed); other methods' params from their call sites (selector-keyed).
                if (v >= 1 && v <= m.ArgCount)
                {
                    return m.IsInitialize
                        ? initParam.GetValueOrDefault((m.Cls, v))
                        : selectorParam.GetValueOrDefault((m.Selector, v));
                }
                return RubyNumKind.Unknown;
            }
            var ins = m.Ir.Instructions[d];
            var op = ins.OpCode;
            switch (op)
            {
                case RubyIROpCode.LoadValue:
                {
                    var lit = m.Ir.GetLiteral(ins.Aux);
                    return lit.IsFloat ? RubyNumKind.Float : lit.IsFixnum ? RubyNumKind.Integer : RubyNumKind.Unknown;
                }
                case RubyIROpCode.GetConstant:
                    return Analyzer.IsFloatConstantName(state, m.Ir.GetSymbol(ins.Aux)) ? RubyNumKind.Float : RubyNumKind.Unknown;
                case RubyIROpCode.GetInstanceVariable:
                case RubyIROpCode.VirtualGetField:
                    return ivarKind.GetValueOrDefault((m.Cls, m.Ir.GetSymbol(ins.Aux)));
                case RubyIROpCode.Move:
                    return KindOf(m, ins.Src0, memo);
                case RubyIROpCode.Send:
                case RubyIROpCode.SendSelf:
                {
                    var sel = m.Ir.GetCallSiteSymbol(ins.Aux);
                    if (Analyzer.IsBuiltinFloatMethod(state, sel)) return RubyNumKind.Float;
                    // Numeric unary +@/-@ and abs preserve the receiver's kind (Float.-@ -> Float,
                    // Integer.-@ -> Integer). `-b` lowers to a `-@` send, so without this a negated
                    // float reads as Unknown and poisons everything downstream.
                    if (m.Ir.GetCallSiteArgumentCount(ins.Aux) == 0 &&
                        state.NameOf(sel).ToString() is "-@" or "+@" or "abs")
                    {
                        return KindOf(m, ins.Src0, memo);
                    }
                    return selectorReturn.GetValueOrDefault(sel);
                }
                // typed arith carry their kind; generic arith promotes from operands.
                case RubyIROpCode.AddFixnum:
                case RubyIROpCode.SubFixnum:
                case RubyIROpCode.MulFixnum:
                case RubyIROpCode.DivFixnum:
                case RubyIROpCode.AddImmediateFixnum:
                case RubyIROpCode.SubImmediateFixnum:
                    return RubyNumKind.Integer;
                case RubyIROpCode.AddFloat:
                case RubyIROpCode.SubFloat:
                case RubyIROpCode.MulFloat:
                case RubyIROpCode.DivFloat:
                case RubyIROpCode.MulAddFloat:
                case RubyIROpCode.MulSubFloat:
                case RubyIROpCode.SubMulFloat:
                case RubyIROpCode.AddImmediateFloat:
                case RubyIROpCode.SubImmediateFloat:
                    return RubyNumKind.Float;
                case RubyIROpCode.AddImmediate:
                case RubyIROpCode.SubImmediate:
                {
                    var lit = m.Ir.GetLiteral(ins.Aux);
                    var litKind = lit.IsFloat ? RubyNumKind.Float : lit.IsFixnum ? RubyNumKind.Integer : RubyNumKind.Unknown;
                    return Promote(KindOf(m, ins.Src0, memo), litKind);
                }
                default:
                    if (RubyIROpInfo.IsDoubleArith(op))
                    {
                        var k = Promote(KindOf(m, ins.Src0, memo), KindOf(m, ins.Src1, memo));
                        if (RubyIROpInfo.IsDoubleFused(op)) k = Promote(k, KindOf(m, ins.Src2, memo));
                        return k;
                    }
                    return RubyNumKind.Unknown;
            }
        }

        // Ruby numeric promotion for `+ - * / **`-style arith: Int op Int = Int; Float anywhere = Float;
        // an Unknown (possibly non-numeric / untyped) operand can't be proven, so the result is Unknown.
        static RubyNumKind Promote(RubyNumKind a, RubyNumKind b)
        {
            if (a == RubyNumKind.Unknown || b == RubyNumKind.Unknown) return RubyNumKind.Unknown;
            return a == RubyNumKind.Float || b == RubyNumKind.Float ? RubyNumKind.Float : RubyNumKind.Integer;
        }
    }

    static bool TryResolveNewClass(MRubyState state, Method m, int newIndex, out RClass cls)
    {
        cls = null!;
        var classVid = m.Ir.Instructions[newIndex].Src0;
        var ins = m.Ir.Instructions;
        for (var hops = 0; hops < ins.Length; hops++)
        {
            if ((uint)classVid >= (uint)m.DefIndex.Length) return false;
            var d = m.DefIndex[classVid];
            if (d < 0) return false;
            var def = ins[d];
            if (def.OpCode == RubyIROpCode.Move) { classVid = def.Src0; continue; } // trace copy chains
            if (def.OpCode != RubyIROpCode.GetConstant) return false;
            if (!state.TryGetConst(m.Ir.GetSymbol(def.Aux), out var value) || value.Object is not RClass klass) return false;
            cls = klass;
            return true;
        }
        return false;
    }

    static bool TryReadArgCount(Irep irep, out int argCount)
    {
        argCount = 0;
        var seq = irep.Sequence;
        if (seq.Length == 0 || (OpCode)seq[0] != OpCode.Enter) return false;
        var aspec = new ArgumentSpec(((uint)seq[1] << 16) | ((uint)seq[2] << 8) | seq[3]);
        if (aspec.OptionalArgumentsCount != 0 || aspec.TakeRestArguments ||
            aspec.MandatoryArguments2Count != 0 || aspec.KeywordArgumentsCount != 0 ||
            aspec.TakeKeywordDict || aspec.TakeBlock)
        {
            return false;
        }
        argCount = aspec.MandatoryArguments1Count;
        return true;
    }

    static RubyNumKind Meet(RubyNumKind? acc, RubyNumKind x) => acc is not { } a ? x : a == x ? a : RubyNumKind.Unknown;

    static void MeetInto<TKey>(Dictionary<TKey, RubyNumKind> map, TKey key, RubyNumKind kind) where TKey : notnull =>
        map[key] = map.TryGetValue(key, out var prev) ? (prev == kind ? prev : RubyNumKind.Unknown) : kind;

    static bool DictEq<TKey>(Dictionary<TKey, RubyNumKind> a, Dictionary<TKey, RubyNumKind> b) where TKey : notnull
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
        }
        return true;
    }
}
