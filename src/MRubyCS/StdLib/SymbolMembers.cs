using System;
using MRubyCS.Internals;

namespace MRubyCS.StdLib;

[RubyClass("Symbol")]
static class SymbolMembers
{
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var name = state.NameOf(self.SymbolValue);
        return new MRubyValue(state.NewString(name.AsSpan()));
    }

    [RubyDef("() -> String")]
    public static MRubyValue Name(MRubyState state, MRubyValue self)
    {
        return new MRubyValue(state.NameOf(self.SymbolValue));
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var name = state.NameOf(self.SymbolValue);
        if (NamingRule.IsSymbolName(name))
        {
            Span<byte> buffer = stackalloc byte[name.Length + 1];
            buffer[0] = (byte)':';
            name.AsSpan().CopyTo(buffer[1..]);
            return new MRubyValue(state.NewString(buffer));
        }

        Span<byte> escapeBuffer = stackalloc byte[name.Length * 2 + 4];
        escapeBuffer[0] = (byte)':';
        int escapedSize;
        while (!NamingRule.TryEscape(name, true, escapeBuffer[1..], out escapedSize))
        {
#pragma warning disable CA2014
            // ReSharper disable once StackAllocInsideLoop
            escapeBuffer = stackalloc byte[escapeBuffer.Length * 2];
#pragma warning restore CA2014
        }
        return new MRubyValue(state.NewString(escapeBuffer[..(escapedSize + 1)]));
    }

    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue Cmp(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (!other.IsSymbol) return MRubyValue.Nil;

        var sym1 = self.SymbolValue;
        var sym2 = other.SymbolValue;
        if (sym1 == sym2)
        {
            return new MRubyValue(0);
        }

        var str1 = state.NameOf(sym1);
        var str2 = state.NameOf(sym2);
        var result = str1.AsSpan().SequenceCompareTo(str2.AsSpan());
        return new MRubyValue(result);
    }


}