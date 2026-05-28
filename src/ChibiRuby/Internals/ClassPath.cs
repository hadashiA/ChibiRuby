using System;
using System.Collections.Generic;

namespace ChibiRuby.Internals;

struct ClassPath
{
    public int Length => length;
    public bool IsEmpty => length <= 0;

    int length;
    RClass? item0;
    RClass? item1;
    RClass? item2;
    RClass? item3;
    List<RClass>? laterItems;

    public ClassPath(params ReadOnlySpan<RClass> items)
    {
        if (items.Length >= 1) item0 = items[0];
        if (items.Length >= 2) item1 = items[1];
        if (items.Length >= 3) item2 = items[2];
        if (items.Length >= 4) item3 = items[3];
        if (items.Length >= 5) laterItems = [..items[4..].ToArray()];
        length = items.Length;
    }

    public RClass this[int index] => index switch
    {
        0 => item0!,
        1 => item1!,
        2 => item2!,
        3 => item3!,
        _ => laterItems![index]
    };


    void Add(RClass item)
    {
        if (length <= 0) item0 = item;
        else if (length <= 1) item1 = item;
        else if (length <= 2) item2 = item;
        else if (length <= 3) item3 = item;
        else
        {
            (laterItems ??= []).Add(item);
        }
        length++;
    }

    public RString ToRString(MRubyState state)
    {
        if (Length <= 0)
        {
            return state.NewString(0);
        }
        if (Length <= 1)
        {
            var name = state.Stringify(item0!.ClassInstanceVariables.Get(Names.ClassNameKey));
            return name.Dup();
        }

        var result = state.NewString(32);
        if (this[length - 1] is { } top && top != state.ObjectClass)
        {
            result.Concat(state.NameOf(top));
            result.Concat("::"u8);
        }

        for (var i = length - 1; i >= 1; i--)
        {
            var outer = this[i];
            var inner = this[i - 1];
            if (outer.TryFindClassSymbol(inner, out var sym))
            {
                result.Concat(state.NameOf(sym));
                if (i > 1)
                {
                    result.Concat("::"u8);
                }
            }
        }
        return result;
    }

    public static ClassPath Find(MRubyState state, RClass c)
    {
        var result = new ClassPath(c);

        var current = c;
        var next = GetOuterClass(state, c);
        while (true)
        {
            if (current == null ||
                current == state.ObjectClass ||
                next == null ||
                current == next) break;

            result.Add(next);

            next = GetOuterClass(state, next);
            current = GetOuterClass(state, current);
        }
        return result;
    }

    public static RClass? GetOuterClass(MRubyState state, RClass klass)
    {
        var value = klass.InstanceVariables.Get(Names.OuterKey);
        if (value.IsNil) return null;

        if (value.VType is MRubyVType.Class or MRubyVType.Module)
        {
            return value.As<RClass>();
        }
        return null;
    }

    bool DetectOuterLoop(MRubyState state, RClass c)
    {
        var t = c;
        var h = c;
        while (true)
        {
            if (h == null!) return false;
            h = GetOuterClass(state, h);

            if (h == null) return false;
            h = GetOuterClass(state, h);
            t = GetOuterClass(state, t!);
            if (t == h) return true;
        }
    }
}
