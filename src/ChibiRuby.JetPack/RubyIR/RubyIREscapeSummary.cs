using System.Collections.Generic;
using ChibiRuby;

using ChibiRuby.JetPack.Mrb2Cs;
namespace ChibiRuby.JetPack;

// Whole-program interprocedural escape/retention summary — the foundation for AOT object stack
// allocation. For each (class, selector, parameter) it answers: does this method RETAIN that
// parameter beyond the call (store it to the heap / return it / pass it to a callee that retains
// it / capture it / use it in an unmodeled way)? A parameter that is only FIELD-read/written and
// otherwise dropped does NOT escape — the caller may then build it on the stack (as a struct) and
// pass it by ref, instead of `new RObject`.
//
// Soundness is the whole point: claiming NonEscaping for a param that actually escapes would let
// stack state leak. So the lattice DEFAULTS TO Escaping and only proves NonEscaping; any unresolved
// callee / polymorphic receiver / unmodeled op forces Escaping. The monotone fixpoint flips a param
// false->true (NonEscaping->Escaping) and never back, mirroring RubyIRReturnTypes' discipline.
public static class RubyIREscapeSummary
{
    // Per (class, selector, param): the class-independent "hard" escape verdict plus the set of
    // selectors invoked on the param AS RECEIVER. A field read/write through the param's own
    // accessor never retains it, but whether a given selector IS an accessor depends on the param's
    // runtime class — which only the CALLER knows. So the query takes the arg's class and checks
    // those selectors against it.
    internal readonly struct ParamInfo(bool hardEscape, HashSet<Symbol> receiverSelectors)
    {
        public bool HardEscape { get; } = hardEscape;
        public HashSet<Symbol> ReceiverSelectors { get; } = receiverSelectors;
    }

    public sealed class Summary
    {
        readonly MRubyState state;
        readonly Dictionary<(RClass, Symbol, int), ParamInfo> infos;
        // Selectors defined by more than one class with differing hard-escape => treat as Escaping.
        readonly HashSet<(Symbol, int)> ambiguous;
        readonly Dictionary<Symbol, List<RClass>> selectorClasses;
        internal Summary(MRubyState state, Dictionary<(RClass, Symbol, int), ParamInfo> infos, HashSet<(Symbol, int)> ambiguous, Dictionary<Symbol, List<RClass>> selectorClasses)
        {
            this.state = state;
            this.infos = infos;
            this.ambiguous = ambiguous;
            this.selectorClasses = selectorClasses;
        }

        // Polymorphic-safe query for a call site whose receiver class isn't statically pinned: does
        // ANY class defining `selector` retain its arg at paramIndex when the arg is argClass? An
        // unknown selector (not in the AOT set) => true. Used at a `recv.sel(arg)` site to decide
        // whether `arg` (an argClass instance) can be passed without escaping, regardless of which
        // override runs.
        public bool SelectorRetains(Symbol selector, int paramIndex, RClass? argClass)
        {
            if (argClass is null) return true;
            if (!selectorClasses.TryGetValue(selector, out var classes) || classes.Count == 0) return true;
            foreach (var c in classes)
            {
                if (ParamEscapes(c, selector, paramIndex, argClass)) return true;
            }
            return false;
        }

        // The classes defining `selector` (for emitting a guarded variant dispatch per receiver class).
        public IReadOnlyList<RClass> DefiningClasses(Symbol selector) =>
            selectorClasses.TryGetValue(selector, out var c) ? c : System.Array.Empty<RClass>();

        // Does ANY class defining `selector` invoke a setter on the param at paramIndex (given the
        // arg is argClass)? Such a param is MUTATED by the callee -> needs by-`ref` (Stage 2), so
        // Stage 1 (read-only `in`) must not stack-allocate it. Unknown => true (safe: exclude).
        public bool SelectorMutates(Symbol selector, int paramIndex, RClass? argClass)
        {
            if (argClass is null) return true;
            if (!selectorClasses.TryGetValue(selector, out var classes) || classes.Count == 0) return true;
            foreach (var c in classes)
            {
                if (!infos.TryGetValue((c, selector, paramIndex), out var info)) return true;
                foreach (var sel in info.ReceiverSelectors)
                {
                    if (state.TryFindMethod(argClass, sel, out var m, out _) && m.Proc is { } pr &&
                        Analyzer.TryRecognizeTrivialAccessor(state, pr.Irep) is { IsSetter: true })
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Does (calleeClass, selector) retain its argument at position paramIndex (1-based), GIVEN
        // the argument is an instance of argClass? Unknown / ambiguous / non-accessor send on the
        // param => true (safe). The caller passes argClass (known at the allocation site) so the
        // param's own accessor sends can be resolved soundly.
        public bool ParamEscapes(RClass? calleeClass, Symbol selector, int paramIndex, RClass? argClass)
        {
            if (calleeClass is null || argClass is null) return true;
            if (ambiguous.Contains((selector, paramIndex))) return true;
            if (!infos.TryGetValue((calleeClass, selector, paramIndex), out var info)) return true;
            if (info.HardEscape) return true;
            // Every send invoked on the param must be a trivial accessor (getter/setter) on argClass
            // — those provably don't retain the receiver. Any other selector might store self.
            foreach (var sel in info.ReceiverSelectors)
            {
                if (!state.TryFindMethod(argClass, sel, out var method, out _) ||
                    method.Proc is not { } proc ||
                    Analyzer.TryRecognizeTrivialAccessor(state, proc.Irep) is null)
                {
                    return true;
                }
            }
            return false;
        }
    }

    sealed class Method
    {
        public RClass Cls = null!;
        public Symbol Selector;
        public RubyIRMethod Ir = null!;
        public int ArgCount;
        public int[] DefIndex = null!;
    }

    public static Summary Build(MRubyState state)
    {
        var methods = new List<Method>();
        // selector -> set of (class) defining it, to detect polymorphic/ambiguous escape later.
        var selectorDefs = new Dictionary<Symbol, List<Method>>();
        state.EnumerateAotMethods((cls, methodId, irep) =>
        {
            if (!TryReadArgCount(irep, out var argCount) || argCount == 0) return;
            RubyIRMethod? ir;
            try
            {
                ir = RubyIRBuilder.Build(irep, 0, out _);
                if (ir is not null) ir = RubyIRSsaRenumber.Run(ir, argCount);
            }
            catch { ir = null; }
            if (ir is null) return;
            var ins = ir.Instructions;
            var defIndex = new int[ir.ValueCount];
            for (var i = 0; i < defIndex.Length; i++) defIndex[i] = -1;
            for (var i = 0; i < ins.Length; i++) { var d = ins[i].Dst; if ((uint)d < (uint)defIndex.Length) defIndex[d] = i; }
            var m = new Method { Cls = cls, Selector = methodId, Ir = ir, ArgCount = argCount, DefIndex = defIndex };
            methods.Add(m);
            if (!selectorDefs.TryGetValue(methodId, out var list)) selectorDefs[methodId] = list = [];
            list.Add(m);
        });

        // Receiver-selector sets are class-independent (just which selectors are invoked on the
        // param), so compute them once. HardEscape is the class-independent retention verdict,
        // refined by a monotone fixpoint (false -> true only).
        var infos = new Dictionary<(RClass, Symbol, int), ParamInfo>();
        foreach (var m in methods)
        {
            for (var p = 1; p <= m.ArgCount; p++)
            {
                infos[(m.Cls, m.Selector, p)] = new ParamInfo(false, CollectReceiverSelectors(m, p));
            }
        }

        // Transitive (class-independent, conservative): a param passed onward as a callee arg
        // retains it unless the callee provably never retains that arg WITHOUT needing the class
        // (i.e. hard=false AND no receiver-selectors to validate). Unknown selector => retains.
        bool SelectorParamRetainsConservatively(Symbol sel, int paramIndex)
        {
            if (!selectorDefs.TryGetValue(sel, out var defs)) return true;
            foreach (var d in defs)
            {
                if (paramIndex > d.ArgCount) return true;
                var info = infos.GetValueOrDefault((d.Cls, d.Selector, paramIndex), new ParamInfo(true, []));
                if (info.HardEscape || info.ReceiverSelectors.Count > 0) return true;
            }
            return false;
        }

        var changed = true;
        var pass = 0;
        while (changed && pass++ < 64)
        {
            changed = false;
            foreach (var m in methods)
            {
                for (var p = 1; p <= m.ArgCount; p++)
                {
                    var info = infos[(m.Cls, m.Selector, p)];
                    if (info.HardEscape) continue;
                    if (ParamHardRetained(m, p, SelectorParamRetainsConservatively))
                    {
                        infos[(m.Cls, m.Selector, p)] = new ParamInfo(true, info.ReceiverSelectors);
                        changed = true;
                    }
                }
            }
        }

        // Ambiguous: a selector defined by multiple classes whose hard-escape differs for a param
        // -> caller (which only knows the selector) must assume escape.
        var ambiguous = new HashSet<(Symbol, int)>();
        foreach (var (sel, defs) in selectorDefs)
        {
            if (defs.Count < 2) continue;
            var maxArg = 0;
            foreach (var d in defs) if (d.ArgCount > maxArg) maxArg = d.ArgCount;
            for (var p = 1; p <= maxArg; p++)
            {
                var anyTrue = false; var anyFalse = false;
                foreach (var d in defs)
                {
                    var e = p > d.ArgCount || infos.GetValueOrDefault((d.Cls, d.Selector, p), new ParamInfo(true, [])).HardEscape;
                    if (e) anyTrue = true; else anyFalse = true;
                }
                if (anyTrue && anyFalse) ambiguous.Add((sel, p));
            }
        }

        var selectorClasses = new Dictionary<Symbol, List<RClass>>();
        foreach (var (sel, defs) in selectorDefs)
        {
            var list = new List<RClass>(defs.Count);
            foreach (var d in defs) list.Add(d.Cls);
            selectorClasses[sel] = list;
        }
        return new Summary(state, infos, ambiguous, selectorClasses);
    }

    // Selectors invoked on param p as the RECEIVER (Send only). Each must be validated as a trivial
    // accessor against the arg's class at query time.
    static HashSet<Symbol> CollectReceiverSelectors(Method m, int p)
    {
        var ir = m.Ir;
        var ins = ir.Instructions;
        var aliases = AliasesOf(ir, p);
        var sels = new HashSet<Symbol>();
        for (var i = 0; i < ins.Length; i++)
        {
            var u = ins[i];
            if (u.OpCode == RubyIROpCode.Send && aliases.Contains(u.Src0))
            {
                sels.Add(ir.GetCallSiteSymbol(u.Aux));
            }
        }
        return sels;
    }

    static HashSet<int> AliasesOf(RubyIRMethod ir, int p)
    {
        var ins = ir.Instructions;
        var aliases = new HashSet<int> { p };
        var grew = true;
        while (grew)
        {
            grew = false;
            for (var i = 0; i < ins.Length; i++)
            {
                if (ins[i].OpCode == RubyIROpCode.Move && aliases.Contains(ins[i].Src0))
                {
                    if (aliases.Add(ins[i].Dst)) grew = true;
                }
            }
        }
        return aliases;
    }

    // Class-INDEPENDENT retention: does method m retain param p in a way that holds regardless of
    // p's class? (Receiver-as-param Send selectors are NOT hard — they're validated per-class at
    // query time via ReceiverSelectors.) Returns true on any return/store/array/ctor-arg/onward-
    // pass-that-retains/closure-capture/unmodeled use. Aliases via Move are followed.
    static bool ParamHardRetained(Method m, int p, System.Func<Symbol, int, bool> selectorRetains)
    {
        var ir = m.Ir;
        var ins = ir.Instructions;
        foreach (var captured in ir.ClosureCapturedValueIds)
        {
            if (captured == p) return true;
        }
        var aliases = AliasesOf(ir, p);
        foreach (var captured in ir.ClosureCapturedValueIds)
        {
            if (aliases.Contains(captured)) return true;
        }

        for (var i = 0; i < ins.Length; i++)
        {
            var u = ins[i];
            var op = u.OpCode;
            if (op == RubyIROpCode.Return && aliases.Contains(u.Src0)) return true;
            if (op is RubyIROpCode.SetInstanceVariable or RubyIROpCode.VirtualSetField)
            {
                if (aliases.Contains(u.Src1)) return true; // param stored as the value
                continue; // Src0 == param == writing the param's own field == mutation, ok
            }
            if (op is RubyIROpCode.SetIndex)
            {
                if (aliases.Contains(u.Src1) || aliases.Contains(u.Src2)) return true;
                continue;
            }
            if (op is RubyIROpCode.NewArray or RubyIROpCode.NewArray2)
            {
                var c = ir.GetOperandListCount(u.Aux);
                for (var a = 0; a < c; a++) if (aliases.Contains(ir.GetOperandListValueId(u.Aux, a))) return true;
                continue;
            }
            if (op is RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField)
            {
                continue; // reading the param's field is fine
            }
            if (IsSendOp(op) || op is RubyIROpCode.PureUnarySend)
            {
                var sel = ir.GetCallSiteSymbol(u.Aux);
                var argc = ir.GetCallSiteArgumentCount(u.Aux);
                // Receiver-as-param: a plain Send is deferred to per-class accessor validation
                // (ReceiverSelectors). A PureUnarySend (numeric op) on the param is unmodeled.
                if (aliases.Contains(u.Src0) && op is not RubyIROpCode.Send) return true;
                for (var a = 0; a < argc; a++)
                {
                    if (aliases.Contains(ir.GetCallSiteArgumentValueId(u.Aux, a)) && selectorRetains(sel, a + 1)) return true;
                }
                continue;
            }
            if (op is RubyIROpCode.VirtualNew)
            {
                if (aliases.Contains(u.Src0)) return true;
                var argc = ir.GetCallSiteArgumentCount(u.Aux);
                for (var a = 0; a < argc; a++) if (aliases.Contains(ir.GetCallSiteArgumentValueId(u.Aux, a))) return true;
                continue;
            }
            if (aliases.Contains(u.Src0) || aliases.Contains(u.Src1) || aliases.Contains(u.Src2))
            {
                if (op is RubyIROpCode.Move or RubyIROpCode.GuardInlineClass) continue;
                return true;
            }
        }
        return false;
    }

    static bool IsSendOp(RubyIROpCode op) => op is RubyIROpCode.Send or RubyIROpCode.SendSelf;

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
}
