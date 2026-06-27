namespace ChibiRuby.JetPack;

// Op-code classification predicates over RubyIROpCode. Lives in the RubyIR layer so both the IR
// passes (escape summary, return-type inference) and the Mrb2Cs analyzer/emitter share them
// without the lower IR layer reaching up into the codegen layer.
static class RubyIROpInfo
{
    internal static bool IsSendOp(RubyIROpCode op) => op is
        RubyIROpCode.Send or RubyIROpCode.SendSelf or
        RubyIROpCode.SendBlock or RubyIROpCode.SendSelfBlock or
        RubyIROpCode.SendBlockDescriptor or RubyIROpCode.SendSelfBlockDescriptor;

    // Fixnum arithmetic whose result, given the deopt-on-non-fixnum guard, is always a
    // fixnum -> safe to hold unboxed as a C# long.
    internal static bool IsFixnumArith(RubyIROpCode op) => op is
        RubyIROpCode.Add or RubyIROpCode.AddFixnum or
        RubyIROpCode.Sub or RubyIROpCode.SubFixnum or
        RubyIROpCode.Mul or RubyIROpCode.MulFixnum or
        RubyIROpCode.Div or RubyIROpCode.DivFixnum or
        RubyIROpCode.AddImmediate or RubyIROpCode.AddImmediateFixnum or
        RubyIROpCode.SubImmediate or RubyIROpCode.SubImmediateFixnum or
        RubyIROpCode.MulAdd or RubyIROpCode.MulSub or RubyIROpCode.SubMul;

    internal static bool IsFixnumCompare(RubyIROpCode op) => op is
        RubyIROpCode.Lt or RubyIROpCode.LtFixnum or RubyIROpCode.Le or RubyIROpCode.LeFixnum or
        RubyIROpCode.Gt or RubyIROpCode.GtFixnum or RubyIROpCode.Ge or RubyIROpCode.GeFixnum or
        RubyIROpCode.Eq;

    // The subset of arith/compare ops that double-unboxing emits as a guard-free raw-double form
    // when every operand is provably Float. Excludes the fixnum-typed (AddFixnum, LtFixnum, ...)
    // and immediate variants, which are fixnum-specialized. SubMul (reverse) is a fused form too.
    internal static bool IsDoubleArith(RubyIROpCode op) => op is
        RubyIROpCode.Add or RubyIROpCode.Sub or RubyIROpCode.Mul or RubyIROpCode.Div or
        RubyIROpCode.MulAdd or RubyIROpCode.MulSub or RubyIROpCode.SubMul;

    internal static bool IsDoubleFused(RubyIROpCode op) => op is
        RubyIROpCode.MulAdd or RubyIROpCode.MulSub or RubyIROpCode.SubMul;

    internal static bool IsDoubleCompare(RubyIROpCode op) => op is
        RubyIROpCode.Lt or RubyIROpCode.Le or RubyIROpCode.Gt or RubyIROpCode.Ge or RubyIROpCode.Eq;

    // Ops that commit no side effect and cannot re-enter arbitrary code, so a speculation guard's
    // deopt (which re-runs the whole method in the interpreter) is harmless to re-apply.
    internal static bool IsPureSpeculationOp(RubyIROpCode op) =>
        IsFixnumArith(op) || IsFixnumCompare(op) ||
        op is RubyIROpCode.LoadValue or RubyIROpCode.Move or
              RubyIROpCode.GetInstanceVariable or RubyIROpCode.VirtualGetField or
              RubyIROpCode.GetConstant or RubyIROpCode.PureUnarySend or
              RubyIROpCode.Return or RubyIROpCode.ReturnValue or RubyIROpCode.ReturnSelf or
              RubyIROpCode.Jump or RubyIROpCode.JumpIfTruthy or RubyIROpCode.JumpIfFalsy or
              RubyIROpCode.JumpIfNil or RubyIROpCode.CheckArity or RubyIROpCode.GuardInlineClass;
}
