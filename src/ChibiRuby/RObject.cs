namespace ChibiRuby;

public class RObject : RBasic
{
    // Public FIELD (not a property) so AOT-generated code reads/writes ivars directly
    // (recv.InstanceVariables.Get/Set) and, since VariableTable is a struct, mutates it IN PLACE.
    // A property getter would return a copy and silently drop Set mutations.
    public VariableTable InstanceVariables = new();

    public RObject(RClass klass) : this(klass.InstanceVType, klass)
    {
    }

    internal RObject(MRubyVType vType, RClass klass) : base(vType, klass)
    {
    }

    /// <summary>
    /// Create a copy of the object (equivalent to `init_copy`)
    /// </summary>
    /// <remarks>
    ///
    /// Because of the ruby specification, overrideable processes are implemented with `initialize_copy`.
    /// </remarks>
    internal virtual RObject Clone()
    {
        var clone = new RObject(VType, Class);
        InstanceVariables.CopyTo(ref clone.InstanceVariables);
        return clone;
    }
}

