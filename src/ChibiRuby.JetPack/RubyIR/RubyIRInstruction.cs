using System;

using ChibiRuby;
namespace ChibiRuby.JetPack;

[Flags]
enum RubyIREffects : ushort
{
    None = 0,
    Pure = 1 << 0,
    Alloc = 1 << 1,
    ReadField = 1 << 2,
    WriteField = 1 << 3,
    ReadGlobal = 1 << 4,
    WriteGlobal = 1 << 5,
    MayCall = 1 << 6,
    MayEscape = 1 << 7,
    MayRaise = 1 << 8,
    ControlFlow = 1 << 9,
}

enum RubyIROpCode : byte
{
    CheckArity,
    Move,
    LoadValue,
    LoadSelf,
    LoadBlock,
    GetUpVar,
    SetUpVar,
    GetConstant,
    GetModuleConstant,
    GetInstanceVariable,
    SetInstanceVariable,
    GuardClass,
    GuardMethod,
    // Guard for an SSA-spliced inline body: checks the receiver's class identity
    // and method-cache version against the call site stored in Src1. On a match it
    // jumps to the spliced hot body (Aux); on a miss it falls through to the cold
    // path (the original Send), which both paths' continuation merges back into.
    GuardInlineClass,
    InlineBody,
    VirtualNew,
    VirtualGetField,
    VirtualSetField,
    MaterializeObject,
    GuardValueType,
    TypeSwitch,
    GetIndex,
    GetIndex0,
    SetIndex,
    NewArray,
    NewArray2,
    NewHash,
    ArrayRef,
    ArraySet,
    Jump,
    JumpIfTruthy,
    JumpIfFalsy,
    JumpIfNil,
    Send,
    SendSelf,
    SendBlock,
    SendSelfBlock,
    SendBlockDescriptor,
    SendSelfBlockDescriptor,
    PureUnarySend,
    Add,
    AddImmediate,
    Sub,
    SubImmediate,
    AddImmediateFixnum,
    SubImmediateFixnum,
    AddImmediateFloat,
    SubImmediateFloat,
    Mul,
    Div,
    MulAdd,
    MulSub,
    SubMul,
    AddFixnum,
    SubFixnum,
    MulFixnum,
    DivFixnum,
    AddFloat,
    SubFloat,
    MulFloat,
    DivFloat,
    MulAddFloat,
    MulSubFloat,
    SubMulFloat,
    Eq,
    Lt,
    Le,
    Gt,
    Ge,
    LtFixnum,
    LeFixnum,
    GtFixnum,
    GeFixnum,
    LtFloat,
    LeFloat,
    GtFloat,
    GeFloat,
    Return,
    ReturnSelf,
    ReturnValue,
}

static class RubyIROpCodeExtensions
{
    public static RubyIREffects Effects(this RubyIROpCode opCode) => opCode switch
    {
        RubyIROpCode.CheckArity => RubyIREffects.MayRaise,
        RubyIROpCode.Move or
        RubyIROpCode.LoadValue or
        RubyIROpCode.LoadSelf or
        RubyIROpCode.ArrayRef => RubyIREffects.Pure,
        RubyIROpCode.LoadBlock => RubyIREffects.Alloc | RubyIREffects.MayEscape,
        RubyIROpCode.GetUpVar => RubyIREffects.ReadField,
        RubyIROpCode.SetUpVar => RubyIREffects.WriteField | RubyIREffects.MayEscape,
        RubyIROpCode.GetConstant or
        RubyIROpCode.GetModuleConstant => RubyIREffects.ReadGlobal | RubyIREffects.MayCall | RubyIREffects.MayRaise,
        RubyIROpCode.GetInstanceVariable or
        RubyIROpCode.VirtualGetField => RubyIREffects.ReadField,
        RubyIROpCode.SetInstanceVariable or
        RubyIROpCode.VirtualSetField => RubyIREffects.WriteField | RubyIREffects.MayEscape,
        RubyIROpCode.GuardClass => RubyIREffects.Pure | RubyIREffects.MayRaise,
        RubyIROpCode.GuardInlineClass => RubyIREffects.ControlFlow,
        RubyIROpCode.GuardMethod or
        RubyIROpCode.GuardValueType or
        RubyIROpCode.InlineBody => RubyIREffects.MayCall | RubyIREffects.MayEscape | RubyIREffects.MayRaise,
        RubyIROpCode.VirtualNew => RubyIREffects.Alloc | RubyIREffects.MayCall | RubyIREffects.MayRaise,
        RubyIROpCode.MaterializeObject => RubyIREffects.Alloc | RubyIREffects.MayEscape,
        RubyIROpCode.TypeSwitch => RubyIREffects.ControlFlow | RubyIREffects.MayCall | RubyIREffects.MayEscape | RubyIREffects.MayRaise,
        RubyIROpCode.GetIndex => RubyIREffects.MayCall | RubyIREffects.MayRaise,
        RubyIROpCode.GetIndex0 => RubyIREffects.MayCall | RubyIREffects.MayRaise,
        RubyIROpCode.SetIndex => RubyIREffects.WriteField | RubyIREffects.MayCall | RubyIREffects.MayEscape | RubyIREffects.MayRaise,
        RubyIROpCode.NewArray or
        RubyIROpCode.NewArray2 => RubyIREffects.Alloc,
        RubyIROpCode.ArraySet => RubyIREffects.WriteField | RubyIREffects.MayEscape,
        RubyIROpCode.Jump or
        RubyIROpCode.JumpIfTruthy or
        RubyIROpCode.JumpIfFalsy or
        RubyIROpCode.JumpIfNil => RubyIREffects.ControlFlow,
        RubyIROpCode.Send or
        RubyIROpCode.SendSelf or
        RubyIROpCode.SendBlock or
        RubyIROpCode.SendSelfBlock or
        RubyIROpCode.SendBlockDescriptor or
        RubyIROpCode.SendSelfBlockDescriptor or
        RubyIROpCode.PureUnarySend => RubyIREffects.Alloc | RubyIREffects.MayCall | RubyIREffects.MayEscape | RubyIREffects.MayRaise,
        RubyIROpCode.Add or
        RubyIROpCode.AddImmediate or
        RubyIROpCode.Sub or
        RubyIROpCode.SubImmediate or
        RubyIROpCode.AddImmediateFixnum or
        RubyIROpCode.SubImmediateFixnum or
        RubyIROpCode.AddImmediateFloat or
        RubyIROpCode.SubImmediateFloat or
        RubyIROpCode.Mul or
        RubyIROpCode.Div or
        RubyIROpCode.MulAdd or
        RubyIROpCode.MulSub or
        RubyIROpCode.SubMul or
        RubyIROpCode.AddFixnum or
        RubyIROpCode.SubFixnum or
        RubyIROpCode.MulFixnum or
        RubyIROpCode.DivFixnum or
        RubyIROpCode.AddFloat or
        RubyIROpCode.SubFloat or
        RubyIROpCode.MulFloat or
        RubyIROpCode.DivFloat or
        RubyIROpCode.MulAddFloat or
        RubyIROpCode.MulSubFloat or
        RubyIROpCode.SubMulFloat or
        RubyIROpCode.Eq or
        RubyIROpCode.Lt or
        RubyIROpCode.Le or
        RubyIROpCode.Gt or
        RubyIROpCode.Ge or
        RubyIROpCode.LtFixnum or
        RubyIROpCode.LeFixnum or
        RubyIROpCode.GtFixnum or
        RubyIROpCode.GeFixnum or
        RubyIROpCode.LtFloat or
        RubyIROpCode.LeFloat or
        RubyIROpCode.GtFloat or
        RubyIROpCode.GeFloat => RubyIREffects.MayCall | RubyIREffects.MayRaise,
        RubyIROpCode.Return or
        RubyIROpCode.ReturnSelf => RubyIREffects.ControlFlow | RubyIREffects.MayEscape,
        RubyIROpCode.ReturnValue => RubyIREffects.ControlFlow,
        _ => RubyIREffects.None
    };
}

readonly struct RubyIRInstruction(
    RubyIROpCode opCode,
    ushort dst = 0,
    ushort src0 = 0,
    ushort src1 = 0,
    ushort src2 = 0,
    int aux = 0)
{
    public readonly RubyIROpCode OpCode = opCode;
    public readonly ushort Dst = dst;
    public readonly ushort Src0 = src0;
    public readonly ushort Src1 = src1;
    public readonly ushort Src2 = src2;
    public readonly int Aux = aux;
}

readonly struct RubyIRInstructionStream
{
    public readonly RubyIROpCode[] OpCodes;
    public readonly ushort[] Destinations;
    public readonly ushort[] Source0s;
    public readonly ushort[] Source1s;
    public readonly ushort[] Source2s;
    public readonly int[] Auxes;

    RubyIRInstructionStream(
        RubyIROpCode[] opCodes,
        ushort[] destinations,
        ushort[] source0s,
        ushort[] source1s,
        ushort[] source2s,
        int[] auxes)
    {
        OpCodes = opCodes;
        Destinations = destinations;
        Source0s = source0s;
        Source1s = source1s;
        Source2s = source2s;
        Auxes = auxes;
    }

    public int Count => OpCodes.Length;

    public static RubyIRInstructionStream Create(ReadOnlySpan<RubyIRInstruction> instructions)
    {
        var opCodes = new RubyIROpCode[instructions.Length];
        var destinations = new ushort[instructions.Length];
        var source0s = new ushort[instructions.Length];
        var source1s = new ushort[instructions.Length];
        var source2s = new ushort[instructions.Length];
        var auxes = new int[instructions.Length];

        for (var i = 0; i < instructions.Length; i++)
        {
            ref readonly var instruction = ref instructions[i];
            opCodes[i] = instruction.OpCode;
            destinations[i] = instruction.Dst;
            source0s[i] = instruction.Src0;
            source1s[i] = instruction.Src1;
            source2s[i] = instruction.Src2;
            auxes[i] = instruction.Aux;
        }

        return new RubyIRInstructionStream(opCodes, destinations, source0s, source1s, source2s, auxes);
    }
}
