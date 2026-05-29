using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ChibiRuby;

public readonly record struct Symbol(uint Value)
{
    public static readonly Symbol Empty = new(0);
    public readonly uint Value = Value;
}

class SymbolTable
{
    readonly record struct Key(int HashCode)
    {
        const uint OffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;

        public static Key Create(ReadOnlySpan<byte> symbolName)
        {
            var hash = OffsetBasis;
            foreach (var b in symbolName)
            {
                hash ^= b;
                hash *= FnvPrime;
            }
            return new Key(unchecked((int)hash));
        }

        public override int GetHashCode() => HashCode;
    }

    const int PackLengthMax = 5;

    static readonly byte[] PackTable = "_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"u8.ToArray();

    [ThreadStatic]
    static byte[]? nameBuffer;

    static uint lastId = (uint)Names.Count;

    static byte[] ThreadStaticBuffer() => nameBuffer ??= new byte[32];

    readonly Dictionary<Symbol, byte[]> names = new(64);
    readonly Dictionary<Key, Symbol> symbols = new(64);

    public Symbol Intern(ReadOnlySpan<byte> utf8)
    {
        if (TryFind(utf8, out var symbol))
        {
            return symbol;
        }

        symbol = new Symbol(++lastId);
        var nameBuf = new byte[utf8.Length];
        utf8.CopyTo(nameBuf);
        names.Add(symbol, nameBuf);
        symbols.Add(Key.Create(utf8), symbol);
        return symbol;
    }

    public Symbol InternLiteral(byte[] utf8)
    {
        if (TryFind(utf8, out var symbol))
        {
            return symbol;
        }
        symbol = new Symbol(++lastId);
        names.Add(symbol, utf8);
        symbols.Add(Key.Create(utf8), symbol);
        return symbol;
    }

    public Symbol Intern(string s)
    {
        var buf = ThreadStaticBuffer();
        var maxLength = Encoding.UTF8.GetMaxByteCount(s.Length);
        if (buf.Length < maxLength)
        {
            buf = nameBuffer = new byte[maxLength];
        }
        var bytesWritten = Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0);
        return Intern(buf.AsSpan(0, bytesWritten));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFind(ReadOnlySpan<byte> utf8, out Symbol symbol)
    {
        // if (TryInlinePack(utf8, out symbol))
        // {
        //     return true;
        // }
        var key = Key.Create(utf8);
        return symbols.TryGetValue(key, out symbol) ||
               Names.TryFind(key.HashCode, utf8, out symbol);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> NameOf(Symbol symbol)
    {
        if(symbol.Value==0)
        {
            return default;
        }
        // if (TryInlineUnpack(symbol, out var utf8))
        // {
        //     return utf8;
        // }
        if (Names.TryGetName(symbol, out var c))
        {
            return c;
        }
        return names[symbol];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool IsInlined(Symbol symbol) => symbol.Value > 1 << 24;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryInlinePack(ReadOnlySpan<byte> utf8, out Symbol packedSymbol)
    {
        if (utf8.Length > PackLengthMax || utf8.IsEmpty)
        {
            packedSymbol = default;
            return false;
        }

        uint packedValue = 0;
        var table = PackTable.AsSpan();
        for (var i = 0; i < utf8.Length; i++)
        {
            var ch = utf8[i];
            var x = table.IndexOf(ch);
            if (x < 0)
            {
                packedSymbol = default;
                return false;
            }
            var bits = (uint)x + 1;
            packedValue |= bits << (24 - i * 6);
        }

        packedSymbol = new Symbol(packedValue);
        // assert((sym) >= (1<<24))
        return true;
    }

    static bool TryInlineUnpack(Symbol symbol, out ReadOnlySpan<byte> utf8)
    {
        if (!IsInlined(symbol))
        {
            utf8 = default!;
            return false;
        }

        Span<byte> buf = ThreadStaticBuffer();

        int i;
        for (i = 0; i < PackLengthMax; i++)
        {
            uint bits = symbol.Value >> (24 - i * 6) & 0x3f;
            if (bits == 0) break;
            buf[i] = PackTable[bits - 1];
        }
        utf8 = buf[..i];
        return true;
    }
}
