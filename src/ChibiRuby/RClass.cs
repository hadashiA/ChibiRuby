namespace ChibiRuby;

public class RClass : RObject, ICallScope
{
    public required MRubyVType InstanceVType { get; init; }
    public RClass TargetClass => this;

    public required RClass Super
    {
        get => super;
        init => super = value;
    }

    public bool IsSingletonClass => VType == MRubyVType.SClass;

    internal MethodTable MethodTable { get; private set; } = new();

    internal VariableTable ClassInstanceVariables => VType == MRubyVType.IClass
        ? Class.InstanceVariables
        : InstanceVariables;

    RClass super = default!;

    internal RClass(RClass classClass, MRubyVType vType = MRubyVType.Class) : base(vType, classClass)
    {
    }

    internal override RObject Clone()
    {
        var clone = new RClass(Class, VType)
        {
            InstanceVType = InstanceVType,
            Super = Super,
        };
        // if the origin is not the same as the class, then the origin and
        // the current class need to be copied
        if (HasFlag(MRubyObjectFlags.ClassPrepended))
        {
            var c0 = Super;
            var c1 = clone;
            // copy prepended iclasses
            while (!c0.HasFlag(MRubyObjectFlags.ClassOrigin))
            {
                c1.super = (RClass)c0.Clone();
                c1 = c1.Super;
                c0 = c0.Super;
                c1.Super.SetFlag(MRubyObjectFlags.ClassOrigin);
            }
        }
        if (InstanceVType == MRubyVType.IClass && !HasFlag(MRubyObjectFlags.ClassOrigin))
        {
            MoveMethodTableTo(clone);
        }
        else
        {
            MethodTable.CopyTo(clone.MethodTable);
        }
        clone.super = Super;
        clone.UnFreeze();

        if (VType is MRubyVType.Class or MRubyVType.Module)
        {
            InstanceVariables.CopyTo(clone.InstanceVariables);
            // clone.InstanceVariables.Remove(Ids.ClassName);
        }
        return clone;
    }

    public bool Is(RClass baseClass)
    {
        var x = this;
        while (x != null!)
        {
            if (x == baseClass) return true;
            x = x.Super;
        }
        return false;
    }

    public RClass AsOrigin()
    {
        var result = this;
        if (result.HasFlag(MRubyObjectFlags.ClassPrepended))
        {
            result = Super;
            while (!result.HasFlag(MRubyObjectFlags.ClassOrigin))
            {
                result = result.Super;
            }
            return result;
        }
        return result;
    }

    public RClass GetRealClass()
    {
        var result = this;
        if (result.VType == MRubyVType.Class) return result;

        while (result.VType is MRubyVType.SClass or MRubyVType.IClass)
        {
            result = result.Super;
        }

        return result;
    }

    public bool TryFindClassSymbol(RClass c, out Symbol symbol)
    {
        foreach (var (k, v) in ClassInstanceVariables)
        {
            if (v.VType == c.VType && v.As<RClass>() == c)
            {
                symbol = k;
                return true;
            }
        }
        symbol = default;
        return false;
    }

    internal bool TryFindMethod(Symbol methodId, out MRubyMethod method, out RClass receiver)
    {
        var current = this;
        while (current != null!)
        {
            if (current.MethodTable.TryGet(methodId, out method))
            {
                receiver = current;
                return true;
            }
            current = current.Super;
        }

        method = default!;
        receiver = default!;
        return false;
    }

    internal void MoveMethodTableTo(RClass to)
    {
        to.MethodTable = MethodTable;
        MethodTable = new MethodTable();
    }

    internal void ShareInstanceVariablesTo(RClass to)
    {
        to.InstanceVariables = InstanceVariables;
    }

    internal bool TryIncludeModule(RClass insertPos, RClass m, bool searchSuper)
    {
        var mt = AsOrigin().MethodTable;

        while (m != null!)
        {
            var originalSeen = false;
            var superclassSeen = false;

            if (this == insertPos)
            {
                originalSeen = true;
            }
            if (m.HasFlag(MRubyObjectFlags.ClassPrepended))
            {
                goto SKIP;
            }
            if (mt == m.MethodTable)
            {
                // circular references
                return false;
            }

            var p = Super;
            while (p != null!)
            {
                if (this == p)
                {
                    originalSeen = true;
                }
                if (p.VType == MRubyVType.IClass)
                {
                    if (p.MethodTable == m.MethodTable)
                    {
                        if (!superclassSeen && originalSeen)
                        {
                            insertPos = p; // move insert point
                        }
                        goto SKIP;
                    }
                }
                else if (p.VType == MRubyVType.Class)
                {
                    if (!searchSuper) break;
                    superclassSeen = true;
                }
                p = p.Super;
            }

            var includeClass = m.CreateIncludeClass(insertPos.Super);
            insertPos.super = includeClass;
            insertPos = includeClass;
            m.SetFlag(MRubyObjectFlags.ClassInherited);

            SKIP:
            m = m.Super;
        }
        return true;
    }

    RClass CreateIncludeClass(RClass insertionClass)
    {
        var mod = VType == MRubyVType.IClass ? Class : this;
        mod = mod.AsOrigin();

        return new RClass(mod.VType == MRubyVType.IClass ? mod.Class : mod, MRubyVType.IClass)
        {
            Super = insertionClass,
            InstanceVType = MRubyVType.Class,
            MethodTable = mod.MethodTable,
            InstanceVariables = mod.InstanceVariables
        };
    }

    internal void SetSuper(RClass newSuper) => super = newSuper;
}
