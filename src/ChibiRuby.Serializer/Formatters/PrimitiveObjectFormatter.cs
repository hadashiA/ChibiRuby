using System.Collections;
using System.Collections.Generic;

namespace ChibiRuby.Serializer;

public class PrimitiveObjectFormatter : IMRubyValueFormatter<object?>
{
    public static readonly PrimitiveObjectFormatter Instance = new();

    public MRubyValue Serialize(object? value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var type = value.GetType();

        switch (value)
        {
            case bool x:
                return new MRubyValue(x);
            case short x:
                return new MRubyValue(x);
            case ushort x:
                return new MRubyValue(x);
            case int x:
                return new MRubyValue(x);
            case uint x:
                return new MRubyValue(x);
            case long x:
                return new MRubyValue(x);
            case ulong x:
                return new MRubyValue(x);
            case float x:
                return new MRubyValue(x);
            case double x:
                return new MRubyValue(x);
            case string x:
                return new MRubyValue(mrb.NewString($"{x}"));
            case Symbol x:
                return new MRubyValue(x);
            case RObject x:
                return new MRubyValue(x);
            case IDictionary d:
            {
                var hash = mrb.NewHash(d.Count);
                foreach (DictionaryEntry x in d)
                {
                    var k = Serialize(x.Key, mrb, options);
                    var v = Serialize(x.Value, mrb, options);
                    hash.Add(k, v);
                }
                return hash;
            }
            case ICollection c:
            {
                var array = mrb.NewArray(c.Count);
                foreach (var x in c)
                {
                    array.Push(Serialize(x, mrb, options));
                }
                return array;
            }
            default:
                if (type.IsEnum)
                {
                    return mrb.NewString($"{value}");
                }
                throw new MRubySerializationException($"Serialization not supported for type {value.GetType()}");
        }
    }

    public object? Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        if (value.IsNil)
        {
            return null;
        }

        switch (value.VType)
        {
            case MRubyVType.Nil:
                return null;
            case MRubyVType.False:
                return false;
            case MRubyVType.True:
                return true;
            case MRubyVType.Integer:
                return value.IntegerValue;
            case MRubyVType.Float:
                return value.FloatValue;
            case MRubyVType.Symbol:
                return mrb.NameOf(value.SymbolValue).ToString();
            case MRubyVType.String:
                return value.As<RString>().ToString();
            case MRubyVType.Array:
            {
                var array = value.As<RArray>();
                var result = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    var elementValue = mrb.Send(value, Names.OpAref, [new MRubyValue(i)]);
                    var element = options.Resolver.GetFormatterWithVerify<object?>()
                        .Deserialize(elementValue, mrb, options);
                    result[i] = element;
                }
                return result;
            }
            case MRubyVType.Hash:
            {
                var dict = new Dictionary<object, object?>();
                foreach (var x in value.As<RHash>())
                {
                    var k = options.Resolver.GetFormatterWithVerify<string>()
                        .Deserialize(x.Key, mrb, options);
                    var v = options.Resolver.GetFormatterWithVerify<object?>()
                        .Deserialize(x.Value, mrb, options);
                    dict.Add(k, v);
                }
                return dict;
            }
        }
        throw new MRubySerializationException($"Deserialization not supported `{value.VType}`");
    }
}