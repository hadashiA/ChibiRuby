using System;
using System.Collections.Generic;
using ChibiRuby;

namespace ChibiRuby.JetPack.Mrb2Cs;

// Per-field representation of a stack struct. Stage 1 emits all Boxed; ② fills Double/Long
// (unboxed FP across calls), ① fills Nested (Vec-in-Ray etc.).
internal enum StackFieldKind { Boxed, Double, Long, Nested }

// The C# struct layout for a stack-allocatable class. Forward-compatible with ①(nested)/②(typed).
internal sealed class StackLayout
{
    public required RClass Cls;
    public required ulong ClassFp;          // identity for the struct type name + variant name
    public required Symbol ConstName;
    public required ulong InitFingerprint;
    public required List<Symbol> Fields;    // ivar symbols, initialize order
    public required List<int> FieldArg;     // field i <- ctor arg FieldArg[i], or -1 if literal
    public required List<MRubyValue> FieldLiteral; // field i <- this literal when FieldArg[i] == -1
    public required List<StackFieldKind> FieldKinds;
    public required List<StackLayout?> FieldNested; // non-null when FieldKinds[i]==Nested (②/①)
    public bool Mutated;                    // a setter is called on it -> pass by ref (①)
    public string NameSuffix = "";          // `_RubyClassName`, so the struct type reads back to source
    public string StructType => "Stk_" + ClassFp.ToString("x16") + NameSuffix;
    public int FieldIndexOf(Symbol f) { for (var i = 0; i < Fields.Count; i++) if (Fields[i] == f) return i; return -1; }
    public string CsFieldType(int i) => FieldKinds[i] switch
    {
        StackFieldKind.Double => "double",
        StackFieldKind.Long => "long",
        StackFieldKind.Nested => FieldNested[i]!.StructType,
        _ => "global::ChibiRuby.MRubyValue",
    };
}
