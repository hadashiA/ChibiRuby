using System.Text;

namespace MRubyCS.SourceGenerator;

using Microsoft.CodeAnalysis;

sealed class ConstSymbolDefinition
{
    const uint OffsetBasis = 2166136261u;
    const uint FnvPrime = 16777619u;

    public int Index { get; }
    public string SymbolName { get; }
    public string VariableName { get; }
    public byte[] Utf8 { get; }
    public int HashCode { get; }

    public ConstSymbolDefinition(int index, string symbolName, string variableName)
    {
        Index = index;
        SymbolName = symbolName;
        VariableName = variableName;
        Utf8 = Encoding.UTF8.GetBytes(symbolName);

        var hash = OffsetBasis;
        foreach (var b in symbolName)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        HashCode = unchecked((int)hash);
    }
}

[Generator(LanguageNames.CSharp)]
public class MRubyCSSourceGenerator : IIncrementalGenerator
{
    static readonly KeyValuePair<string, string>[] KnownSymbols =
    [
        new("ClassNameKey", "__classname__"),
        new("OuterKey", "__outer__"),
        new("OuterClassKey", "__outerclass__"),
        new("AttachedKey", "__attached__"),
        new("InheritedKey", "__inherited__"),
        new("IdKey", "__id__"),

        new("NameVariable", "@name"),
        new("ArgsVariable", "@args"),

        new("New", "new"),
        new("Id", "id"),
        new("Send", "send"),
        new("Dup", "dup"),
        new("Clone", "clone"),
        new("ToS", "to_s"),
        new("ToSym", "to_sym"),
        new("ToA", "to_a"),
        new("ToI", "to_i"),
        new("ToF", "to_f"),
        new("Inspect", "inspect"),
        new("Raise", "raise"),
        new("Name", "name"),
        new("Class", "class"),
        new("Default", "default"),
        new("Initialize", "initialize"),
        new("InitializeCopy", "initialize_copy"),
        new("InstanceEval", "instance_eval"),
        new("MethodAdded", "method_added"),
        new("SingletonMethodAdded", "singleton_method_added"),
        new("Nil", "nil"),
        new("SuperClass", "superclass"),
        new("Inherited", "inherited"),
        new("ExtendObject", "extend_object"),
        new("Extended", "extended"),
        new("Prepended", "prepended"),
        new("Private", "private"),
        new("Protected", "protected"),
        new("ClassEval", "class_eval"),
        new("ModuleEval", "module_eval"),
        new("Hash", "hash"),
        new("Exception", "exception"),
        new("Call", "call"),
        new("MethodMissing", "method_missing"),

        new("QNil", "nil?"),
        new("QEqual", "equal?"),
        new("QEql", "eql?"),
        new("QBlockGiven", "block_given?"),
        new("QRespondTo", "respond_to?"),
        new("QRespondToMissing", "respond_to_missing?"),
        new("QInclude", "include?"),
        new("QIsA", "is_a?"),
        new("QInstanceOf", "instance_of?"),
        new("QKindOf", "kind_of?"),

        new("OpNot", "!"),
        new("OpMod", "%"),
        new("OpAnd", "&"),
        new("OpMul", "*"),
        new("OpAdd", "+"),
        new("OpSub", "-"),
        new("OpDiv", "/"),
        new("OpLt", "<"),
        new("OpLe", "<="),
        new("OpGt", ">"),
        new("OpGe", ">="),
        new("OpXor", "^"),
        new("OpTick", "`"),
        new("OpOr", "|"),
        new("OpNeg", "~"),
        new("OpNeq", "!="),
        new("OpMatch", "=~"),
        new("OpAndAnd", "&&"),
        new("OpPow", "**"),
        new("OpPlus", "+@"),
        new("OpMinus", "-@"),
        new("OpLShift", "<<"),
        new("OpRShift", ">>"),
        new("OpEq", "=="),
        new("OpEqq", "==="),
        new("OpCmp", "<=>"),
        new("OpAref", "[]"),
        new("OpAset", "[]="),
        new("OpOrOr", "||"),

        new("BasicObjectClass", "BasicObject"),
        new("ObjectClass", "Object"),
        new("ModuleClass", "Module"),
        new("ClassClass", "Class"),
        new("ExceptionClass", "Exception"),
        new("RuntimeError", "RuntimeError"),
        new("TypeError", "TypeError"),
        new("ZeroDivisionError", "ZeroDivisionError"),
        new("ArgumentError", "ArgumentError"),
        new("NoMethodError", "NoMethodError"),
        new("NameError", "NameError"),
        new("IndexError", "IndexError"),
        new("RangeError", "RangeError"),
        new("FrozenError", "FrozenError"),
        new("NotImplementedError", "NotImplementedError"),
        new("LocalJumpError", "LocalJumpError"),
        new("NoMatchingPatternError", "NoMatchingPatternError"),
        new("SystemStackError", "SystemStackError"),
        new("FloatDomainError", "FloatDomainError"),
        new("KeyError", "KeyError"),
        new("FiberError", "FiberError"),
        new("RegexpError", "RegexpError"),

        new("GvMatch", "$~"),
        new("GvMatchedString", "$&"),
        new("GvPreMatch", "$`"),
        new("GvPostMatch", "$'"),
        new("GvLastCapture", "$+"),
        new("Gv1", "$1"),
        new("Gv2", "$2"),
        new("Gv3", "$3"),
        new("Gv4", "$4"),
        new("Gv5", "$5"),
        new("Gv6", "$6"),
        new("Gv7", "$7"),
        new("Gv8", "$8"),
        new("Gv9", "$9"),
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static initContext =>
        {
            var index = 1;
            var stringBuilder = new StringBuilder();

            var definitions = KnownSymbols
                .Select(x => new ConstSymbolDefinition(index++, x.Value, x.Key))
                .ToArray();

            var symbolDefinitionsByHashCode = definitions.ToLookup(
                x => x.HashCode,
                x => x);

            stringBuilder.AppendLine($$"""
// <auto-generated />
using System;
using System.Runtime.CompilerServices;

namespace MRubyCS;

static class Names
{
    public static int Count => {{definitions.Length}};

    public static readonly byte[][] Utf8Names =
    [
""");
            foreach (var x in definitions)
            {
                var byteArrayString = string.Join(", ", x.Utf8.Select(x => x.ToString()));
                stringBuilder.AppendLine($$"""
        [{{byteArrayString}}], // "{{x.SymbolName}}"
""");
            }
            stringBuilder.AppendLine($$"""
    ];

""");
            foreach (var x in definitions)
            {
                stringBuilder.AppendLine($$"""
    /// <summary>
    /// return known symbol ("{{x.SymbolName}}")
    /// </summary>
    public static Symbol {{x.VariableName}}
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new({{x.Index}});
    }

""");
            }
            stringBuilder.AppendLine($$"""
    public static bool TryFind(int hashCode, ReadOnlySpan<byte> name, out Symbol symbol)
    {
        switch (hashCode)
        {
""");
            foreach (var x in symbolDefinitionsByHashCode)
            {
                stringBuilder.AppendLine($$"""
            case {{x.Key}}:
""");
                if (x.Count() == 1)
                {
                    var singleValue = x.First();
                    stringBuilder.AppendLine($$"""
                symbol = new Symbol({{singleValue.Index}});
                return true;
""");
                }
                else
                {
                    var branch = "if";
                    foreach (var xs in x)
                    {
                        stringBuilder.AppendLine($$"""
                {{branch}} (name.SequenceEqual("{{xs.SymbolName}}"u8))
                {
                    symbol = new Symbol({{xs.Index}});
                    return true;
                }
""");
                        branch = "else if";
                    }
                    stringBuilder.AppendLine($$"""
                break;
""");
                }
            }
            stringBuilder.AppendLine($$"""
        }
        symbol = default;
        return false;
    }

    public static bool TryGetName(Symbol symbol, out ReadOnlySpan<byte> name)
    {
        if (symbol.Value > 0 && symbol.Value <= Count)
        {
            name = Utf8Names[(int)symbol.Value - 1];
            return true;
        }
        name = default!;
        return false;
    }
}
""");
            initContext.AddSource("KnownSymbols.g.cs", stringBuilder.ToString());
        });

        var knownSymbolMap = KnownSymbols.ToDictionary(kv => kv.Key, kv => kv.Value);
        RbsGenerator.Register(context, knownSymbolMap);
    }
}
