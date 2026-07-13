using System.Collections.Generic;

namespace ChibiRuby.JetPack.Mrb2Cs;

// The C# source for a whole compiled program, plus the (generated method name, irep
// fingerprint) pairs a host binds to make a re-parse of the same bytecode hit the compiled body.
public sealed class ProgramResult(string source, IReadOnlyList<(string Name, ulong Fingerprint)> methods)
{
    public string Source { get; } = source;
    public IReadOnlyList<(string Name, ulong Fingerprint)> Methods { get; } = methods;
}
