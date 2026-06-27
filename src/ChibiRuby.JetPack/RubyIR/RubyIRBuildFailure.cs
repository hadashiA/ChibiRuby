using ChibiRuby;
namespace ChibiRuby.JetPack;

readonly struct RubyIRBuildFailure(OpCode? opCode, int programCounter, string reason)
{
    public readonly OpCode? OpCode = opCode;
    public readonly int ProgramCounter = programCounter;
    public readonly string Reason = reason;

    public static RubyIRBuildFailure None => new(null, -1, string.Empty);
}
