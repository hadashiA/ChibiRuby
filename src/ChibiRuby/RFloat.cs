namespace ChibiRuby;

public sealed class RFloat : RObject
{
    public double Value { get; }

    internal RFloat(double value, RClass floatClass) : base(MRubyVType.Float, floatClass)
    {
        Value = value;
    }
}
