using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChibiRuby;

class MethodTable
{
    readonly Dictionary<Symbol, MRubyMethod> methods = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Symbol symbol, out MRubyMethod method) => methods.TryGetValue(symbol, out method!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Symbol id, MRubyMethod method)
    {
        methods.Add(id, method);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => methods.Clear();

    public MRubyMethod this[Symbol id]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => methods[id];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => methods[id] = value;
    }

    public void CopyTo(MethodTable other)
    {
        foreach (var (key, value) in methods)
        {
            other.Add(key, value);
        }
    }
}
