namespace ChibiRuby;

public sealed class RException(
    RString? message,
    RClass exceptionClass)
    : RObject(MRubyVType.Exception, exceptionClass)
{
    public RString? Message { get; set; } = message;
    // Settable so the runtime can attach a backtrace at the raise site when the
    // exception object was constructed without one (e.g. `raise SomeError.new(...)`).
    public Backtrace? Backtrace { get; set; }

    internal override RObject Clone()
    {
        var clone = new RException(Message, Class);
        InstanceVariables.CopyTo(ref clone.InstanceVariables);
        return clone;
    }

    public override string ToString() => $"{Message}";
}
