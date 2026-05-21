using System;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public sealed class ClassDefineOptions(MRubyState state, RClass c)
{
    public void DefineConst(Symbol name, MRubyValue value) => state.DefineConst(c, name, value);

    public MRubyValue GetInstanceVariable(Symbol name) => state.GetInstanceVariable(c, name);
    public void SetInstanceVariable(Symbol name, MRubyValue value) => state.SetInstanceVariable(c, name, value);
    public MRubyValue RemoveInstanceVariable(Symbol name) => state.RemoveInstanceVariable(c, name);

    public MRubyValue GetClassVariable(Symbol name) => state.GetClassVariable(c, name);
    public void SetClassVariable(Symbol name, MRubyValue value) => state.SetClassVariable(c, name, value);

    public RClass DefineClass(Symbol name, RClass super) =>
        state.DefineClass(name, super, outer: c);

    public RClass DefineClass(Symbol name, Action<ClassDefineOptions> configure) =>
        state.DefineClass(name, state.ObjectClass, outer: c, configure);

    public RClass DefineModule(Symbol name) => state.DefineModule(name, c);
    public RClass DefineModule(Symbol name, Action<ClassDefineOptions> configure) => state.DefineModule(name, c, configure);

    public void DefineMethod(Symbol name, MRubyMethod method) => state.DefineMethod(c, name, method);
    public void DefineMethod(Symbol name, MRubyFunc func) => state.DefineMethod(c, name, func);

    public void DefineClassMethod(Symbol name, MRubyMethod method) => state.DefineClassMethod(c, name, method);
    public void DefineClassMethod(Symbol name, MRubyFunc func) => state.DefineClassMethod(c, name, func);
}

partial class MRubyState
{
    const int MethodCacheSize = 1 << 8;

    struct MethodCacheEntry
    {
        public RClass? Class;
        public Symbol MethodId;
        public MRubyMethod Method;
        public RClass? DefiningClass;
    }

    public RClass SingletonClassOf(MRubyValue value)
    {
        switch (value.VType)
        {
            case MRubyVType.Nil:
                return NilClass;
            case MRubyVType.False:
                return FalseClass;
            case MRubyVType.True:
                return TrueClass;
            case MRubyVType.Symbol:
            case MRubyVType.Integer:
            case MRubyVType.Float:
                Raise(Names.TypeError, "can't define singleton"u8);
                return null!; // not reached
            default:
                var obj = value.As<RObject>();
                if (obj.Class == null!)
                {
                    Raise(Names.TypeError, "can't define singleton"u8);
                    return null!; // not reached
                }
                PrepareSingletonClass(obj);
                return obj.Class;
        }
    }

    public void IncludeModule(RClass c, RClass mod)
    {
        EnsureNotFrozen(c);
        if (!c.TryIncludeModule(c.AsOrigin(), mod, true))
        {
            Raise(Names.ArgumentError, "cyclic include detected"u8);
        }

        // TODO: https://github.com/mruby/mruby/commit/3972df57fe70a29e6bf6db590dd22651640a1217
        ClearMethodCache();
    }

    public void PrependModule(RClass c, RClass mod)
    {
        EnsureNotFrozen(c);
        if (!c.HasFlag(MRubyObjectFlags.ClassPrepended))
        {
            var origin = new RClass(c, MRubyVType.IClass)
            {
                Super = c.Super,
                InstanceVType = MRubyVType.Undef,
            };
            origin.SetFlag(MRubyObjectFlags.ClassOrigin | MRubyObjectFlags.ClassInherited);
            c.SetSuper(origin);
            c.MoveMethodTableTo(origin);
            c.ShareInstanceVariablesTo(origin);
            c.SetFlag(MRubyObjectFlags.ClassPrepended);
        }

        if (!c.TryIncludeModule(c, mod, false))
        {
            Raise(Names.ArgumentError, "cyclic prepend detected"u8);
        }

        // TODO: https://github.com/mruby/mruby/commit/3972df57fe70a29e6bf6db590dd22651640a1217

        ClearMethodCache();
    }

    public void DefineConst(Symbol name, MRubyValue value) => DefineConst(ObjectClass, name, value);

    public void DefineConst(RClass c, Symbol name, MRubyValue value)
    {
        EnsureNotFrozen(c);
        EnsureConstName(name);
        if (value.IsNamespace)
        {
            TrySetClassPathLink(c, value.As<RClass>(), name);
        }
        c.InstanceVariables.Set(name, value);
    }

    public RClass DefineClass(Symbol name, RClass super, MRubyVType? instanceVType = null, RClass? outer = null)
    {
        outer ??= ObjectClass;

        RClass c;
        if (super.VType != MRubyVType.Class)
        {
            Raise(Names.TypeError, $"super class must be a Class ({super.VType} given)");
        }

        if (TryGetConst(name, outer, out var value))
        {
            EnsureInheritable(super);
            EnsureValueType(value, MRubyVType.Class);

            c = value.As<RClass>();
            if (c.Super.GetRealClass() != super)
            {
                Raise(Names.TypeError, $"superclass mismatch for Class {NameOf(name)} ({StringifyModule(c.Super)} not {StringifyModule(super)})");
            }
            return c;
        }

        c = new RClass(ClassClass)
        {
            Super = super,
            InstanceVType = instanceVType ?? super.InstanceVType
        };
        TrySetClassPathLink(outer, c, name);
        PrepareSingletonClass(c);
        return c;
    }

    public RClass DefineClass(Symbol name, RClass super, Action<ClassDefineOptions> configure)
    {
        return DefineClass(name, super, null!, configure);
    }

    public RClass DefineClass(Symbol name, RClass super, RClass outer, Action<ClassDefineOptions> configure)
    {
        var c = DefineClass(name, super, outer: outer);
        configure(new ClassDefineOptions(this, c));
        return c;
    }

    public RClass DefineModule(Symbol name, RClass outer, Action<ClassDefineOptions> configure)
    {
        var module = DefineModule(name, outer);
        configure(new ClassDefineOptions(this, module));
        return module;
    }

    public RClass DefineModule(Symbol name, Action<ClassDefineOptions> configure)
    {
        var module = DefineModule(name, ObjectClass);
        configure(new ClassDefineOptions(this, module));
        return module;
    }

    public RClass DefineModule(Symbol name, RClass outer)
    {
        if (TryGetConst(name, outer, out var value))
        {
            EnsureValueType(value, MRubyVType.Module);
            return value.As<RClass>();
        }

        var m = new RClass(ModuleClass, MRubyVType.Module)
        {
            InstanceVType = MRubyVType.Undef,
            Super = null!
        };
        TrySetClassPathLink(outer, m, name);
        return m;
    }

    public void DefineMethod(RClass c, Symbol id, MRubyFunc func) =>
        DefineMethod(c, id, new MRubyMethod(func));

    public void DefineMethod(RClass c, Symbol id, MRubyMethod method)
    {
        c = c.AsOrigin();
        if (c is { IsSingletonClass: true, IsFrozen: true })
        {
            var instance = c.InstanceVariables.Get(Names.AttachedKey);
            EnsureNotFrozen(instance);
        }
        else
        {
            EnsureNotFrozen(c);
        }

        if (method.Proc is { } proc)
        {
            proc.SetFlag(MRubyObjectFlags.ProcScope);
            if (proc.Scope is not REnv)
            {
                proc.UpdateScope(c);
            }
        }

        if (id == Names.Initialize ||
            id == Names.InitializeCopy ||
            id == Names.QRespondToMissing)
        {
            method = method.WithVisibility(MRubyMethodVisibility.Private);
        }
        else if (method.Visibility == MRubyMethodVisibility.Default)
        {
            ref var callInfo = ref Context.FindClosestVisibilityScope(c, 0, out var env);
            var visibility = env?.Visibility ?? callInfo.Visibility;
            if (method.Visibility != visibility)
            {
                method = method.WithVisibility(visibility);
            }
        }

        c.MethodTable[id] = method;
        ClearMethodCache();
    }

    public void DefinePrivateMethod(RClass c, Symbol id, MRubyMethod method)
    {
        DefineMethod(c, id, method.WithVisibility(MRubyMethodVisibility.Private));
    }

    public void DefinePrivateMethod(RClass c, Symbol id, MRubyFunc func) =>
        DefinePrivateMethod(c, id, new MRubyMethod(func));

    public void AliasMethod(RClass c, Symbol aliasMethodId, Symbol methodId)
    {
        if (aliasMethodId == methodId) return;
        if (!TryFindMethod(c, methodId, out var method, out _))
        {
            RaiseNameError(methodId, NewString($"undefined method '{NameOf(methodId)}' for class '{NameOf(c)}"));
        }
        DefineMethod(c, aliasMethodId, method);
    }

    public void UndefMethod(RClass c, Symbol methodId)
    {
        if (!TryFindMethod(c, methodId, out _, out _))
        {
            Raise(Names.NameError, $"undefined method '{NameOf(methodId)}' for class '{NameOf(c)}'");
        }
        DefineMethod(c, methodId, MRubyMethod.Undef);
    }

    public void DefineClassMethod(RClass c, Symbol methodId, MRubyMethod method)
    {
        DefineSingletonMethod(c, methodId, method);
    }

    public void DefineClassMethod(RClass c, Symbol methodId, MRubyFunc func)
    {
        DefineSingletonMethod(c, methodId, new MRubyMethod(func));
    }

    public void UndefClassMethod(RClass c, Symbol methodId)
    {
        UndefMethod(SingletonClassOf(c)!, methodId);
    }

    public bool RespondTo(MRubyValue self, Symbol methodId)
    {
        return RespondTo(ClassOf(self), methodId);
    }

    public bool RespondTo(RClass c, Symbol methodId)
    {
        return TryFindMethod(c, methodId, out var method, out _) && method != MRubyMethod.Undef;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFindMethod(RClass c, Symbol methodId, out MRubyMethod method, out RClass imp)
    {
        var index = unchecked((uint)RuntimeHelpers.GetHashCode(c) ^ methodId.Value) & (MethodCacheSize - 1);
        ref var entry = ref methodCache[index];
        if (entry.Class == c && entry.MethodId == methodId)
        {
            method = entry.Method;
            imp = entry.DefiningClass!;
            return true;
        }
        if (c.TryFindMethod(methodId, out method, out imp))
        {
            entry.Class = c;
            entry.MethodId = methodId;
            entry.Method = method;
            entry.DefiningClass = imp;
            return true;
        }
        return false;
    }

    void ClearMethodCache()
    {
        Array.Clear(methodCache, 0, methodCache.Length);
    }

    void PrepareSingletonClass(RObject obj)
    {
        if (obj.Class.VType == MRubyVType.SClass) return;

        RClass singletonClass;
        if (obj is RClass { VType: MRubyVType.Class } objAsClass)
        {
            singletonClass = new RClass(ClassClass, MRubyVType.SClass)
            {
                Super = objAsClass.Super == ObjectClass || objAsClass.Super == null!
                    ? ClassClass
                    : objAsClass.Super.Class,
                InstanceVType = MRubyVType.Undef,
            };
        }
        else if (obj is RClass { VType: MRubyVType.SClass } objAsSClass)
        {
            var c = objAsSClass;
            while (c.Super.VType == MRubyVType.IClass)
            {
                c = c.Super;
            }
            PrepareSingletonClass(c.Super);
            singletonClass = new RClass(ClassClass, MRubyVType.SClass)
            {
                Super = c.Super.Class,
                InstanceVType = MRubyVType.Undef,
            };
        }
        else
        {
            singletonClass = new RClass(ClassClass, MRubyVType.SClass)
            {
                Super = obj.Class,
                InstanceVType = MRubyVType.Undef,
            };
            PrepareSingletonClass(singletonClass);
        }
        singletonClass.InstanceVariables.Set(Names.AttachedKey, obj);
        if (obj.IsFrozen)
        {
            singletonClass.MarkAsFrozen();
        }
        obj.Class = singletonClass;
    }

    public void ClassInheritedHook(RClass superClass, RClass newClass)
    {
        superClass.SetFlag(MRubyObjectFlags.ClassInherited);
        if (RespondTo(superClass.Class, Names.Inherited))
        {
            Send(superClass, Names.Inherited, newClass);
        }
    }

    public void MethodAddedHook(RClass c, Symbol methodId)
    {
        Symbol added;
        if (c.VType == MRubyVType.SClass)
        {
            added = Names.SingletonMethodAdded;
            var receiver = c.InstanceVariables.Get(Names.AttachedKey);
            if (RespondTo(receiver, added))
            {
                Send(receiver, added, methodId);
            }
        }
        else
        {
            added = Names.MethodAdded;
            if (RespondTo(c.Class, added))
            {
                Send(c, added, methodId);
            }

        }
    }

    internal Symbol PrepareName(Symbol symbol, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> suffix)
    {
        var name = symbolTable.NameOf(symbol);
        var length = name.Length + prefix.Length + suffix.Length;
        var offset = 0;
        Span<byte> buffer = stackalloc byte[length];
        if (!prefix.IsEmpty)
        {
            prefix.CopyTo(buffer);
            offset += prefix.Length;
        }
        name.CopyTo(buffer[offset..]);
        offset += name.Length;
        if (!suffix.IsEmpty)
        {
            suffix.CopyTo(buffer[offset..]);
        }

        return Intern(buffer);
    }

    internal Symbol PrepareInstanceVariableName(Symbol symbol)
    {
        symbol = PrepareName(symbol, "@"u8, default);
        EnsureInstanceVariableName(symbol);
        return symbol;
    }

    void DefineSingletonMethod(RObject o, Symbol methodId, MRubyMethod method)
    {
        PrepareSingletonClass(o);
        DefineMethod(o.Class, methodId, method);
    }

    void DefineSingletonMethod(RObject o, Symbol methodId, MRubyFunc func) =>
        DefineSingletonMethod(o, methodId, new MRubyMethod(func));

    RClass CloneSingletonClass(MRubyValue obj)
    {
        var klass = obj.As<RObject>().Class;
        if (klass.VType != MRubyVType.SClass) return klass;

        // copy singleton(unnamed) class
        var clone = new RClass(ClassClass, MRubyVType.SClass)
        {
            Super = klass.Super,
            InstanceVType = klass.InstanceVType,
        };

        if (obj.VType is not (MRubyVType.Class or MRubyVType.SClass))
        {
            clone.Class = CloneSingletonClass(klass);
        }

        // copy instance variables
        clone.InstanceVariables.Clear();
        klass.InstanceVariables.CopyTo(clone.InstanceVariables);
        clone.InstanceVariables.Set(Names.AttachedKey, obj);

        // copy method table
        clone.MethodTable.Clear();
        klass.MethodTable.CopyTo(clone.MethodTable);

        return clone;
    }
}

