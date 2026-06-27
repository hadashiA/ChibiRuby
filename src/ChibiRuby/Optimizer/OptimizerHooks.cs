using System.Runtime.CompilerServices;

namespace ChibiRuby;

public partial class MRubyState
{
    // Hot-loop frame resolution: read the irep's arrays directly. (The optimizer
    // dispatch path and the mrb-bytecode-execution image path were both removed;
    // AOT-compiled bodies dispatch via Irep.CompiledBody instead.)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetExecutableArrays(Irep irep, out byte[] code, out Symbol[] symbols, out int registerCount)
    {
        code = irep.Sequence;
        symbols = irep.Symbols;
        registerCount = irep.RegisterVariableCount;
    }
}
