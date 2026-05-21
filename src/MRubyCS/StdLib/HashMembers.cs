using System.Collections.Generic;

namespace MRubyCS.StdLib;

[RubyClass("Hash", TypeParameters = "K, V")]
static class HashMembers
{
    [RubyDef("(?V) ?{ (Hash[K, V], K) -> V } -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        var hash = self.As<RHash>();
        state.EnsureArgumentCount(0, 1);
        var block = state.GetBlockArgument();
        if (state.TryGetArgumentAt(0, out var defaultValue))
        {
            if (block != null)
            {
                state.Raise(Names.ArgumentError, "invalid block"u8);
            }

            hash.DefaultValue = defaultValue;
        }
        else if (block != null)
        {
            hash.DefaultProc = block;
        }
        return self;
    }

    [RubyDef("(Hash[K, V]) -> self")]
    public static MRubyValue InitializeCopy(MRubyState state, MRubyValue self)
    {
        var hash = self.As<RHash>();
        state.EnsureNotFrozen(hash);

        var other = state.GetArgumentAsHashAt(0);

        if (hash != other)
        {
            other.ReplaceTo(hash);
        }

        return self;
    }

    [RubyDef("() -> String")]

    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var hash = self.As<RHash>();
        var result = state.NewString("{"u8);

        // Currently, the only clue for checking whether a method is being called recursively is the method ID.
        // To prepare for cases where this method is called by an alias such as to_s, unify the current method ID to inspect.
        state.ModifyCurrentMethodId(Names.Inspect);

        if (state.IsRecursiveCalling(Names.Inspect, self))
        {
            result.Concat("...}"u8);
        }
        else
        {
            var first = true;
            foreach (var (key, value) in hash)
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }

                first = false;

                var keyString = state.Inspect(key);
                if (key.IsSymbol)
                {
                    result.Concat(state.NameOf(key.SymbolValue));
                    result.Concat(": "u8);
                }
                else
                {
                    result.Concat(keyString);
                    result.Concat(" => "u8);
                }
                var valueString = state.Inspect(value);
                result.Concat(valueString);
            }

            result.Concat("}"u8);
        }

        return result;
    }

    [RubyDef("(K) -> V?")]
    public static MRubyValue OpAref(MRubyState state, MRubyValue self)
    {
        var hash = self.As<RHash>();
        var key = state.GetArgumentAt(0);
        if (hash.TryGetValue(key, out var value))
        {
            return value;
        }

        return state.Send(self, Names.Default, key);
    }

    [RubyDef("(K, V) -> V")]
    public static MRubyValue OpAset(MRubyState state, MRubyValue self)
    {
        var hash = self.As<RHash>();
        state.EnsureNotFrozen(hash);

        var key = state.GetArgumentAt(0);
        if (key.Object is RString { IsFrozen: false })
        {
            key = state.DupObject(key);
            key.Object?.MarkAsFrozen();
        }

        var value = state.GetArgumentAt(1);
        hash[key] = value;
        return value;
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        return h.Length;
    }

    [RubyDef("() -> Array[K]")]
    public static MRubyValue Keys(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var result = state.NewArray(h.Length);
        foreach (var key in h.Keys)
        {
            result.Push(key);
        }

        return result;
    }

    [RubyDef("() -> Array[V]")]
    public static MRubyValue Values(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var result = state.NewArray(h.Length);
        foreach (var value in h.Values)
        {
            result.Push(value);
        }

        return result;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue HasKey(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var key = state.GetArgumentAt(0);
        return h.ContainsKey(key);
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue HasValue(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var value = state.GetArgumentAt(0);
        return h.ContainsValue(value);
    }

    [RubyDef("() -> bool")]
    public static MRubyValue Empty(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        return h.Length <= 0;
    }

    [RubyDef("(?K) -> V?")]
    public static MRubyValue Default(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureArgumentCount(0, 1);

        if (h.DefaultProc is { } proc && state.TryGetArgumentAt(0, out var key))
        {
            return state.Send(proc, Names.Call, self, key);
        }

        if (h.DefaultValue.HasValue)
        {
            return h.DefaultValue.Value;
        }

        return MRubyValue.Nil;
    }

    [RubyDef("() -> Proc?")]
    public static MRubyValue DefaultProc(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        if (h.DefaultProc is { } proc)
        {
            return proc;
        }

        return MRubyValue.Nil;
    }

    [RubyDef("(V) -> V")]
    public static MRubyValue SetDefault(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);
        var value = state.GetArgumentAt(0);
        h.DefaultValue = value;
        return value;
    }

    [RubyDef("() -> self")]
    public static MRubyValue Clear(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);

        h.Clear();
        return self;
    }

    [RubyDef("() -> [K, V]?")]
    public static MRubyValue Shift(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);

        if (h.TryShift(out var headKey, out var headValue))
        {
            var result = state.NewArray(2);
            result.Push(headKey);
            result.Push(headValue);
            return result;
        }

        return MRubyValue.Nil;
    }

    [RubyDef("(K) -> [K, V]?")]
    public static MRubyValue Assoc(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var searchKey = state.GetArgumentAt(0);
        foreach (var x in h)
        {
            if (state.ValueEquals(searchKey, x.Key))
            {
                var result = state.NewArray(2);
                result.Push(x.Key);
                result.Push(x.Value);
                return result;
            }
        }
        return MRubyValue.Nil;
    }

    [RubyDef("(V) -> [K, V]?")]
    public static MRubyValue RAssoc(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();

        var searchValue = state.GetArgumentAt(0);
        foreach (var x in h)
        {
            if (state.ValueEquals(searchValue, x.Value))
            {
                var result = state.NewArray(2);
                result.Push(x.Key);
                result.Push(x.Value);
                return result;
            }
        }
        return MRubyValue.Nil;
    }

    [RubyDef("() -> self")]
    public static MRubyValue Rehash(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        h.Rehash();
        return self;
    }

    [RubyDef("(K) -> V?")]
    public static MRubyValue InternalDelete(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();

        state.EnsureNotFrozen(h);
        state.EnsureArgumentCount(1);

        var key = state.GetArgumentAt(0);
        h.TryDelete(key, out var value);
        return value;
    }

    [RubyDef("(Hash[K, V]) -> Hash[K, V]")]
    public static MRubyValue InternalMerge(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            state.EnsureValueType(arg, MRubyVType.Hash);
            var other = arg.As<RHash>();
            if (h == other) continue;
            foreach (var entry in other)
            {
                h[entry.Key] = entry.Value;
            }
        }
        return self;
    }

    // Hash#slice(*keys) — returns a new hash containing only entries whose key matches an arg.
    [RubyDef("(*K) -> Hash[K, V]")]
    public static MRubyValue Slice(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var keys = state.GetRestArgumentsAfter(0);
        var result = state.NewHash(keys.Length);
        foreach (var key in keys)
        {
            if (h.TryGetValue(key, out var value))
            {
                result[key] = value;
            }
        }
        return result;
    }

    // Hash#slice!(*keys) — keeps only the listed keys in self, returns the removed entries.
    [RubyDef("(*K) -> Hash[K, V]?")]
    public static MRubyValue SliceBang(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);
        var keys = state.GetRestArgumentsAfter(0);

        var keep = new HashSet<MRubyValue>(state.ValueEqualityComparer);
        foreach (var k in keys) keep.Add(k);

        var removed = state.NewHash(0);
        // Snapshot keys to avoid mutating during iteration.
        var snapshot = new MRubyValue[h.Length];
        var i = 0;
        foreach (var entry in h)
        {
            snapshot[i++] = entry.Key;
        }
        foreach (var key in snapshot)
        {
            if (!keep.Contains(key))
            {
                if (h.TryDelete(key, out var value))
                {
                    removed[key] = value;
                }
            }
        }
        return removed;
    }

    // Hash#__except(*keys) — pattern matching support for **rest binding.
    [RubyDef("(*K) -> Hash[K, V]")]
    public static MRubyValue InternalExcept(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var keys = state.GetRestArgumentsAfter(0);
        var result = state.NewHash(h.Length);
        foreach (var entry in h)
        {
            var skip = false;
            foreach (var k in keys)
            {
                if (entry.Key.Equals(k))
                {
                    skip = true;
                    break;
                }
            }
            if (!skip)
            {
                result[entry.Key] = entry.Value;
            }
        }
        return result;
    }

    // Hash#__pat_values(keys) — used by case/in for hash patterns. Returns the
    // values array when every key is present, or false otherwise.
    [RubyDef("(*K) -> Array[V]?")]
    public static MRubyValue InternalPatValues(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var keysArray = state.GetArgumentAsArrayAt(0);
        var result = state.NewArray(keysArray.Length);
        foreach (var key in keysArray.AsSpan())
        {
            if (!h.TryGetValue(key, out var value))
            {
                return MRubyValue.False;
            }
            result.Push(value);
        }
        return result;
    }
}
