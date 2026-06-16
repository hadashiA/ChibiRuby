using System;

namespace ChibiRuby;

public sealed class RMethod(
    MRubyValue receiver,
    Symbol methodId,
    RClass owner,
    MRubyMethod method,
    RClass klass)
    : RObject(klass)
{
    public MRubyValue Receiver { get; } = receiver;
    public Symbol MethodId { get; } = methodId;
    public RClass Owner { get; } = owner;
    public MRubyMethod Method { get; } = method;

    internal override RObject Clone()
    {
        var clone = new RMethod(Receiver, MethodId, Owner, Method, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Receiver, Owner, Method);
    }
}
