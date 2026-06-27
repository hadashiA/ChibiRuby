using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ChibiRuby.Internals;

namespace ChibiRuby;

// AOT-compiled method bodies are bound to ireps by a content fingerprint: a
// build-time codegen step computes each compilable irep's fingerprint and registers
// a CompiledRubyMethodBody under it; at load time the parser computes the same
// fingerprint for each parsed irep and attaches the matching body. Same bytecode ->
// same fingerprint, so a compiled body binds to exactly the irep it was generated
// from, with no class/method names involved. If the Ruby changed since the C# was
// generated, the fingerprint differs -> nothing binds -> Irep.CompiledBody stays null.
public partial class MRubyState
{
    // AOT-compiler driver surface: visit every RProc-backed method reachable from the
    // class tree (Object's constants, walked recursively), together with its DEFINING
    // class. A build step uses this to compile each (definingClass, methodId, irep) and
    // resolve self-sends (self's class == the defining class) for devirtualize+inline.
    public void EnumerateAotMethods(Action<RClass, Symbol, Irep> visit)
    {
        var seen = new HashSet<RClass>();
        void Walk(RClass c)
        {
            if (!seen.Add(c))
            {
                return;
            }
            foreach (var (methodId, method) in c.MethodTable)
            {
                if (method.Proc is { } proc)
                {
                    visit(c, methodId, proc.Irep);
                }
            }
            foreach (var (_, value) in c.InstanceVariables)
            {
                if (value.Object is RClass { VType: MRubyVType.Class or MRubyVType.Module } nested)
                {
                    Walk(nested);
                }
            }
        }
        Walk(ObjectClass);
    }

    // Visit every constant reachable from the class tree (constants live in a class's instance
    // variables, same as the nested-class walk above). The AOT codegen uses this to find
    // Float-valued constants so an arith op mixing one with an integer is recognized as a
    // genuine fixnum/float mix at build time.
    public void EnumerateConstants(Action<Symbol, MRubyValue> visit)
    {
        var seen = new HashSet<RClass>();
        void Walk(RClass c)
        {
            if (!seen.Add(c))
            {
                return;
            }
            foreach (var (name, value) in c.InstanceVariables)
            {
                visit(name, value);
                if (value.Object is RClass { VType: MRubyVType.Class or MRubyVType.Module } nested)
                {
                    Walk(nested);
                }
            }
        }
        Walk(ObjectClass);
    }

    // The operations that AOT-generated bodies invoke (guards, indexed get/set, pure-unary send,
    // constant lookup, class-switch resolution) used to live here as public methods on MRubyState.
    // They are only sound when driven by generated code (their guards are validated against the
    // per-call-site inline-cache fields the codegen emits), so they were never a safe public API —
    // they now live on ChibiRuby.AotGeneratedMethods (the base class generated classes derive from)
    // as protected static `*Unsafe` helpers. ResolveConstantSlow stays here: it is shared with the
    // interpreter, and GetConstantUnsafe calls back into it.

    // The OP_GetConst slow path, shared by the interpreter and compiled bodies: unwrap
    // a singleton class, then walk the proc's lexical Upper chain, finally fall back to
    // GetConst (ancestors + Object + const_missing).
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal MRubyValue ResolveConstantSlow(ref MRubyCallInfo callInfo, Symbol id, RClass c)
    {
        var x = c;
        MRubyValue value;
        while (x is { VType: MRubyVType.SClass })
        {
            if (!x.ClassInstanceVariables.TryGet(id, out value))
            {
                x = null;
                break;
            }
            x = c.Class;
        }
        if (x is { VType: MRubyVType.Class or MRubyVType.Module })
        {
            c = x;
        }
        var proc = callInfo.Proc;
        while (proc != null)
        {
            if (proc != callInfo.Proc)
            {
                x = proc.Scope?.TargetClass ?? ObjectClass;
                if (x.ClassInstanceVariables.TryGet(id, out value))
                {
                    return value;
                }
            }
            if (proc.OptimizedUpperEnvironment is { } optimizedEnv)
            {
                x = optimizedEnv.TargetClass;
                if (x.ClassInstanceVariables.TryGet(id, out value))
                {
                    return value;
                }
            }
            proc = proc.Upper ?? proc.OptimizedUpperProc;
        }
        return GetConst(id, c);
    }

    Dictionary<ulong, CompiledRubyMethodBody>? compiledMethods;

    // Register a compiled body under an irep fingerprint (see ComputeIrepFingerprint).
    // Called by generated code / the codegen registration trigger.
    public void RegisterCompiledMethod(ulong fingerprint, CompiledRubyMethodBody body)
    {
        (compiledMethods ??= new Dictionary<ulong, CompiledRubyMethodBody>())[fingerprint] = body;
    }

    // Walk an irep tree and attach any registered compiled bodies by fingerprint.
    // Called automatically by ParseBytecode/LoadBytecode; public so a host that
    // registered bodies AFTER an irep was already parsed can rebind it. No-op (and
    // zero cost) when nothing is registered — the common dev / no-AOT case.
    public void BindCompiledMethods(Irep irep)
    {
        if (compiledMethods is not { Count: > 0 } registry)
        {
            return;
        }

        BindWalk(irep, registry);
    }

    void BindWalk(Irep irep, Dictionary<ulong, CompiledRubyMethodBody> registry)
    {
        if (registry.TryGetValue(ComputeIrepFingerprint(irep), out var body))
        {
            irep.CompiledBody = body;
        }

        foreach (var child in irep.Children)
        {
            BindWalk(child, registry);
        }
    }

    const ulong FnvOffset = 14695981039346656037UL;
    const ulong FnvPrime = 1099511628211UL;

    // Deterministic content hash of an irep: register count + bytecode + symbol NAMES
    // (not state-local ids) + pool literals + child fingerprints. Must match the
    // algorithm the build-time codegen uses so build-time and load-time agree. Public
    // so the codegen/registration layer can key bodies by it.
    public ulong ComputeIrepFingerprint(Irep irep)
    {
        if (irep.CachedFingerprint is { } cached)
        {
            return cached;
        }

        var h = FnvOffset;
        MixU32(ref h, irep.RegisterVariableCount);

        var seq = irep.Sequence;
        MixU32(ref h, (uint)seq.Length);
        foreach (var b in seq)
        {
            MixByte(ref h, b);
        }

        MixU32(ref h, (uint)irep.Symbols.Length);
        foreach (var sym in irep.Symbols)
        {
            var name = symbolTable.NameOf(sym);
            MixU32(ref h, (uint)name.Length);
            foreach (var b in name)
            {
                MixByte(ref h, b);
            }
        }

        MixU32(ref h, (uint)irep.PoolValues.Length);
        foreach (var pv in irep.PoolValues)
        {
            MixPool(ref h, pv);
        }

        MixU32(ref h, (uint)irep.Children.Length);
        foreach (var child in irep.Children)
        {
            MixU64(ref h, ComputeIrepFingerprint(child));
        }

        irep.CachedFingerprint = h;
        return h;
    }

    void MixPool(ref ulong h, MRubyValue v)
    {
        MixByte(ref h, (byte)v.VType);
        if (v.IsFixnum)
        {
            MixU64(ref h, (ulong)v.FixnumValue);
        }
        else if (v.IsFloat)
        {
            MixU64(ref h, (ulong)BitConverter.DoubleToInt64Bits(v.FloatValue));
        }
        else if (v.VType == MRubyVType.String)
        {
            var s = v.As<RString>().AsSpan();
            MixU32(ref h, (uint)s.Length);
            foreach (var b in s)
            {
                MixByte(ref h, b);
            }
        }
        else if (v.VType == MRubyVType.Symbol)
        {
            var name = symbolTable.NameOf(v.SymbolValue);
            foreach (var b in name)
            {
                MixByte(ref h, b);
            }
        }
    }

    static void MixByte(ref ulong h, byte b)
    {
        h ^= b;
        h *= FnvPrime;
    }

    static void MixU32(ref ulong h, uint v)
    {
        MixByte(ref h, (byte)v);
        MixByte(ref h, (byte)(v >> 8));
        MixByte(ref h, (byte)(v >> 16));
        MixByte(ref h, (byte)(v >> 24));
    }

    static void MixU64(ref ulong h, ulong v)
    {
        for (var i = 0; i < 8; i++)
        {
            MixByte(ref h, (byte)v);
            v >>= 8;
        }
    }
}
