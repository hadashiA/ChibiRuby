namespace ChibiRuby.JetPack;

struct RubyIRCallSite(int symbolIndex, int argumentStart, int argumentCount)
{
    public readonly int SymbolIndex = symbolIndex;
    public readonly int ArgumentStart = argumentStart;
    public readonly int ArgumentCount = argumentCount;
    RClass? guardInlineReceiverClass;
    int guardInlineMethodCacheVersion;
    ulong guardInlineCalleeFingerprint;

    // Guard metadata for an SSA-spliced inline body. The GuardInlineClass
    // instruction reads this off the cold-path Send's call site to decide whether
    // the receiver still matches the class/method shape the body was specialized
    // for; on a miss it deopts to that same Send.
    public void SetGuardInline(RClass receiverClass, int currentMethodCacheVersion, ulong calleeFingerprint = 0)
    {
        guardInlineReceiverClass = receiverClass;
        guardInlineMethodCacheVersion = currentMethodCacheVersion;
        guardInlineCalleeFingerprint = calleeFingerprint;
    }

    public bool TryGetGuardInline(out ulong calleeFingerprint)
    {
        if (guardInlineReceiverClass is { } guardedClass)
        {
            calleeFingerprint = guardInlineCalleeFingerprint;
            return true;
        }
        calleeFingerprint = 0;
        return false;
    }
}

readonly struct RubyIROperandList(int operandStart, int operandCount)
{
    public readonly int OperandStart = operandStart;
    public readonly int OperandCount = operandCount;
}
