namespace ChibiRuby;

public sealed class RInteger : RObject
{
    public long Value { get; }

    internal RInteger(long value, RClass integerClass) : base(MRubyVType.Integer, integerClass)
    {
        Value = value;
    }
}
