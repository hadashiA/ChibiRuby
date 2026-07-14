using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ChibiRuby.StdLib;

namespace ChibiRuby;

public sealed class MRubyValueEqualityComparer(MRubyState state) : IEqualityComparer<MRubyValue>
{
    public bool Equals(MRubyValue x, MRubyValue y)
    {
        return state.ValueEquals(x, y);
    }

    public int GetHashCode(MRubyValue value)
    {
        return value.GetHashCode();
    }
}

/// <summary>
/// Key semantics for Hash, mirroring mruby's ht_hash_value / ht_hash_equal:
/// immediates and strings are handled natively (by value / by content), while other
/// objects defer to the Ruby-visible `hash` / `eql?` methods so user overrides are
/// honored. The default `Kernel#hash` (identity) is detected via the method cache and
/// short-circuited to avoid a full Send per operation on identity-keyed hashes.
///
/// The same class also implements Hash#compare_by_identity (via <see cref="ByIdentity"/>)
/// rather than a separate comparer type, so Dictionary probe sites stay monomorphic for
/// the JIT's guarded devirtualization.
/// </summary>
public sealed class MRubyValueHashKeyEqualityComparer(MRubyState state, bool byIdentity = false)
    : IEqualityComparer<MRubyValue>
{
    /// <summary>
    /// Identity key semantics (Hash#compare_by_identity): objects compare by reference,
    /// immediates by value; user-defined hash/eql? are ignored. Note strings are NOT
    /// special-cased — two content-equal string instances are distinct keys, as in CRuby.
    /// </summary>
    public bool ByIdentity => byIdentity;

    // The hot entry points stay tiny so the JIT's guarded devirtualization can inline
    // them into Dictionary probes; everything type-dispatched lives in NoInlining tails.

    public bool Equals(MRubyValue a, MRubyValue b)
    {
        // Identity / immediate value equality. Remaining immediates
        // (Integer/Float/Symbol/nil/true/false) are fully decided here.
        if (a == b) return true;
        return !byIdentity && a.Object is not null && EqualsSlow(a, b);
    }

    public int GetHashCode(MRubyValue value)
    {
        // Immediates hash by value.
        if (value.Object is null) return value.GetHashCode();
        return byIdentity
            ? RuntimeHelpers.GetHashCode(value.Object)
            : GetHashCodeSlow(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    int GetHashCodeSlow(MRubyValue value)
    {
        // Strings hash by content (RString overrides GetHashCode).
        if (value.Object is RString)
        {
            return value.GetHashCode();
        }

        // Honor a user-defined `hash` override. When the resolved method is the default
        // Kernel#hash (identity), skip the dispatch: bucketing by the CLR identity hash
        // is consistent with the identity-based `eql?` default.
        if (state.TryFindMethod(state.ClassOf(value), Names.Hash, out var method, out _) &&
            method == KernelMembers.HashFunc)
        {
            return value.GetHashCode();
        }

        var hashValue = state.Send(value, Names.Hash);
        return hashValue.IsInteger
            ? hashValue.IntegerValue.GetHashCode()
            : hashValue.GetHashCode();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    bool EqualsSlow(MRubyValue a, MRubyValue b)
    {
        // Strings compare by content regardless of class, as in mruby's ht_hash_equal.
        if (a.Object is RString sa)
        {
            return b.Object is RString sb && sa.AsSpan().SequenceEqual(sb.AsSpan());
        }
        return state.Send(a, Names.QEql, b).Truthy;
    }

}
