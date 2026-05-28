namespace ChibiRuby.Internals;

internal static class SymbolHelpers
{

    // Indexed by (opCode - OpCode.Add); order must match OpCode.cs (Add..GE):
    // Add, AddI, Sub, SubI, AddILV, SubILV, Mul, Div, EQ, LT, LE, GT, GE
    static readonly Symbol[] OpCodeSymbols =
    [
        Names.OpAdd,
        Names.OpAdd,
        Names.OpSub,
        Names.OpSub,
        Names.OpAdd,
        Names.OpSub,
        Names.OpMul,
        Names.OpDiv,
        Names.OpEq,
        Names.OpLt,
        Names.OpLe,
        Names.OpGt,
        Names.OpGe
    ];

    public static Symbol GetOpCodeSymbol(OpCode opCode)
    {
        return opCode is OpCode.GetIdx or OpCode.GetIdx0
            ? Names.OpAref
            : OpCodeSymbols[(int)opCode - (int)OpCode.Add];
    }
}