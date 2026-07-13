using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace ChibiRuby.JetPack.Mrb2Cs;

// Build-time Ruby -> C# codegen. Builds a method's Irep into RubyIR (via RubyIRBuilder.Build,
// which also runs escape-analysis op rewriting + arithmetic fusion), runs analyses over that IR
// (SSA renumbering, escape/scalar replacement, type inference / unboxing), then walks the IR
// and emits a C# method matching CompiledRubyMethodBody: bool (MRubyState, int sp, out MRubyValue).
//
// The generated code targets only PUBLIC ChibiRuby APIs (it lives in the user's
// assembly, no InternalsVisibleTo). Symbols (ivar names, send method ids) are interned
// ONCE per state into per-method static fields (state-keyed so it stays correct across
// multiple states) instead of per call. Control flow is labels + gotos mirroring the
// IR's forward branches; all value-id locals are pre-declared (default-init) so gotos
// never trip C# definite-assignment. Arithmetic/comparison is speculatively typed
// (fixnum fast path + guard->deopt). Unhandled ops -> TryCompileMethod returns false.
public static class Mrb2CsCompiler
{
    // Diagnostic: set to the reason TryCompileMethod last returned false (for coverage analysis).
    [ThreadStatic]
    internal static string? LastBail;

    // SSA live-range splitting (+ its SSA-driven splice detection and pre-side-effect float
    // window) is ON by default; set AOT_NOSSA=1 to fall back to the register-reused path.
    internal static bool SsaEnabled => Environment.GetEnvironmentVariable("AOT_NOSSA") != "1";

    // Diagnostic: devirtualize+inline surface. Counted across a compile run when a
    // defining class is supplied. SelfSendSites = all SendSelf in compiled methods;
    // ResolvedSelfSends = those resolving to an RProc method in the defining class;
    // InlinableSelfSends = resolved + callee is a small leaf that itself AOT-lowers.
    internal static int UnboxedLocals;

    static void ResetInlineSurface()
    {
        UnboxedLocals = 0;
    }


    // Per-method irep fingerprint -> readable `_rubyname` suffix, so the C# method name carries the
    // original Ruby method name (set by Compile; null for standalone TryCompileMethod). Read via MethodCsName.
    [ThreadStatic]
    internal static Dictionary<ulong, string>? MethodNameSuffixes;

    // Fingerprints of methods whose selector is a (deduped) trivial accessor: every call to them —
    // self or cross-object — devirtualizes to direct field access, so they are never `__inline`-called
    // and don't need the frameless `__inline` form. Set by Compile; null for standalone TryCompileMethod.
    [ThreadStatic]
    internal static HashSet<ulong>? AccessorFingerprints;

    // mrb2cs: compile every statically-compilable method in an irep tree to one C# class.
    // Walks the tree, generates each method (dedup by fingerprint), and emits a single
    // `class <className> : ChibiRuby.AotGeneratedMethods` source. The caller compiles that
    // source (Roslyn at runtime, or csc at build time) and binds Methods by fingerprint.
    // `state` must already have executed the program so its classes/methods are defined.
    public static ProgramResult Compile(MRubyState state, Irep root, string className = "Mrb2CsGenerated", string? namespaceName = null)
    {
        // Each method irep -> its defining class, so the codegen can resolve self-sends; and its
        // fingerprint -> a readable `_rubyname` suffix, so the emitted C# method name carries the
        // original Ruby method name (MethodCsName reads the latter via the thread-static below).
        var classOf = new Dictionary<Irep, RClass>();
        var suffixes = new Dictionary<ulong, string>();
        state.EnumerateAotMethods((definingClass, methodId, irep) =>
        {
            classOf[irep] = definingClass;
            suffixes[state.ComputeIrepFingerprint(irep)] = Emitter.NameSuffixFor(Encoding.UTF8.GetString(state.NameOf(methodId)));
        });
        MethodNameSuffixes = suffixes;

        var accessorRegistry = Analyzer.BuildAccessorRegistry(state);
        // Fingerprints of the methods those accessor selectors denote — every send to them devirts to
        // field access, so they skip the frameless `__inline` form (read in TryCompileMethod's emit step).
        var accFps = new HashSet<ulong>();
        state.EnumerateAotMethods((_, methodId, irep) =>
        {
            if (accessorRegistry.ContainsKey(methodId)) accFps.Add(state.ComputeIrepFingerprint(irep));
        });
        AccessorFingerprints = accFps;
        // Constant-returning-method devirt registry (set thread-static; sound regardless of staleness
        // because every emitted call site is fingerprint-guarded). Disable with AOT_NOCONSTRET=1.
        CurrentConstReturns = Environment.GetEnvironmentVariable("AOT_NOCONSTRET") == "1" ? null : Analyzer.BuildConstReturnRegistry(state);

        // Pass 1: which methods are small enough to inline, and their arg counts.
        const int inlineMaxInstructions = 48;
        var inlineRegistry = new Dictionary<ulong, int>();
        var seen1 = new HashSet<ulong>();
        Collect(root);

        var inlineSelectorRegistry = Analyzer.BuildInlineSelectorRegistry(state, inlineRegistry);
        var returnTypes = RubyIRReturnTypes.Build(state);
        // Whole-program retention summary for stack allocation (set once; read per-method via the
        // thread-static below). Off switch mirrors the other AOT_NO* gates.
        CurrentEscapeSummary = Environment.GetEnvironmentVariable("AOT_NOSTACKOBJ") == "1"
            ? null
            : RubyIREscapeSummary.Build(state);
        NeededStructs = new Dictionary<ulong, StackLayout>();
        NeededVariants = new Dictionary<string, (RClass Callee, Symbol Selector, Dictionary<int, StackLayout> StructParams)>();

        // Pass 2: emit final sources, inlining monomorphic self / selected cross-object sends.
        var seen = new HashSet<ulong>();
        var sources = new List<string>();
        var methods = new List<(string, ulong)>();
        var total = 0;
        var bails = new Dictionary<string, int>();
        ResetInlineSurface();
        Walk(root);

        // Pass 3: generate the specialized struct-by-ref callee variants requested during Pass 2.
        // (Worklist: a variant could request more; Stage 1's intersect doesn't, but loop safely.)
        var emittedVariants = new HashSet<string>();
        while (NeededVariants.Count > emittedVariants.Count)
        {
            foreach (var (variantName, spec) in new List<KeyValuePair<string, (RClass Callee, Symbol Selector, Dictionary<int, StackLayout> StructParams)>>(NeededVariants))
            {
                if (!emittedVariants.Add(variantName)) continue;
                if (!state.TryFindMethod(spec.Callee, spec.Selector, out var m, out _) || m.Proc is not { } proc) continue;
                CurrentStructParams = spec.StructParams;
                try
                {
                    if (TryCompileMethod(state, proc.Irep, variantName, spec.Callee, inlineRegistry, accessorRegistry, inlineSelectorRegistry, returnTypes, out var v))
                    {
                        sources.Add(v.Source);
                        sources.AddRange(v.AuxiliaryMethods);
                    }
                }
                finally { CurrentStructParams = null; }
            }
        }

        if (Environment.GetEnvironmentVariable("AOT_COVERAGE") == "1")
        {
            Console.WriteLine($"mrb2cs coverage: {methods.Count}/{total} compiled, {total - methods.Count} bailed; {UnboxedLocals} unboxed long locals; {NeededStructs.Count} stack structs, {emittedVariants.Count} variants");
            foreach (var kv in bails.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  bail {kv.Key}: {kv.Value}");
            }
        }

        // Gather the stack struct types the methods use, transitively (a Stk_Ray with a Stk_Vec
        // field needs Stk_Vec declared too); the emitter writes their declarations before the methods.
        var allStructs = new Dictionary<ulong, StackLayout>();
        foreach (var lay in NeededStructs.Values) CollectStruct(lay);
        return new ProgramResult(Emitter.EmitProgram(className, namespaceName, allStructs.Values, sources), methods);

        void Walk(Irep irep)
        {
            var fp = state.ComputeIrepFingerprint(irep);
            if (seen.Add(fp))
            {
                total++;
                if (TryCompileMethod(state, irep, Emitter.MethodCsName(fp), classOf.GetValueOrDefault(irep), inlineRegistry, accessorRegistry, inlineSelectorRegistry, returnTypes, out var gen))
                {
                    sources.Add(gen.Source);
                    methods.Add((Emitter.MethodCsName(fp), fp));
                    foreach (var aux in gen.AuxiliaryMethods) sources.Add(aux); // __blk bodies, called by name
                }
                else
                {
                    var reason = LastBail ?? "?";
                    bails[reason] = bails.GetValueOrDefault(reason) + 1;
                }
            }
            foreach (var child in irep.Children) Walk(child);
        }

        void CollectStruct(StackLayout l)
        {
            if (!allStructs.TryAdd(l.ClassFp, l)) return;
            foreach (var n in l.FieldNested.OfType<StackLayout>()) CollectStruct(n);
        }

        void Collect(Irep irep)
        {
            var fp = state.ComputeIrepFingerprint(irep);
            if (seen1.Add(fp))
            {
                if (TryCompileMethod(state, irep, Emitter.MethodCsName(fp), classOf.GetValueOrDefault(irep), null, accessorRegistry, out var gen)
                    && gen.InstructionCount <= inlineMaxInstructions) inlineRegistry[fp] = gen.ArgCount;
            }
            foreach (var child in irep.Children) Collect(child);
        }
    }

    // Compile one Ruby method's Irep to a C# method body. Returns false (and method = null) when the
    // method can't be AOT-compiled (an unsupported op / shape bails — see LastBail); true with the
    // emitted CompiledMethod otherwise.
    public static bool TryCompileMethod(MRubyState state, Irep irep, string methodName,
        [MaybeNullWhen(false)] out CompiledMethod method) =>
        TryCompileMethod(state, irep, methodName, null, null, out method);

    public static bool TryCompileMethod(MRubyState state, Irep irep, string methodName, RClass? definingClass,
        IReadOnlyDictionary<ulong, int>? inlineRegistry,
        [MaybeNullWhen(false)] out CompiledMethod method) =>
        TryCompileMethod(state, irep, methodName, definingClass, inlineRegistry, null, out method);

    // inlineRegistry: fp -> argCount of methods safe to inline (compiled + leaf + small).
    // When set (and definingClass known), monomorphic self-sends to those callees are
    // emitted as a guarded direct call to the callee's __inline form (frameless), with a
    // Send fallback. Built in a first pass; this is the second pass. accessorRegistry: selector
    // -> trivial accessor, for guarded cross-object getter/setter devirtualization.
    public static bool TryCompileMethod(MRubyState state, Irep irep, string methodName, RClass? definingClass,
        IReadOnlyDictionary<ulong, int>? inlineRegistry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry,
        [MaybeNullWhen(false)] out CompiledMethod method) =>
        TryCompileMethod(state, irep, methodName, definingClass, inlineRegistry, accessorRegistry, null, out method);

    public static bool TryCompileMethod(MRubyState state, Irep irep, string methodName, RClass? definingClass,
        IReadOnlyDictionary<ulong, int>? inlineRegistry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry,
        IReadOnlyDictionary<Symbol, InlineSelectorTarget>? inlineSelectorRegistry,
        [MaybeNullWhen(false)] out CompiledMethod method) =>
        TryCompileMethod(state, irep, methodName, definingClass, inlineRegistry, accessorRegistry, inlineSelectorRegistry, null, out method);

    // returnTypes: whole-program inferred numeric return kinds (RubyIRReturnTypes.Build), used to
    // recognize Float/Integer-returning user methods instead of matching names. Null -> only
    // builtin float methods are recognized.
    static bool TryCompileMethod(
        MRubyState state,
        Irep irep,
        string methodName,
        RClass? definingClass,
        IReadOnlyDictionary<ulong, int>? inlineRegistry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessorRegistry,
        IReadOnlyDictionary<Symbol, InlineSelectorTarget>? inlineSelectorRegistry,
        RubyIRReturnTypes.Registry? returnTypes,
        [MaybeNullWhen(false)] out CompiledMethod method)
    {
        method = null;
        // The RubyIR lowerer is not robust on every shape (it can throw rather than
        // bail). Treat any lowering failure/exception as "not compilable".
        CurrentReturnTypes = returnTypes;
        LastBail = null;
        RubyIRMethod? ir;
        RubyIRBuildFailure failure;
        try
        {
            ir = RubyIRBuilder.Build(irep, 0, out failure);
            // Looping methods (a backward branch) skip the speculative splice/inline machinery:
            // they compile fully boxed and deopt-free below (a partial loop iteration must never
            // re-execute, so a guarded inline body's deopt-on-miss is unsafe inside a loop).
            if (ir is not null && !ir.HasBackwardBranch && definingClass is not null && inlineRegistry is not null)
            {
                var effectiveInlineSelectorRegistry =
                    Environment.GetEnvironmentVariable("AOT_NOCROSSSPLICE") == "1"
                        ? null
                        : inlineSelectorRegistry;
                    // Splice detection traces each send's receiver/args to their producer via
                    // defIndex (the value's last def). On the register-reused IR a workhorse id
                    // (e.g. ao's `rs`, conflated into v10) points defIndex at a later redefinition,
                    // hiding that the receiver is a freshly-`new`ed object — so a consumer like
                    // `rs.vdot(dir)` is missed. SSA-renumbering first gives `rs` a unique id whose
                    // single def IS the producer, exposing the candidate. Detection is 1:1 over
                    // instructions, so the returned bytecode pcs still key the plan correctly.
                    var detectIr = ir;
                    if (SsaEnabled &&
                        TryReadMandatoryArgCount(irep, out var detectArgCount))
                    {
                        detectIr = RubyIRSsaRenumber.Run(ir, detectArgCount);
                    }
                    var candidatePcs = Analyzer.FindSpliceCandidatePcs(detectIr, effectiveInlineSelectorRegistry, accessorRegistry);
                    var splicePlan = candidatePcs is { Count: > 0 }
                        ? RubyIRBuilder.TryBuildSelfInlinePlan(
                            state,
                            irep,
                            definingClass,
                            inlineRegistry,
                            effectiveInlineSelectorRegistry,
                            candidatePcs)
                        : null;
                    if (splicePlan is { Count: > 0 })
                    {
                        var spliced = RubyIRBuilder.Build(irep, 0, splicePlan, out var splicedFailure);
                        if (spliced is not null)
                        {
                            ir = spliced;
                            failure = splicedFailure;
                        }
                    }
            }
        }
        catch
        {
            LastBail = "lower-throw";
            return false;
        }
        if (ir is null)
        {
            // Surface the lowerer's own reason (opcode it choked on / why) so coverage
            // analysis can tell e.g. "backward branch" (loops) from an unsupported op.
            LastBail = failure.OpCode is { } op
                ? "lower:" + op + (failure.Reason.Length > 0 ? "(" + failure.Reason + ")" : "")
                : "lower:" + (failure.Reason.Length > 0 ? failure.Reason : "null");
            return false;
        }

        if (!TryReadMandatoryArgCount(irep, out var argCount))
        {
            LastBail = "argspec";
            return false;
        }

        // Looping methods (a `while`/`until` back-edge) compile in a fully-boxed, deopt-free mode:
        // SSA renumbering, unboxing, scalar replacement and stack allocation are all OFF, and numeric
        // slow paths Send instead of deopting (ForceSend) — exactly the block-body contract, because
        // a loop body must never re-execute a partial iteration after a mid-loop deopt. SSA + the
        // cyclic-dataflow type inference needed to unbox loop-carried values is Phase 2.
        var looping = ir.HasBackwardBranch;

        // SSA-grade live-range splitting (ON by default; disable with AOT_NOSSA=1): split reused
        // merge-slot value-ids into join-precise ids so per-range type inference / unboxing can
        // fire. Renumbering only; emission + ComputeUnboxing are unchanged. Falls back internally
        // to the original ir on any anomaly. Float-heavy code wins (ao ~-16%/-35% alloc) because
        // the split float ranges unbox; integer code is neutral now that float speculation is gated
        // on class-level float evidence (so it never mis-speculates int ivars). See jetpack-ssa-plan.md.
        // SSA now handles back-edges (loop-aware reaching-defs fixpoint), so it runs for looping
        // methods too: it splits the merge-slot temp workhorses into per-range ids (so an arith
        // intermediate isn't conflated with a boolean compare result), which is what lets the sound
        // loop lattice prove a clean Float/Fixnum type. Loop-carried values keep one id (their init
        // and back-edge defs both reach the header use -> unioned).
        if (SsaEnabled)
        {
            ir = RubyIRSsaRenumber.Run(ir, argCount);
        }

        var instructions = ir.Instructions;

        var targets = new HashSet<int>();
        foreach (var ins in instructions)
        {
            if (ins.OpCode is RubyIROpCode.Jump or RubyIROpCode.JumpIfTruthy
                or RubyIROpCode.JumpIfFalsy or RubyIROpCode.JumpIfNil
                or RubyIROpCode.GuardInlineClass)
            {
                targets.Add(ins.Aux);
            }
        }

        var sym = new SymbolCache(methodName);
        CurrentConstLit = Analyzer.BuildConstLit(ir); // fixnum/float literals -> guard-free constant operands
        // Scalar replacement / stack allocation deopt on a guard miss, which is unsafe inside a loop
        // (a partial iteration would re-execute). Off for looping methods.
        var sc = looping ? null : ScalarContext.TryBuild(state, ir, sym, methodName);
        // Stack objects in THIS method (caller side): VirtualNew sites we build on the stack and
        // pass by-ref to a specialized callee. Only when emission is enabled (opt-in for now).
        var structParams = CurrentStructParams; // set by the variant generator (Pass 3)
        CurrentStackObjects = !looping && StackObjEnabled && CurrentEscapeSummary is { } esc0
            ? Analyzer.FindStackEligible(state, ir, esc0)
            : null;
        // Defer to scalar replacement: a fully-local object ScalarContext already eliminates is strictly
        // better as a scalar (no struct). Stack allocation is only for objects that escape via a
        // non-retaining call (which ScalarContext rejects). Drop any overlap.
        if (CurrentStackObjects is { Count: > 0 } && sc is not null)
        {
            // Only scalar-replaced (fully-eliminated) objects are excluded. fastNew objects still
            // heap-allocate, so stack allocation SHOULD take them over (the VirtualNew case checks
            // CurrentStackObjects before fastNew).
            foreach (var k in new List<int>(CurrentStackObjects.Keys))
            {
                if (sc.IsScalar(k)) CurrentStackObjects.Remove(k);
            }
        }
        // Variant body: the struct parameter and its Move-copies are struct locals too, so reads
        // of `ray` aliased into another value-id (`v3 = ray`) stay struct (a struct copy) and
        // their `ray.org`-style accessor sends lower to field reads. Register the whole alias
        // closure (the param + copies) under the param layout in the same CurrentStackObjects map.
        if (structParams is not null)
        {
            CurrentStackObjects ??= new Dictionary<int, StackLayout>();
            foreach (var (pidx, play) in structParams)
            {
                foreach (var a in Analyzer.MoveClosure(ir, pidx))
                {
                    CurrentStackObjects[a] = play;
                }
            }
            // The param is registered AFTER FindStackEligible ran, so cascade its own nested-field
            // reads now (`param.inner.m()` -> struct-receiver on a stack copy).
            if (CurrentEscapeSummary is { } pesc)
            {
                var pdef = new int[ir.ValueCount];
                for (var i = 0; i < pdef.Length; i++) pdef[i] = -1;
                for (var i = 0; i < instructions.Length; i++) { var d = instructions[i].Dst; if ((uint)d < (uint)pdef.Length) pdef[d] = i; }
                Analyzer.PropagateNestedReads(state, ir, pdef, CurrentStackObjects, pesc);
            }
        }
        Emitter.RebuildStructCanonical(); // mutated-object aliases share one struct local
        if (Environment.GetEnvironmentVariable("AOT_ESCAPE_DEBUG") == "1" && CurrentStackObjects is { Count: > 0 } dbgso)
        {
            foreach (var (objId, lay) in dbgso)
            {
                Console.Error.WriteLine($"[stackobj] {methodName} v{objId} = new {state.NameOf(lay.ConstName)} -> {lay.StructType}");
            }
        }
        // Float speculation needs class context (to know which ivars are statically int); without
        // a defining class, pass null so ComputeUnboxing skips speculation entirely.
        var knownFixnumIvars = definingClass is null ? null : Analyzer.CollectKnownFixnumIvars(state, definingClass);
        // Float speculation only fires in classes that demonstrably use floats (per whole-program
        // inference) — keeps it off all-integer classes (e.g. optcarrot) where it would mis-speculate
        // int ivars and constant-deopt. No registry (unit tests) -> permit, preserving prior behavior.
        var classUsesFloat = CurrentReturnTypes is null || CurrentReturnTypes.ClassUsesFloat(definingClass);
        bool[] isLong, floatTaint, isDouble, provesDouble;
        List<int>? loopArgGuards = null;
        CurrentProvesFixnum = null;
        if (looping)
        {
            // Phase 2: sound MUST numeric typing over the cyclic loop IR. Storage stays boxed (loop-
            // carried values are defined by the back-edge Move, which never writes a raw l/d local),
            // but proven Float/Fixnum operands enable guard-free double arith (no per-op type
            // dispatch). isLong/isDouble stay false (no raw locals); the win is provesDouble/
            // provesFixnum + the mixed-numeric arith path. Speculation is OFF (deopt-in-loop unsafe);
            // numerically-used args are Fixnum-guarded at entry (pre-side-effect, deopt-safe).
            Analyzer.ComputeLoopUnboxing(state, ir, argCount, definingClass,
                out provesDouble, out var provesFixnum, out floatTaint, out var soundProvenLoop, out loopArgGuards);
            CurrentProvesFixnum = provesFixnum;
            CurrentSoundProven = soundProvenLoop;
            // Phase 3: promote purely-numeric loop values to raw double/long locals (FP/int
            // registers) instead of boxed-but-guard-free MRubyValue. Move/LoadValue become
            // representation-aware so the back-edge update and the pre-loop init write the raw local.
            Analyzer.ComputeLoopRawLocals(state, ir, argCount, provesDouble, provesFixnum, out isLong, out isDouble);
        }
        else
        {
            isLong = Analyzer.ComputeUnboxing(state, ir, argCount, sc, out floatTaint, out isDouble, out provesDouble, out var soundProven, accessorRegistry, knownFixnumIvars, classUsesFloat, definingClass);
            CurrentSoundProven = soundProven;
        }
        // Coalesce the many SSA temps into a minimal set of C# locals (source-size only; the JIT
        // register-allocates regardless). Looping methods are where SSA produces the most temps;
        // gated to them to keep non-looping output byte-identical. Scalar/stack-object emission has
        // its own value->local naming, so skip coalescing when those are active.
        // Array-literal scalar replacement: `[a,b,c]` that never escapes and is read/written only by
        // constant index becomes per-element locals (no RArray alloc). Independent of object
        // scalar/stack replacement (those are VirtualNew-only), so it composes with every path.
        // Computed BEFORE coalescing so its value-ids (which become element locals, never a v{}) are
        // excluded from the coalescing pool.
        CurrentScalarArrays = ScalarArrayEnabled ? Analyzer.FindScalarArrays(state, ir) : null;
        CurrentHashKeyTags = null;
        CurrentScalarHashes = ScalarArrayEnabled ? Analyzer.FindScalarHashes(ir) : null;
        CurrentLocalSlot = looping && sc is null && CurrentStackObjects is null && structParams is null
            ? Analyzer.CoalesceLocals(ir, argCount, isLong, isDouble, CurrentScalarArrays, CurrentScalarHashes)
            : null;
        var ic = new InlineContext(state, definingClass, inlineRegistry, accessorRegistry, CurrentConstReturns, methodName, sym, ir);
        var source = Emitter.EmitMethod(state, ir, irep, methodName, inlineRegistry, accessorRegistry,
            sym, ic, sc, structParams, isLong, floatTaint, isDouble, provesDouble,
            targets, loopArgGuards, looping, argCount, out var isLeaf);
        if (source is null) return false; // an op bailed; LastBail is set
        if (definingClass is not null) MeasureInlineSurface(state, ir, definingClass);
        method = new CompiledMethod(methodName, source, argCount, instructions.Length, isLeaf, Emitter.BlockEmit.AuxMethods);
        return true;
    }

    // Diagnostic only (no effect on emitted code): for each self-send in this compiled
    // method, count whether it resolves to an RProc method on the defining class and
    // whether that callee is a small splice candidate. Drives the inline build-out.
    static void MeasureInlineSurface(MRubyState state, RubyIRMethod exe, RClass definingClass)
    {
        var instructions = exe.Instructions;
        for (var i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode != RubyIROpCode.SendSelf)
            {
                continue;
            }

            var methodSym = exe.GetCallSiteSymbol(instructions[i].Aux);
            if (!state.TryFindMethod(definingClass, methodSym, out var method, out _) ||
                method.Proc is not { } proc)
            {
                continue;
            }

            if (Analyzer.IsInlineCandidate(proc.Irep))
            {
            }
        }
    }

    // Decide which value-ids can live as unboxed `long`. A value is Long iff it is (a) not
    // self/an argument, (b) defined only by fixnum-arith ops (so it's provably fixnum given
    // the deopt guards), (c) used ONLY as a fixnum operand of arith/comparison ops, and (d)
    // NOT float-tainted. (c) guarantees a Long value never reaches a boxed context, so non-
    // arith op emission is untouched and never needs to box it. (d) keeps known-float arith
    // chains boxed so the dual fixnum/float emission handles them at runtime instead of the
    // long path speculating fixnum and deopting on the first float. Conservative throughout.
    //
    // Float taint is a forward dataflow: seeds are float literals and float-returning sends
    // (to_f / Math.sqrt etc.); it flows through Move and arith ops to their dst. Integer-only
    // code (e.g. optcarrot) has no seeds, so nothing is tainted and long-unboxing is unchanged.
    // Collect a coalesced local's representative id (set per method while emitting; null = identity).
    [ThreadStatic] internal static int[]? CurrentLocalSlot;
    internal static int Slot(int id) =>
        CurrentLocalSlot is { } s && (uint)id < (uint)s.Length ? s[id] : id;

    // --- array-literal scalar replacement (alloc elimination) ---
    // A `[a, b, c]` (NewArray) value-id whose ONLY uses are constant-index `[]`/`[]=` in range and
    // which never escapes (no Send arg/receiver, return, ivar/array store, Move, non-const index) is
    // replaced by one boxed MRubyValue local PER ELEMENT (`av{id}_{k}`) — the RArray + its backing
    // MRubyValue[] allocation are eliminated. Maps array value-id -> element count.
    // alias value-id -> (canonical array id, element count). Every Move-alias of a scalar-replaced
    // array maps to the same canonical id, whose element locals are `av{canon}_{k}`.
    [ThreadStatic] internal static Dictionary<int, (int Canon, int Size)>? CurrentScalarArrays;
    static bool ScalarArrayEnabled => Environment.GetEnvironmentVariable("AOT_NOSCALARARRAY") != "1";
    internal static string ArrElem(int arrId, int index) => "av" + arrId + "_" + index;
    internal static bool TryScalarArray(int id, out int canon, out int size)
    {
        if (CurrentScalarArrays is { } m && m.TryGetValue(id, out var info)) { canon = info.Canon; size = info.Size; return true; }
        canon = 0; size = 0; return false;
    }

    // --- hash-literal scalar replacement (constant-key {k=>v}) ---
    // A `{k0 => v0, ...}` whose keys are all CONSTANT (symbol/fixnum), accessed only by constant key,
    // never escaping/iterated, is replaced by one boxed local per distinct key (`hv{canon}_{tag}`) —
    // no RHash alloc, no [] / []= dispatch. A lookup of a key never present reads nil (Hash default).
    [ThreadStatic] internal static Dictionary<int, (int Canon, HashSet<string> Keys)>? CurrentScalarHashes;
    [ThreadStatic] internal static Dictionary<int, string>? CurrentHashKeyTags; // const-key value-id -> tag
    internal static string HashElem(int canon, string tag) => "hv" + canon + "_" + tag;
    internal static bool TryScalarHash(int id, out int canon, out HashSet<string> keys)
    {
        if (CurrentScalarHashes is { } m && m.TryGetValue(id, out var info)) { canon = info.Canon; keys = info.Keys; return true; }
        canon = 0; keys = null!; return false;
    }
    // Emission-time tag of a constant key value-id (computed during detection).
    internal static string? KeyTag(int keyId) =>
        CurrentHashKeyTags is { } m && m.TryGetValue(keyId, out var t) ? t : null;

    // Whole-program inferred return types (RubyIRReturnTypes), set per compile run. Null when no
    // registry was supplied (e.g. unit tests) -> only the builtin float methods are recognized.
    [ThreadStatic] internal static RubyIRReturnTypes.Registry? CurrentReturnTypes;
    // Selector -> the immediate constant its 0-arg method returns (set per Compile run). Fingerprint-
    // guarded at each call site, so it is sound even if stale across states.
    [ThreadStatic] internal static IReadOnlyDictionary<Symbol, ConstReturnTarget>? CurrentConstReturns;

    // Whole-program retention summary for AOT stack allocation (set per Compile run). Null when
    // disabled (AOT_NOSTACKOBJ=1) or unavailable -> no stack-object classification.
    [ThreadStatic] internal static RubyIREscapeSummary.Summary? CurrentEscapeSummary;

    // Stack-object EMISSION (struct + specialized callee). Stage 1 verified end-to-end
    // (ao -18% alloc / ~10% faster, checksum-identical on/off, 173/173 both ways), so it is ON by
    // default now — gated by AOT_NOSTACKOBJ like CurrentEscapeSummary and the other AOT passes.
    internal static bool StackObjEnabled => Environment.GetEnvironmentVariable("AOT_NOSTACKOBJ") != "1";

    // Caller side: stack-allocated objects in the method being compiled (objId -> struct layout).
    [ThreadStatic] internal static Dictionary<int, StackLayout>? CurrentStackObjects;
    // Variant side: when generating a specialized callee, the parameters passed as stack structs
    // (paramIndex -> layout; in/ref decided by layout.Mutated). A send may pass several.
    [ThreadStatic] static Dictionary<int, StackLayout>? CurrentStructParams;
    // Whole-program collectors (set per Compile run): struct types to declare + variants to emit.
    [ThreadStatic] internal static Dictionary<ulong, StackLayout>? NeededStructs;          // ClassFp -> layout
    [ThreadStatic] internal static Dictionary<string, (RClass Callee, Symbol Selector, Dictionary<int, StackLayout> StructParams)>? NeededVariants; // variant name -> spec

    // (Registry.ReturnsInteger is the symmetric "always returns Integer" proof — the foundation
    // for unboxing a Send result to a long with no speculation/deopt — not wired into isLong yet.)

    // value-id -> its LoadValue fixnum/float constant (set per emitted exe). A constant operand
    // needs no `IsFixnum`/`IsFloat` guard and reads as a C# literal, not `.FixnumValue`/`.FloatValue`
    // — so a `@x & 0xFFFFF` lowers to `v1.FixnumValue & 1048575L` with only v1 guarded. Sound: the
    // emitted IR is single-def (SSA), so the id's only definition is that LoadValue.
    [ThreadStatic] internal static Dictionary<int, MRubyValue>? CurrentConstLit;
    internal static bool ConstFix(int id, out long v)
    {
        if (CurrentConstLit is { } m && m.TryGetValue(id, out var lit) && lit.IsFixnum) { v = lit.FixnumValue; return true; }
        v = 0; return false;
    }
    internal static bool ConstFloat(int id, out double v)
    {
        if (CurrentConstLit is { } m && m.TryGetValue(id, out var lit) && lit.IsFloat) { v = lit.FloatValue; return true; }
        v = 0; return false;
    }

    // Read value-id `id` as a long in a fixnum context: the constant literal if known, else l{id}
    // if it lives unboxed, else the boxed value's FixnumValue (guarded separately by FixGuard).
    internal static string FixRead(bool[] isLong, int id) =>
        ConstFix(id, out var cv) ? cv + "L" : isLong[id] ? "l" + Slot(id) : "v" + Slot(id) + ".FixnumValue";

    // Guard asserting `id` is a fixnum, or null when it needs none (a known constant, or unboxed long).
    internal static string? FixGuard(bool[] isLong, int id) =>
        ConstFix(id, out _) || isLong[id] ? null : "v" + Slot(id) + ".IsFixnum";


    // Per-compile set of dsts whose provesDouble is a sound proof (no guard), set in TryCompileMethod
    // alongside CurrentReturnTypes. Null in contexts that don't compute it (preserves the guard).
    [ThreadStatic] internal static bool[]? CurrentSoundProven;

    // Phase 2 (looping methods): per-id "always Fixnum" proof from the sound loop type lattice.
    // Lets mixed Float/Fixnum arith read a boxed-but-proven-fixnum operand as `(double)v.FixnumValue`
    // with no runtime type guard. Null for non-looping methods (no mixed-numeric fast path).
    [ThreadStatic] internal static bool[]? CurrentProvesFixnum;

    internal static bool TryReadMandatoryArgCount(Irep irep, out int argCount)
    {
        argCount = 0;
        var seq = irep.Sequence;
        if (seq.Length < 4 || (OpCode)seq[0] != OpCode.Enter)
        {
            return false;
        }
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

    // A stack-allocated object's value-id refers to its struct local `so<id>`; everything else is
    // the boxed/unboxed local `v<id>`. (An accidental boxed use of a stack obj -> `so<id>` where an
    // MRubyValue is expected -> a C# compile error, i.e. a loud failure, not silent miscompile.)
    internal static string Val(int valueId) =>
        Emitter.StructCanonical is { } sc && sc.TryGetValue(valueId, out var c) ? "so" + c :
        CurrentStackObjects is { } so && so.ContainsKey(valueId) ? "so" + valueId : "v" + Slot(valueId);

}
