#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif
using ChibiRuby.Internals;

namespace ChibiRuby.StdLib;

/// <summary>
/// A first-class callable created from a block. Capture a block as a
/// <c>Proc</c> with <c>Proc.new</c>, <c>proc { ... }</c>, or the <c>&amp;block</c>
/// parameter form; invoke with <c>call</c>, <c>[]</c>, or <c>.()</c>. Lambdas
/// (<c>-&gt;</c>) are <c>Proc</c>s with stricter arity and <c>return</c> semantics.
/// </summary>
[RubyClass("Proc")]
static class ProcMembers
{
    /// <summary>
    /// Creates a new <c>Proc</c> from the given block.
    /// </summary>
    /// <example>
    /// <code>
    /// pr = Proc.new { |x| x + 1 }
    /// pr.call(5)        # => 6
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> and the given value refer to the same Proc.
    /// </summary>
    /// <example>
    /// <code>
    /// pr = proc { |x| x }
    /// pr.eql?(pr)       # => true
    /// pr.eql?(proc {})  # => false
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the number of mandatory arguments. If the block takes a rest
    /// argument, returns <c>-(required + 1)</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// proc { |a, b| }.arity      # => 2
    /// proc { |*a| }.arity        # => -1
    /// </code>
    /// </example>
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
        var bits = (uint)bbb.A << 16 | (uint)bbb.B << 8 | (uint)bbb.C;
        var aspec = new ArgumentSpec(bits);
        // arity = ra || (MRB_PROC_STRICT_P(p) && op) ? -(ma + pa + 1) : ma + pa;
        var arity = aspec.TakeRestArguments || (proc.HasFlag(MRubyObjectFlags.ProcStrict) && aspec.OptionalArgumentsCount > 0)
            ? -(aspec.MandatoryArguments1Count + aspec.MandatoryArguments2Count + 1)
            : aspec.MandatoryArguments1Count + aspec.MandatoryArguments2Count;
        return arity;
    }
}
