using System;
using System.Collections.Generic;

namespace ChibiRuby.Serializer;

public class EnumAsStringFormatter<T> : IMRubyValueFormatter<T> where T : Enum
{
    class EnumSymbolTable(
        Dictionary<Symbol, T> values,
        Dictionary<T, Symbol> symbols)
    {
        public Dictionary<Symbol, T> Values => values;
        public Dictionary<T, Symbol> Symbols => symbols;

        public static EnumSymbolTable Create(MRubyState mrb)
        {
            var values = new Dictionary<Symbol, T>();
            var symbols = new Dictionary<T, Symbol>();

            var csharpNames = Enum.GetNames(typeof(T));
            var csharpValues = (T[])Enum.GetValues(typeof(T));
            var maxNameLength = 0;
            foreach (var n in csharpNames)
            {
                if (n.Length > maxNameLength) maxNameLength = n.Length;
            }
            Span<byte> nameUtf8 = stackalloc byte[maxNameLength];
            Span<byte> underscoreNameUtf8 = stackalloc byte[maxNameLength * 2];
            for (var i = 0; i < csharpNames.Length; i++)
            {
                var csharpName = csharpNames[i];
                var nameSpan = nameUtf8[..csharpName.Length];
                System.Text.Encoding.UTF8.GetBytes(csharpName, nameSpan);
                var underscoreSpan = underscoreNameUtf8[..(csharpName.Length * 2)];
                NamingConventionMutator.SnakeCase.TryMutate(nameSpan, underscoreSpan, out var written);

                var symbol = mrb.Intern(underscoreNameUtf8[..written]);
                values.Add(symbol, csharpValues[i]);
                symbols.Add(csharpValues[i], symbol);
            }
            return new EnumSymbolTable(values, symbols);
        }
    }

    [ThreadStatic]
    static Dictionary<MRubyState, EnumSymbolTable>? Cache;

    public MRubyValue Serialize(T value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        Cache ??= new Dictionary<MRubyState, EnumSymbolTable>();
        if (!Cache.TryGetValue(mrb, out var table))
        {
            Cache[mrb] = table = EnumSymbolTable.Create(mrb);
        }
        return table.Symbols[value];
    }

    public T Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        Cache ??= new Dictionary<MRubyState, EnumSymbolTable>();
        if (!Cache.TryGetValue(mrb, out var table))
        {
            Cache[mrb] = table = EnumSymbolTable.Create(mrb);
        }
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Symbol, state: mrb);
        if (table.Values.TryGetValue(value.SymbolValue, out var result))
        {
            return result;
        }
        throw new MRubySerializationException($"Cannot convert to enum `{typeof(T)}` from symbol `{mrb.Stringify(value)}`");
    }
}