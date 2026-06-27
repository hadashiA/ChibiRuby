using System.Collections.Generic;

namespace ChibiRuby.JetPack.Mrb2Cs;

// One Ruby method compiled to C#: the emitted method source plus the metadata the driver
// (Compile) needs to assemble and bind it. Produced by Mrb2CsCompiler.TryCompileMethod.
public sealed class CompiledMethod(string methodName, string source, int argCount, int instructionCount, bool isLeaf, List<string>? auxiliaryMethods = null)
{
    public string MethodName { get; } = methodName;
    public string Source { get; } = source;
    public int ArgCount { get; } = argCount;
    public int InstructionCount { get; } = instructionCount;
    // No outbound Ruby calls (Send/SendSelf/block sends) -> safe to inline without
    // growing the C# call stack per Ruby call depth (bounds depth to caller+1).
    public bool IsLeaf { get; } = isLeaf;
    // Extra C# methods this body needs emitted alongside it (inlined block bodies as
    // `__blk` methods). The driver adds them to the generated class.
    public IReadOnlyList<string> AuxiliaryMethods { get; } = (IReadOnlyList<string>?)auxiliaryMethods ?? [];
}
