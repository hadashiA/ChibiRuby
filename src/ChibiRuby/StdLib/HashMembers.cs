using System.Collections.Generic;

namespace ChibiRuby.StdLib;

/// <summary>
/// Unordered mapping from keys to values (insertion order is preserved on
/// iteration, matching CRuby). Keys must implement <c>hash</c> and
/// <c>eql?</c>; lookup is via <c>h[key]</c>. <c>Hash</c> is mutable, includes
/// <c>Enumerable</c>, and supports a default value or default-proc fallback
/// when a key is missing.
/// </summary>
[RubyClass("Hash", TypeParameters = "K, V")]
static class HashMembers
{
    /// <summary>
    /// Initializes a new <c>Hash</c>. With an argument sets the default value; with a block sets the default proc.
    /// </summary>
    /// <example>
    /// <code>
    /// h = Hash.new(0)
    /// h[:a] += 1            # => 1
    /// h2 = Hash.new { |hash, k| hash[k] = [] }
    /// h2[:xs] &lt;&lt; 1          # => [1]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Replaces the contents of <c>self</c> with a copy of the given hash. Called by <c>dup</c> and <c>clone</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1}.dup     # => {a: 1}
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns a human-readable string representation of <c>self</c>, like <c>"{key=&gt;value, ...}"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2}.inspect    # => "{a: 1, b: 2}"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Element reference <c>[]</c>. Returns the value for the given key, or the default value when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {a: 1, b: 2}
    /// h[:a]        # => 1
    /// h[:z]        # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Element assignment <c>[]=</c>. Stores the value under the given key and returns the value. String keys are frozen.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {}
    /// h[:a] = 1     # => 1
    /// h             # => {a: 1}
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the number of key-value pairs in <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2}.size    # => 2
    /// {}.size              # => 0
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Size(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        return h.Length;
    }

    /// <summary>
    /// Returns a new array containing the keys of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2}.keys    # => [:a, :b]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns a new array containing the values of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2}.values    # => [1, 2]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> contains the given key.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1}.has_key?(:a)    # => true
    /// {a: 1}.has_key?(:z)    # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue HasKey(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var key = state.GetArgumentAt(0);
        return h.ContainsKey(key);
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> contains the given value.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1}.has_value?(1)    # => true
    /// {a: 1}.has_value?(9)    # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue HasValue(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        var value = state.GetArgumentAt(0);
        return h.ContainsValue(value);
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> contains no key-value pairs.
    /// </summary>
    /// <example>
    /// <code>
    /// {}.empty?         # => true
    /// {a: 1}.empty?     # => false
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue Empty(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        return h.Length <= 0;
    }

    /// <summary>
    /// Returns the default value of <c>self</c>. When a default proc is set and a key is given, calls the proc with <c>self</c> and the key.
    /// </summary>
    /// <example>
    /// <code>
    /// h = Hash.new(0)
    /// h.default       # => 0
    /// {}.default      # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the default proc of <c>self</c>, or <c>nil</c> when none is set.
    /// </summary>
    /// <example>
    /// <code>
    /// h = Hash.new { |hash, k| 0 }
    /// h.default_proc.class    # => Proc
    /// {}.default_proc         # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Sets the default value of <c>self</c> and returns the given value.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {}
    /// h.default = 0
    /// h[:missing]     # => 0
    /// </code>
    /// </example>
    [RubyDef("(V) -> V")]
    public static MRubyValue SetDefault(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);
        var value = state.GetArgumentAt(0);
        h.DefaultValue = value;
        return value;
    }

    /// <summary>
    /// Removes all key-value pairs from <c>self</c> and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {a: 1, b: 2}
    /// h.clear     # => {}
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue Clear(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);

        h.Clear();
        return self;
    }

    /// <summary>
    /// Removes and returns the first <c>[key, value]</c> pair from <c>self</c>, or <c>nil</c> when empty.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {a: 1, b: 2}
    /// h.shift     # => [:a, 1]
    /// h           # => {b: 2}
    /// </code>
    /// </example>
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

    /// <summary>
    /// Searches <c>self</c> for the given key and returns the matching <c>[key, value]</c> pair, or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2}.assoc(:a)    # => [:a, 1]
    /// {a: 1}.assoc(:z)          # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Searches <c>self</c> for the given value and returns the first matching <c>[key, value]</c> pair, or <c>nil</c> when not found.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2}.rassoc(2)    # => [:b, 2]
    /// {a: 1}.rassoc(9)          # => nil
    /// </code>
    /// </example>
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

    /// <summary>
    /// Rebuilds the hash table based on the current hash values of each key. Call after mutating keys in place.
    /// </summary>
    /// <example>
    /// <code>
    /// k = [1]
    /// h = {k => :v}
    /// k &lt;&lt; 2
    /// h.rehash      # => h
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue Rehash(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        h.Rehash();
        return self;
    }

    /// <summary>
    /// Switches the hash to identity key semantics: keys are compared by object identity
    /// instead of <c>hash</c>/<c>eql?</c>, so mutating a key object does not invalidate it.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {}.compare_by_identity
    /// a = [0]
    /// h[a] = 42
    /// a[0] = 1
    /// h[a]    # => 42
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue CompareByIdentity(MRubyState state, MRubyValue self)
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);
        h.CompareByIdentity(new MRubyValueHashKeyEqualityComparer(state, byIdentity: true));
        return self;
    }

    /// <summary>Returns whether the hash compares keys by identity.</summary>
    [RubyDef("() -> bool")]
    public static MRubyValue QCompareByIdentity(MRubyState state, MRubyValue self)
    {
        return self.As<RHash>().ComparedByIdentity;
    }

    /// <summary>Internal helper used by <c>Hash#delete</c> to remove and return the value for a key.</summary>
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

    /// <summary>Internal helper used by <c>Hash#merge!</c> to copy entries from other hashes into <c>self</c>.</summary>
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

    // Hash#slice(*keys) -- returns a new hash containing only entries whose key matches an arg.
    /// <summary>
    /// Returns a new hash containing only the entries for the given keys that exist in <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// {a: 1, b: 2, c: 3}.slice(:a, :c)    # => {a: 1, c: 3}
    /// </code>
    /// </example>
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

    // Hash#slice!(*keys) -- keeps only the listed keys in self, returns the removed entries.
    /// <summary>
    /// Removes from <c>self</c> all entries whose key is not in the given list, and returns a new hash containing the removed entries.
    /// </summary>
    /// <example>
    /// <code>
    /// h = {a: 1, b: 2, c: 3}
    /// h.slice!(:a, :c)     # => {b: 2}
    /// h                    # => {a: 1, c: 3}
    /// </code>
    /// </example>
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

    // Hash#__except(*keys) -- pattern matching support for **rest binding.
    /// <summary>Internal helper used by hash pattern matching to bind <c>**rest</c> by excluding matched keys.</summary>
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

    // Hash#__pat_values(keys) -- used by case/in for hash patterns. Returns the
    // values array when every key is present, or false otherwise.
    /// <summary>Internal helper used by <c>case/in</c> hash patterns to fetch the values for a list of keys, or <c>false</c> when any key is missing.</summary>
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
