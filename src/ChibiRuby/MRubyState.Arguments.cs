using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChibiRuby;

partial class MRubyState
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ModifyCurrentMethodId(Symbol newMethodId) =>
        Context.ModifyCurrentMethodId(newMethodId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRecursiveCalling(Symbol methodId, MRubyValue self) =>
        Context.IsRecursiveCalling(methodId, self);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRecursiveCalling(Symbol methodId, MRubyValue self, MRubyValue arg0) =>
        Context.IsRecursiveCalling(methodId, self, arg0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArgumentCount() => Context.GetArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetKeywordArgumentCount() => Context.GetKeywordArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetArgumentAt(int index) => Context.GetArgumentAt(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetKeywordArgument(Symbol key) => Context.GetKeywordArgument(key);

    public bool TryGetArgumentAt(int index, out MRubyValue value) =>
        Context.TryGetArgumentAt(index, out value);

    public bool TryGetKeywordArgument(Symbol key, out MRubyValue value) =>
        Context.TryGetKeywordArgument(key, out value);

    public MRubyValue GetSelf() => Context.GetSelf();

    /// <summary>
    /// Fast <c>ref</c>-based slice of the current method's positional arguments as a
    /// <see cref="Span{T}"/> carved directly out of the VM stack. Prefer this over repeated
    /// <see cref="GetArgumentAt"/> calls in hot C# builtins.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<MRubyValue> GetArgumentsSpan() => Context.GetArgumentsSpan();

    public ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> GetKeywordArguments() =>
        Context.GetKeywordArgs(ref Context.CurrentCallInfo);

    public RClass GetArgumentAsClassAt(int index)
    {
        var arg = GetArgumentAt(index);
        EnsureClassOrModule(arg);
        return arg.As<RClass>();
    }

    public Symbol GetArgumentAsSymbolAt(int index)
    {
        var arg = GetArgumentAt(index);
        return AsSymbol(arg);
    }

    public long GetArgumentAsIntegerAt(int index)
    {
        var arg = GetArgumentAt(index);
        return AsInteger(arg);
    }

    public double GetArgumentAsFloatAt(int index)
    {
        var arg = GetArgumentAt(index);
        return AsFloat(arg);
    }

    public RString GetArgumentAsStringAt(int index)
    {
        var arg = GetArgumentAt(index);
        if (arg.Object is RString str)
        {
            return str;
        }
        Raise(Names.TypeError, $"{StringifyAny(arg)} cannot be converted to String");
        return default!;
    }

    public RArray GetArgumentAsArrayAt(int index)
    {
        var arg = GetArgumentAt(index);
        if (arg.Object is RArray array)
        {
            return array;
        }
        Raise(Names.TypeError, $"{StringifyAny(arg)} cannot be converted to Array");
        return default!;
    }

    public RHash GetArgumentAsHashAt(int index)
    {
        var arg = GetArgumentAt(index);
        if (arg.Object is RHash hash)
        {
            return hash;
        }
        Raise(Names.TypeError, $"{StringifyAny(arg)} cannot be converted to Hash");
        return default!;
    }

    public RRange GetArgumentAsRangeAt(int index)
    {
        var arg = GetArgumentAt(index);
        if (arg.Object is RRange range)
        {
            return range;
        }
        Raise(Names.TypeError, $"{StringifyAny(arg)} cannot be converted to Range");
        return default!;
    }

    public ReadOnlySpan<MRubyValue> GetRestArgumentsAfter(int startIndex) =>
        Context.GetRestArgumentsAfter(startIndex);

    public RProc? GetBlockArgument(bool optional = true)
    {
        var blockArg = Context.GetBlockArgument();
        if (blockArg.Object is RProc proc)
        {
            return proc;
        }

        if (!optional)
        {
            Raise(Names.ArgumentError, "no block given"u8);
        }
        return null;
    }
}