using ChibiRuby;

namespace ChibiRuby.JetPack.Mrb2Cs;

public readonly record struct AccessorTarget(Symbol Field, ulong Fingerprint, bool IsSetter);
public readonly record struct ConstReturnTarget(MRubyValue Value, ulong Fingerprint);
public readonly record struct InlineSelectorTarget(
    Irep Irep,
    int ArgCount,
    ulong Fingerprint,
    RClass DefiningClass,
    bool ReturnsNew);
