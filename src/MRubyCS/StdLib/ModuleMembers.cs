using System;

namespace MRubyCS.StdLib;

[RubyClass("Module")]
static class ModuleMembers
{
    [RubyDef("(*Symbol | *String) -> self")]
    public static MRubyValue Public(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Public);
        return mod;
    }

    [RubyDef("(*Symbol | *String) -> self")]

    public static MRubyValue Private(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Private);
        return mod;
    }

    [RubyDef("(*Symbol | *String) -> self")]

    public static MRubyValue Protected(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Protected);
        return mod;
    }

    [RubyDef("(*Symbol | *String) -> Object")]

    public static MRubyValue TopPublic(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Public);
        return mrb.ObjectClass;
    }

    [RubyDef("(*Symbol | *String) -> Object")]

    public static MRubyValue TopPrivate(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Private);
        return mrb.ObjectClass;
    }

    [RubyDef("(*Symbol | *String) -> Object")]

    public static MRubyValue TopProtected(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Protected);
        return mrb.ObjectClass;
    }

    [RubyDef("() ?{ (Module) -> void } -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var block = state.GetBlockArgument();
        if (block != null)
        {
            state.YieldWithClass(mod, self, [self], block);
        }
        return self;
    }

    [RubyDef("(untyped) -> self")]
    public static MRubyValue ExtendObject(MRubyState state, MRubyValue self)
    {
        // state.EnsureValueType(self, MRubyVType.Module);
        var obj = state.GetArgumentAt(0);
        var target = state.SingletonClassOf(obj);
        state.IncludeModule(target, self.As<RClass>());
        return self;
    }

    [RubyDef("(Module) -> self")]
    public static MRubyValue PrependFeatures(MRubyState state, MRubyValue self)
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArgumentAt(0);
        state.PrependModule(c.As<RClass>(), self.As<RClass>());
        return self;
    }

    [RubyDef("(Module) -> self")]
    public static MRubyValue AppendFeatures(MRubyState state, MRubyValue self)
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArgumentAt(0);
        state.IncludeModule(c.As<RClass>(), self.As<RClass>());
        return self;
    }

    [RubyDef("(Module) -> bool")]
    public static MRubyValue QInclude(MRubyState state, MRubyValue self)
    {
        var c = self.As<RClass>();
        var mod2 = state.GetArgumentAt(0);
        state.EnsureValueType(mod2, MRubyVType.Module);

        while (c != null!)
        {
            if (c.VType == MRubyVType.IClass && c.Class == mod2.As<RClass>())
            {
                return MRubyValue.True;
            }

            c = c.Super;
        }

        return MRubyValue.False;
    }

    [RubyDef("(*untyped) ?{ (Module) -> untyped } -> untyped")]
    public static MRubyValue ClassEval(MRubyState state, MRubyValue self)
    {
        var block = state.GetBlockArgument(false);
        return state.EvalUnder(self, block!, self.As<RClass>());
    }

    [RubyDef("(*Symbol | *String) -> self")]
    public static MRubyValue ModuleFunction(MRubyState state, MRubyValue self)
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var argv = state.GetRestArgumentsAfter(0);
        if (argv.Length <= 0)
        {
            return self;
        }

        var mod = self.As<RClass>();

        foreach (var arg in argv)
        {
            state.EnsureValueType(arg, MRubyVType.Symbol);
            if (!state.TryFindMethod(mod, arg.SymbolValue, out var method, out _))
            {
                state.RaiseNameError(
                    arg.SymbolValue,
                    state.NewString($"undefined method '{state.NameOf(arg.SymbolValue)}' for class {state.NameOf(mod)}"));
            }

            state.DefineClassMethod(mod, arg.SymbolValue, method);
        }
        return self;
    }

    [RubyDef("(*Symbol | *String) -> nil")]
    public static MRubyValue AttrReader(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var argv = state.GetRestArgumentsAfter(0);
        foreach (var arg in argv)
        {
            var methodId = state.AsSymbol(arg);
            var name = state.PrepareInstanceVariableName(methodId);

            state.EnsureInstanceVariableName(name);

            state.DefineMethod(mod, methodId, MRubyMethod.CreateTrivialGetter((s, _) =>
            {
                var runtimeSelf = s.GetSelf();
                return state.GetInstanceVariable(runtimeSelf.Object!, name);
            }, name));
        }
        return MRubyValue.Nil;
    }

    [RubyDef("(*Symbol | *String) -> nil")]
    public static MRubyValue AttrWriter(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var argv = state.GetRestArgumentsAfter(0);
        foreach (var arg in argv)
        {
            var attrId = state.AsSymbol(arg);
            var variableName = state.PrepareInstanceVariableName(attrId);
            var setterName = state.PrepareName(attrId, default, "="u8);

            state.DefineMethod(mod, setterName, (s, _) =>
            {
                var runtimeSelf = s.GetSelf();
                var value = s.GetArgumentAt(0);
                state.SetInstanceVariable(runtimeSelf.Object!, variableName, value);
                return MRubyValue.Nil;
            });
        }
        return MRubyValue.Nil;
    }

    [RubyDef("(*Symbol | *String) -> nil")]
    public static MRubyValue AttrAccessor(MRubyState state, MRubyValue mod)
    {
        AttrReader(state, mod);
        return AttrWriter(state, mod);
    }

    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        if (mod.VType == MRubyVType.SClass)
        {
            var v = mod.InstanceVariables.Get(Names.AttachedKey);
            return v.VType.IsClass()
                ? state.NewString($"<Class:{state.InspectObject(v)}>")
                : state.NewString($"<Class:{state.StringifyAny(v)}>");
        }

        return state.NameOf(mod);
    }

    [RubyDef("(Symbol | String, Symbol | String) -> self")]
    public static MRubyValue AliasMethod(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var newName = state.GetArgumentAt(0).SymbolValue;
        var oldName = state.GetArgumentAt(1).SymbolValue;
        state.AliasMethod(mod, newName, oldName);
        state.MethodAddedHook(mod, newName);
        return self;
    }

    [RubyDef("(*Symbol | *String) -> self")]
    public static MRubyValue UndefMethod(MRubyState state, MRubyValue self)
    {
        var c = self.As<RClass>();
        var argv = state.GetRestArgumentsAfter(0);
        foreach (var arg in argv)
        {
            state.UndefMethod(c, arg.SymbolValue);
        }

        return self;
    }

    [RubyDef("() -> Array[Module]")]
    public static MRubyValue Ancestors(MRubyState state, MRubyValue self)
    {
        var c = self.As<RClass>();
        var result = state.NewArray();

        while (c != null!)
        {
            if (c.VType == MRubyVType.IClass)
            {
                result.Push(c.Class);
            }
            else if (!c.Flags.HasFlag(MRubyObjectFlags.ClassPrepended))
            {
                result.Push(c);
            }

            c = c.Super;
        }

        return result;
    }

    [RubyDef("(Symbol | String, ?bool) -> bool")]
    public static MRubyValue ConstDefined(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var id = state.GetArgumentAsSymbolAt(0);
        var inherit = state.GetArgumentAt(1);
        state.EnsureConstName(id);
        var result = inherit.Truthy
            ? state.ConstDefinedAt(id, mod)
            : state.ConstDefinedAt(id, mod, true);
        return result;
    }

    [RubyDef("(Symbol | String, ?bool) -> untyped")]
    public static MRubyValue ConstGet(MRubyState state, MRubyValue self)
    {
        if (self.VType is not (MRubyVType.Class or MRubyVType.Module or MRubyVType.SClass))
        {
            state.Raise(Names.TypeError, "constant look-up for non class/module"u8);
        }

        var mod = self.As<RClass>();
        var path = state.GetArgumentAt(0);
        if (path.IsSymbol)
        {
            return state.GetConst(path.SymbolValue, mod);
        }

        // const get with class path string
        state.EnsureValueType(path, MRubyVType.String);
        var pathString = path.As<RString>().AsSpan();
        MRubyValue result;
        while (true)
        {
            var end = pathString.IndexOf("::"u8);
            if (end < 0) end = pathString.Length;
            var id = state.Intern(pathString[..end]);
            result = state.GetConst(id, mod);

            if (end == pathString.Length)
            {
                break;
            }

            mod = result.As<RClass>();
            pathString = pathString[(end + 2)..];
        }

        return result;
    }

    [RubyDef("(Symbol | String, untyped) -> untyped")]
    public static MRubyValue ConstSet(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var id = state.GetArgumentAt(0).SymbolValue;
        var value = state.GetArgumentAt(1);
        state.DefineConst(mod, id, value);
        return value;
    }

    [RubyDef("(Symbol | String) -> untyped")]
    public static MRubyValue RemoveConst(MRubyState state, MRubyValue self)
    {
        var n = state.GetArgumentAt(0).SymbolValue;
        state.EnsureConstName(n);
        var removed = state.RemoveInstanceVariable(self.As<RObject>(), n);
        if (removed.IsUndef)
        {
            state.RaiseNameError(n, state.NewString($"constant {n} is not defined"));
        }

        return removed;
    }

    [RubyDef("(Symbol) -> untyped")]
    public static MRubyValue ConstMissing(MRubyState state, MRubyValue self)
    {
        var name = state.GetArgumentAsSymbolAt(0);
        state.RaiseConstMissing(self.As<RClass>(), name);
        return MRubyValue.Nil;
    }

    [RubyDef("(Symbol | String, ?bool) -> bool")]
    public static MRubyValue MethodDefined(MRubyState state, MRubyValue self)
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        return state.RespondTo(self.As<RClass>(), methodId);
    }

    [RubyDef("(Symbol | String, ?Proc) ?{ (*untyped) -> untyped } -> Symbol")]
    public static MRubyValue DefineMethod(MRubyState state, MRubyValue self)
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        var proc = state.GetArgumentAt(1);
        var block = state.GetBlockArgument();

        RProc? p;
        if (block != null)
        {
            p = block;
        }
        else
        {
            if (proc is { IsUndef: false, IsProc: false })
            {
                state.Raise(
                    Names.TypeError,
                    $"wrong argument type {state.Stringify(proc)} (expected Proc)");
            }
            p = proc.As<RProc>();
        }

        p = (RProc)p.Clone();
        p.SetFlag(MRubyObjectFlags.ProcStrict);
        var method = new MRubyMethod(p);

        var mod = self.As<RClass>();
        state.DefineMethod(mod, methodId, method);
        state.MethodAddedHook(mod, methodId);

        return methodId;
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eqq(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var other = state.GetArgumentAt(0);
        return state.KindOf(other, mod);
    }

    [RubyDef("() -> instance")]
    public static MRubyValue Dup(MRubyState state, MRubyValue self)
    {
        var clone = state.CloneObject(self);
        if (clone.Object is { } obj)
        {
            obj.UnFreeze();
        }
        return clone;
    }

    static void SetMethodVisibility(MRubyState mrb, RClass c, MRubyMethodVisibility visibility)
    {
        var args = mrb.GetRestArgumentsAfter(0);
        if (args.Length <= 0)
        {
            ref var callInfo = ref mrb.Context.FindClosestVisibilityScope(null, 1, out var env);
            if (env != null)
            {
                env.Visibility = visibility;
            }
            else
            {
                callInfo.Visibility = visibility;
            }
        }
        else
        {
            foreach (var arg in args)
            {
                mrb.EnsureValueType(arg, MRubyVType.Symbol);
                var methodId = arg.SymbolValue;
                c.TryFindMethod(methodId, out var method, out _);
                c.MethodTable[methodId] = method.WithVisibility(visibility);
            }
        }
    }
}
