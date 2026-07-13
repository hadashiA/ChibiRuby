using System.Runtime.CompilerServices;
using ChibiRuby.Internals;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif

namespace ChibiRuby;

/// <summary>
/// Base type for AOT-generated method-body classes. It lives in the ChibiRuby assembly, so it can
/// touch internal VM state (the frame stack, the ivar table); it re-exposes exactly that state to
/// generated subclasses through <c>protected static</c> helpers.
/// </summary>
/// <remarks>
/// The generated code is emitted into a separate assembly (a friend source-gen assembly in
/// production, a runtime-Roslyn assembly in the PoC). Rather than make the VM internals public or
/// grant a blanket <c>InternalsVisibleTo</c>, generated classes derive from this base and call the
/// helpers below. <c>protected static</c> members are reachable from a derived class's static
/// methods even across assembly boundaries (family access crosses assemblies, and the
/// derived-instance restriction does not apply to static members), so the VM internals stay
/// internal. The helpers are aggressively inlined, so this indirection costs nothing at runtime.
/// </remarks>
public abstract class AotGeneratedMethods
{
    // The helpers are suffixed "Unsafe": they read raw VM internals (the frame stack via an unchecked
    // ref offset, the in-flight call info) with no bounds/state validation, and are only sound when
    // called from a generated body bound to the matching irep. The suffix flags that they are not a
    // safe public API even though family access makes them reachable across the assembly boundary.

    /// <summary>The frame register at <c>sp + index</c> — used by wrappers to marshal self/args off the stack.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue RegisterUnsafe(MRubyState state, int sp, int index)
    {
        return Unsafe.Add(ref GetArrayDataReference(state.Context.Stack), sp + index);
    }

    /// <summary>Argument count of the in-flight call — for the wrapper's arity guard.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static int ArgumentCountUnsafe(MRubyState state)
    {
        return state.Context.CurrentCallInfo.ArgumentCount;
    }

    // --- Operations targeted by generated bodies ---------------------------------------------
    // Moved here from MRubyState's public surface: they are only sound when called from a
    // generated body (their guards are validated against per-call-site inline-cache fields the
    // codegen emits), so they don't belong on the public VM API. Each takes the state explicitly
    // and reaches the VM internals through it. All carry the "Unsafe" suffix for the same reason
    // as the helpers above — reachable across the assembly boundary, but not a safe public API.

    // Monomorphic inline-cache guard for a devirtualized self-send. The codegen inlined (at build
    // time) the callee whose irep fingerprint is `expectedCalleeFingerprint`, resolved against the
    // method's defining class. This decides at runtime whether that inlined body is still valid for
    // `receiver`: true => run the inline form; false => fall back to a normal Send (polymorphic
    // receiver / overriding subclass / redefined method). icClass+icVersion are per-call-site static
    // fields. Fast path: receiver's class and the method-cache version both match the cached pair.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool InlineGuardUnsafe(
        MRubyState state,
        MRubyValue receiver,
        Symbol methodId,
        ulong expectedCalleeFingerprint,
        ref RClass? icClass,
        ref int icVersion)
    {
        var cls = receiver.Object is { } o ? o.Class : state.ClassOf(receiver);
        if (ReferenceEquals(cls, icClass) && icVersion == state.MethodCacheVersion)
        {
            return true;
        }
        return InlineGuardResolve(state, cls, methodId, expectedCalleeFingerprint, ref icClass, ref icVersion);
    }

    // Slow path of InlineGuardUnsafe, split out (NoInlining) so the steady-state fast path stays
    // small enough for the JIT to inline: re-resolve the method on the receiver's actual class and
    // confirm it's the SAME body we inlined (fingerprint identity — so an override on a subclass can
    // never run the base inline); on match, refresh the cache.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool InlineGuardResolve(
        MRubyState state,
        RClass cls,
        Symbol methodId,
        ulong expectedCalleeFingerprint,
        ref RClass? icClass,
        ref int icVersion)
    {
        if (state.TryFindMethod(cls, methodId, out var method, out _) &&
            method.Proc is { } proc &&
            state.ComputeIrepFingerprint(proc.Irep) == expectedCalleeFingerprint)
        {
            icClass = cls;
            icVersion = state.MethodCacheVersion;
            return true;
        }
        icClass = null;
        return false;
    }

    // Guard for a scalar-replaced allocation. When the codegen elides a `Const.new(...)` and inlines
    // its initialize / accessors, the emitted code is valid only while (a) `constName` still resolves
    // to the class it was compiled against and (b) the inlined method `methodId` on that class still
    // has the body fingerprint it was compiled from. Match => the call site is safe; miss => deopt.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool ClassMethodGuardUnsafe(
        MRubyState state,
        Symbol constName,
        Symbol methodId,
        ulong expectedFingerprint,
        ref RClass? icClass,
        ref int icVer)
    {
        if (icClass is not null && icVer == state.MethodCacheVersion)
        {
            return true;
        }
        if (state.TryGetConst(constName, out var value) &&
            value.Object is RClass klass &&
            state.TryFindMethod(klass, methodId, out var method, out _) &&
            method.Proc is { } proc &&
            state.ComputeIrepFingerprint(proc.Irep) == expectedFingerprint)
        {
            icClass = klass;
            icVer = state.MethodCacheVersion;
            return true;
        }
        icClass = null;
        return false;
    }

    // Resolve a candidate receiver class for a class-switch dispatch (stack-arg Send). Returns the
    // class iff `constName` still resolves to a class whose `sel` method has body fingerprint `fp`
    // (the exact body the struct variant was compiled from); null otherwise. The generated dispatch
    // pointer-compares the receiver's class against the result — a hit runs the variant, a miss
    // (incl. null) reifies + Sends. Fingerprint identity makes this sound (subclass override => null).
    protected static RClass? ResolveGuardClassUnsafe(MRubyState state, Symbol constName, Symbol sel, ulong fp)
    {
        if (state.TryGetConst(constName, out var value) &&
            value.Object is RClass klass &&
            state.TryFindMethod(klass, sel, out var method, out _) &&
            method.Proc is { } proc &&
            state.ComputeIrepFingerprint(proc.Irep) == fp)
        {
            return klass;
        }
        return null;
    }

    // Guard for inlined object construction (fast-new). Valid only while (a) `:new` on `classValue`
    // is still the default C# builtin (a Ruby `def self.new` override has a Proc and must run) and
    // (b) `:initialize` is the simple setter body it inlined (fingerprint unchanged). Miss => deopt.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool InlineNewGuardUnsafe(
        MRubyState state,
        MRubyValue classValue,
        Symbol newId,
        Symbol initId,
        ulong initFingerprint,
        ref RClass? icClass,
        ref int icVer)
    {
        if (classValue.Object is not RClass cls)
        {
            icClass = null;
            return false;
        }
        if (ReferenceEquals(cls, icClass) && icVer == state.MethodCacheVersion)
        {
            return true;
        }
        if (state.TryFindMethod(state.ClassOf(classValue), newId, out var newMethod, out _) &&
            newMethod.Proc is null &&
            state.TryFindMethod(cls, initId, out var init, out _) &&
            init.Proc is { } proc &&
            state.ComputeIrepFingerprint(proc.Irep) == initFingerprint)
        {
            icClass = cls;
            icVer = state.MethodCacheVersion;
            return true;
        }
        icClass = null;
        return false;
    }

    // Indexed get/set — mirror the interpreter's OP_GetIdx / OP_GetIdx0 / OP_SetIdx fast paths: a
    // plain Array (exact ArrayClass) with a fixnum index goes straight through RArray (its indexer
    // handles negative / out-of-range -> nil); anything else falls back to the same :[] / :[]= send
    // the interpreter would do, so semantics match.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue GetIndexUnsafe(MRubyState state, MRubyValue receiver, MRubyValue key)
    {
        if (receiver.Object is RArray array && key.IsFixnum && array.Class == state.ArrayClass)
        {
            return array[(int)key.FixnumValue];
        }
        return state.Send(receiver, Names.OpAref, key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue GetIndexZeroUnsafe(MRubyState state, MRubyValue receiver)
    {
        if (receiver.Object is RArray array && array.Class == state.ArrayClass)
        {
            return array.Length > 0 ? array[0] : default;
        }
        return state.Send(receiver, Names.OpAref, new MRubyValue(0L));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue SetIndexUnsafe(MRubyState state, MRubyValue receiver, MRubyValue key, MRubyValue value)
    {
        if (receiver.Object is RArray array && key.IsFixnum && array.Class == state.ArrayClass
            && !array.HasFlag(MRubyObjectFlags.Frozen))
        {
            array.Set((int)key.FixnumValue, value);
            return value;
        }
        return state.Send(receiver, Names.OpAset, key, value);
    }

    // Fast path for one-argument C# methods registered with a pure-unary delegate (Math.sqrt/cos/sin
    // in the benchmark harness). Still resolves the current method, so Ruby redefinition falls back
    // to normal Send semantics.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue PureUnarySendUnsafe(MRubyState state, MRubyValue receiver, Symbol methodId, MRubyValue argument)
    {
        if (state.TryFindMethod(state.ClassOf(receiver), methodId, out var method, out _) &&
            method != MRubyMethod.Undef &&
            method.TryInvokePureUnaryNumeric(state, receiver, argument, out var result))
        {
            return result;
        }
        return state.Send(receiver, methodId, argument);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue PureUnarySendUnsafe(
        MRubyState state,
        MRubyValue receiver,
        Symbol methodId,
        MRubyValue argument,
        ref RClass? icClass,
        ref int icVersion,
        ref MRubyMethod icMethod)
    {
        var cls = state.ClassOf(receiver);
        if (ReferenceEquals(cls, icClass) &&
            icVersion == state.MethodCacheVersion &&
            icMethod.TryInvokePureUnaryNumeric(state, receiver, argument, out var cachedResult))
        {
            return cachedResult;
        }
        if (state.TryFindMethod(cls, methodId, out var method, out _) &&
            method != MRubyMethod.Undef &&
            method.TryInvokePureUnaryNumeric(state, receiver, argument, out var result))
        {
            icClass = cls;
            icVersion = state.MethodCacheVersion;
            icMethod = method;
            return result;
        }
        icClass = null;
        return state.Send(receiver, methodId, argument);
    }

    // Constant lookup — identical to OP_GetConst (fast path on the current frame's scope class, then
    // ResolveConstantSlow which walks the proc's lexical Upper chain). Shares ResolveConstantSlow with
    // the interpreter (kept on MRubyState) so the resolution can't drift.
    protected static MRubyValue GetConstantUnsafe(MRubyState state, Symbol id)
    {
        ref var callInfo = ref state.Context.CurrentCallInfo;
        var c = callInfo.Proc?.Scope?.TargetClass ?? state.ObjectClass;
        if (c.ClassInstanceVariables.TryGet(id, out var value))
        {
            return value;
        }
        return state.ResolveConstantSlow(ref callInfo, id, c);
    }

    // Per-call-site cached constant read. Constant resolution at a given lexical site is stable until
    // some constant is (re)assigned, which bumps ConstCacheVersion — so a version match returns the
    // cached value and skips the scope-chain walk. The cache fields are static (shared across states),
    // so the cached state is part of the key: a different MRubyState (different constant tables, even
    // at the same version number) re-resolves. `cachedState` is null on first use.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static MRubyValue GetConstantCachedUnsafe(MRubyState state, Symbol id, ref MRubyValue cached, ref int cachedVer, ref MRubyState? cachedState)
    {
        if (cachedVer == state.ConstCacheVersion && ReferenceEquals(cachedState, state))
        {
            return cached;
        }
        cached = GetConstantUnsafe(state, id);
        cachedVer = state.ConstCacheVersion;
        cachedState = state;
        return cached;
    }
}
