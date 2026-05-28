using System;

namespace ChibiRuby.StdLib;

/// <summary>
/// A named bundle of methods and constants that can be mixed into classes via
/// <c>include</c>, <c>prepend</c>, or <c>extend</c>. <c>Class</c> is itself a
/// subclass of <c>Module</c>, so most reflective and metaprogramming methods
/// (e.g. <c>define_method</c>, <c>const_set</c>, <c>ancestors</c>) live here.
/// </summary>
[RubyClass("Module")]
static class ModuleMembers
{
    /// <summary>
    /// Sets the default method visibility to public, or makes the named methods public.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def bar; end
    ///   public :bar
    /// end
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> self")]
    public static MRubyValue Public(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Public);
        return mod;
    }

    /// <summary>
    /// Sets the default method visibility to private, or makes the named methods private.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def secret; end
    ///   private :secret
    /// end
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> self")]

    public static MRubyValue Private(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Private);
        return mod;
    }

    /// <summary>
    /// Sets the default method visibility to protected, or makes the named methods protected.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def helper; end
    ///   protected :helper
    /// end
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> self")]

    public static MRubyValue Protected(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Protected);
        return mod;
    }

    /// <summary>
    /// At the top level, sets the default method visibility on <c>Object</c> to public,
    /// or makes the named top-level methods public.
    /// </summary>
    /// <example>
    /// <code>
    /// def my_method; end
    /// public :my_method
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> Object")]

    public static MRubyValue TopPublic(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Public);
        return mrb.ObjectClass;
    }

    /// <summary>
    /// At the top level, sets the default method visibility on <c>Object</c> to private,
    /// or makes the named top-level methods private.
    /// </summary>
    /// <example>
    /// <code>
    /// def my_helper; end
    /// private :my_helper
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> Object")]

    public static MRubyValue TopPrivate(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Private);
        return mrb.ObjectClass;
    }

    /// <summary>
    /// At the top level, sets the default method visibility on <c>Object</c> to protected,
    /// or makes the named top-level methods protected.
    /// </summary>
    /// <example>
    /// <code>
    /// def my_protected; end
    /// protected :my_protected
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> Object")]

    public static MRubyValue TopProtected(MRubyState mrb, MRubyValue mod)
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Protected);
        return mrb.ObjectClass;
    }

    /// <summary>
    /// Initializes a newly-created module. If a block is given, it is evaluated in the module context.
    /// </summary>
    /// <example>
    /// <code>
    /// m = Module.new do
    ///   def hi; "hi"; end
    /// end
    /// </code>
    /// </example>
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

    /// <summary>
    /// Adds the methods of <c>self</c> as singleton methods of the given object. Called by <c>Object#extend</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// module M; def hi; "hi"; end; end
    /// obj = Object.new
    /// M.extend_object(obj)
    /// obj.hi          # => "hi"
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> self")]
    public static MRubyValue ExtendObject(MRubyState state, MRubyValue self)
    {
        // state.EnsureValueType(self, MRubyVType.Module);
        var obj = state.GetArgumentAt(0);
        var target = state.SingletonClassOf(obj);
        state.IncludeModule(target, self.As<RClass>());
        return self;
    }

    /// <summary>
    /// Prepends <c>self</c> as an ancestor of the given class/module. Invoked by <c>prepend</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// module M; end
    /// class C; prepend M; end
    /// C.ancestors    # => [M, C, Object, ...]
    /// </code>
    /// </example>
    [RubyDef("(Module) -> self")]
    public static MRubyValue PrependFeatures(MRubyState state, MRubyValue self)
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArgumentAt(0);
        state.PrependModule(c.As<RClass>(), self.As<RClass>());
        return self;
    }

    /// <summary>
    /// Includes <c>self</c> as an ancestor of the given class/module. Invoked by <c>include</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// module M; end
    /// class C; include M; end
    /// C.ancestors    # => [C, M, Object, ...]
    /// </code>
    /// </example>
    [RubyDef("(Module) -> self")]
    public static MRubyValue AppendFeatures(MRubyState state, MRubyValue self)
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArgumentAt(0);
        state.IncludeModule(c.As<RClass>(), self.As<RClass>());
        return self;
    }

    /// <summary>
    /// Returns <c>true</c> if the given module is included in <c>self</c> or one of its ancestors.
    /// </summary>
    /// <example>
    /// <code>
    /// module M; end
    /// class C; include M; end
    /// C.include?(M)    # => true
    /// </code>
    /// </example>
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

    /// <summary>
    /// Evaluates the given block in the context of <c>self</c> as a class/module.
    /// Inside the block, <c>self</c> is the class/module, so <c>def</c> defines an instance method.
    /// </summary>
    /// <example>
    /// <code>
    /// String.class_eval do
    ///   def shout; upcase + "!"; end
    /// end
    /// "hi".shout      # => "HI!"
    /// </code>
    /// </example>
    [RubyDef("(*untyped) ?{ (Module) -> untyped } -> untyped")]
    public static MRubyValue ClassEval(MRubyState state, MRubyValue self)
    {
        var block = state.GetBlockArgument(false);
        return state.EvalUnder(self, block!, self.As<RClass>());
    }

    /// <summary>
    /// Makes the given module methods callable both as private instance methods and as module-level methods.
    /// </summary>
    /// <example>
    /// <code>
    /// module M
    ///   def hi; "hi"; end
    ///   module_function :hi
    /// end
    /// M.hi      # => "hi"
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> self")]
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

    /// <summary>
    /// Defines reader methods for the given instance variable names.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   attr_reader :name
    ///   def initialize(name); @name = name; end
    /// end
    /// Foo.new("Ada").name    # => "Ada"
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> nil")]
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

    /// <summary>
    /// Defines writer (<c>name=</c>) methods for the given instance variable names.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   attr_writer :name
    /// end
    /// f = Foo.new
    /// f.name = "Ada"     # => "Ada"
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> nil")]
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

    /// <summary>
    /// Defines reader and writer methods for the given instance variable names.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   attr_accessor :name
    /// end
    /// f = Foo.new
    /// f.name = "Ada"
    /// f.name             # => "Ada"
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> nil")]
    public static MRubyValue AttrAccessor(MRubyState state, MRubyValue mod)
    {
        AttrReader(state, mod);
        return AttrWriter(state, mod);
    }

    /// <summary>
    /// Returns the name of <c>self</c> as a string, or a synthetic name for anonymous modules
    /// and singleton classes.
    /// </summary>
    /// <example>
    /// <code>
    /// String.to_s         # => "String"
    /// Module.new.to_s     # => something like "#&lt;Module:0x...&gt;"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Creates an alias <c>new_name</c> for the existing method <c>old_name</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def hi; "hi"; end
    ///   alias_method :greet, :hi
    /// end
    /// Foo.new.greet     # => "hi"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Removes the named methods from <c>self</c>, so calls raise <c>NoMethodError</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def hi; end
    ///   undef_method :hi
    /// end
    /// </code>
    /// </example>
    [RubyDef("(*(Symbol | String)) -> self")]
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

    /// <summary>
    /// Returns the list of modules included in the ancestor chain of <c>self</c>, starting with <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Integer.ancestors    # => [Integer, Numeric, Comparable, Object, Kernel, BasicObject]
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns <c>true</c> if the constant <c>name</c> is defined in <c>self</c>.
    /// Pass <c>false</c> as the second argument to skip the ancestor chain.
    /// </summary>
    /// <example>
    /// <code>
    /// Object.const_defined?(:String)    # => true
    /// Object.const_defined?(:Nope)      # => false
    /// </code>
    /// </example>
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

    /// <summary>
    /// Returns the value of the named constant in <c>self</c>. A string path like
    /// <c>"A::B"</c> traverses nested constants.
    /// </summary>
    /// <example>
    /// <code>
    /// Object.const_get(:String)        # => String
    /// Object.const_get("Process::PID") rescue NameError
    /// </code>
    /// </example>
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

    /// <summary>
    /// Defines (or replaces) the constant <c>name</c> on <c>self</c> with the given value.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo; end
    /// Foo.const_set(:BAR, 42)
    /// Foo::BAR        # => 42
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String, untyped) -> untyped")]
    public static MRubyValue ConstSet(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var id = state.GetArgumentAt(0).SymbolValue;
        var value = state.GetArgumentAt(1);
        state.DefineConst(mod, id, value);
        return value;
    }

    /// <summary>
    /// Removes the named constant from <c>self</c> and returns its former value.
    /// Raises <c>NameError</c> if the constant is not defined.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo; X = 1; end
    /// Foo.send(:remove_const, :X)   # => 1
    /// </code>
    /// </example>
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

    /// <summary>
    /// Invoked when a reference is made to an undefined constant. The default implementation raises <c>NameError</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def self.const_missing(name); :default; end
    /// end
    /// Foo::Anything    # => :default
    /// </code>
    /// </example>
    [RubyDef("(Symbol) -> untyped")]
    public static MRubyValue ConstMissing(MRubyState state, MRubyValue self)
    {
        var name = state.GetArgumentAsSymbolAt(0);
        state.RaiseConstMissing(self.As<RClass>(), name);
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns <c>true</c> if the named instance method is defined on <c>self</c> or its ancestors.
    /// </summary>
    /// <example>
    /// <code>
    /// String.method_defined?(:upcase)    # => true
    /// String.method_defined?(:nope)      # => false
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String, ?bool) -> bool")]
    public static MRubyValue MethodDefined(MRubyState state, MRubyValue self)
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        return state.RespondTo(self.As<RClass>(), methodId);
    }

    /// <summary>
    /// Defines an instance method <c>name</c> on <c>self</c> using either a <c>Proc</c>
    /// argument or the given block. Returns the method name as a symbol.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   define_method(:hi) { "hi" }
    /// end
    /// Foo.new.hi      # => "hi"
    /// </code>
    /// </example>
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

    /// <summary>
    /// Case-equality (<c>===</c>) for modules and classes: returns <c>true</c> if the argument
    /// is an instance of <c>self</c> or one of its descendants. Used by <c>case</c>/<c>when</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Integer === 1       # => true
    /// String  === "hi"    # => true
    /// String  === 1       # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eqq(MRubyState state, MRubyValue self)
    {
        var mod = self.As<RClass>();
        var other = state.GetArgumentAt(0);
        return state.KindOf(other, mod);
    }

    /// <summary>
    /// Returns a shallow copy of <c>self</c>. The copy is not frozen.
    /// </summary>
    /// <example>
    /// <code>
    /// m = Module.new
    /// m2 = m.dup
    /// m2.equal?(m)    # => false
    /// </code>
    /// </example>
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
