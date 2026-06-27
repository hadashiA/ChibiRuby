namespace ChibiRuby;

public class RData : RObject
{
    public RData(object? data) : base(MRubyVType.CSharpData, null!)
    {
        Data = data;
    }

    public RData(RClass c, object? data) : base(MRubyVType.CSharpData, c)
    {
        Data = data;
    }

    public RData(RClass c) : base(MRubyVType.CSharpData, c)
    {
    }

    public object? Data { get; set; }

    internal override RObject Clone()
    {
        var clone = new RData(Class, Data);
        InstanceVariables.CopyTo(ref clone.InstanceVariables);
        return clone;
    }
}
