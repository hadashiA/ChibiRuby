#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static MRubyCS.Internal.MemoryMarshalEx;
#endif
using MRubyCS.Internals;

namespace MRubyCS.StdLib;

[RubyClass("Proc")]
static class ProcMembers
{
    [RubyDef("() { (*untyped) -> untyped } -> Proc")]
    public static MRubyValue New(MRubyState state, MRubyValue self)
    {
        var block = state.GetBlockArgument(false);
        var proc = block!.Dup();
        var procValue = new MRubyValue(proc);
        state.Send(procValue, Names.Initialize, procValue);
        if (!proc.HasFlag(MRubyObjectFlags.ProcStrict) &&
            state.CheckProcIsOrphan(proc))
        {
            proc.SetFlag(MRubyObjectFlags.ProcOrphan);
        }
        return procValue;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eql(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (other.VType != MRubyVType.Proc)
        {
            return MRubyValue.False;
        }
        return self.As<RProc>() == other.As<RProc>();
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Arity(MRubyState state, MRubyValue self)
    {
        var proc = self.As<RProc>();
        var sequence = proc.Irep.Sequence;
        if (sequence[0] != (byte)OpCode.Enter)
        {
            // arity is depend on OP_ENTER
            return 0;
        }

        var pc = 0;
        var bbb = OperandBBB.Read(ref GetArrayDataReference(sequence), ref pc);
        var bits = (uint)bbb.A << 16 | (uint)bbb.B << 8 | bbb.C;
        var aspec = new ArgumentSpec(bits);
        // arity = ra || (MRB_PROC_STRICT_P(p) && op) ? -(ma + pa + 1) : ma + pa;
        var arity = aspec.TakeRestArguments || (proc.HasFlag(MRubyObjectFlags.ProcStrict) && aspec.OptionalArgumentsCount > 0)
            ? -(aspec.MandatoryArguments1Count + aspec.MandatoryArguments2Count + 1)
            : aspec.MandatoryArguments1Count + aspec.MandatoryArguments2Count;
        return arity;
    }
}
