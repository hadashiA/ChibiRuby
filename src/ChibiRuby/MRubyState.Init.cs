using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using ChibiRuby.Internals;
using ChibiRuby.StdLib;
using Utf8StringInterpolation;

namespace ChibiRuby;

public partial class MRubyState : IDisposable
{
    public static MRubyState Create(Action<MRubyState> configure)
    {
        var state = Create();
        configure(state);
        return state;
    }

    public static MRubyState Create()
    {
        var state = new MRubyState();

        // built-in
        state.InitClass();
        state.InitObject();
        state.InitKernel();
        state.InitSymbol();
        state.InitString();
        state.InitProc();
        state.InitBinding();
        state.InitException();
        state.InitNumeric();
        state.InitArray();
        state.InitHash();
        state.InitRange();
        state.InitEnumerable();
        state.InitFiber();
        state.InitThread();
        state.InitMrbLib();
        state.InitObjectExt();
        state.InitTime();
        state.InitRandom();

        // IO / File are opt-in: call `state.DefineIO()` to enable them.

        return state;
    }

    public RClass BasicObjectClass { get; private set; } = default!;
    public RClass ObjectClass { get; private set; } = default!;
    public RClass ClassClass { get; private set; } = default!;
    public RClass ModuleClass { get; private set; } = default!;
    public RClass ProcClass { get; private set; } = default!;
    public RClass StringClass { get; private set; } = default!;
    public RClass ArrayClass { get; private set; } = default!;
    public RClass HashClass { get; private set; } = default!;
    public RClass RangeClass { get; private set; } = default!;
    public RClass FloatClass { get; private set; } = default!;
    public RClass IntegerClass { get; private set; } = default!;
    public RClass TrueClass { get; private set; } = default!;
    public RClass FalseClass { get; private set; } = default!;
    public RClass NilClass { get; private set; } = default!;
    public RClass SymbolClass { get; private set; } = default!;
    public RClass FiberClass { get; private set; } = default!;
    public RClass IOClass { get; private set; } = default!;
    public RClass FileClass { get; private set; } = default!;
    public RClass KernelModule { get; private set; } = default!;
    public RClass ExceptionClass { get; private set; } = default!;
    public RClass StandardErrorClass { get; private set; } = default!;

    public RObject TopSelf { get; private set; } = default!;
    public MRubyLongJumpException? Exception { get; private set; }

    public MRubyValueEqualityComparer ValueEqualityComparer { get; }
    public MRubyValueHashKeyEqualityComparer HashKeyEqualityComparer { get; }

    internal MRubyContext Context { get; private set; }
    internal MRubyContext ContextRoot { get; }= new();

    public RiteParser RiteParser => riteParser ??= new RiteParser(this);

    readonly SymbolTable symbolTable = new();
    readonly VariableTable globalVariables = new();

    RiteParser? riteParser;

    readonly MethodCacheEntry[] methodCache = new MethodCacheEntry[MethodCacheSize];

    MRubyState()
    {
        Context = ContextRoot;
        ValueEqualityComparer = new MRubyValueEqualityComparer(this);
        HashKeyEqualityComparer = new MRubyValueHashKeyEqualityComparer(this);
    }

    bool disposed;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Core dispose path. Override in derived types to release additional
    /// resources, calling <c>base.Dispose(disposing)</c>.
    /// <paramref name="disposing"/> is <c>true</c> when invoked from
    /// <see cref="Dispose()"/> (safe to touch other managed objects) and
    /// <c>false</c> when invoked from the finalizer (touch only unmanaged
    /// state).
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        if (disposing)
        {
            FiberScheduler?.Dispose();
            FiberScheduler = null;
        }

        disposed = true;
    }

    ~MRubyState()
    {
        Dispose(disposing: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(ReadOnlySpan<byte> name) => symbolTable.Intern(name);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(RString name) => symbolTable.Intern(name.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
        format.Flush();
        return symbolTable.Intern(format.GetBufferWriter().WrittenSpan);
    }

    public Symbol Intern(string name)
    {
        return symbolTable.Intern(name);
    }

    void InitClass()
    {
        BasicObjectClass = new RClass(default!)
        {
            InstanceVType = MRubyVType.Undef,
            Super = default!, // sentinel. only for BasicObject
        };
        ObjectClass = new RClass(default!)
        {
            InstanceVType = MRubyVType.Object,
            Super = BasicObjectClass,
        };
        ModuleClass = new RClass(default!)
        {
            InstanceVType = MRubyVType.Module,
            Super = ObjectClass,
        };
        ClassClass = new RClass(default!)   // sentinel. only for ClassClass
        {
            InstanceVType = MRubyVType.Class,
            Super = ModuleClass,
        };

        BasicObjectClass.Class = ClassClass;
        ObjectClass.Class = ClassClass;
        ModuleClass.Class = ClassClass;
        ClassClass.Class = ClassClass;

        // Prepare singleton class
        PrepareSingletonClass(BasicObjectClass);
        PrepareSingletonClass(ObjectClass);
        PrepareSingletonClass(ModuleClass);
        PrepareSingletonClass(ClassClass);

        // name basic classes
        DefineConst(Names.BasicObjectClass, BasicObjectClass);
        DefineConst(Names.ObjectClass, ObjectClass);
        DefineConst(Names.ModuleClass, ModuleClass);
        DefineConst(Names.ClassClass, ClassClass);

        BasicObjectClass.InstanceVariables.Set(Names.ClassNameKey, NewString("BasicObject"u8));
        ObjectClass.InstanceVariables.Set(Names.ClassNameKey, NewString("Object"u8));
        ModuleClass.InstanceVariables.Set(Names.ClassNameKey, NewString("Module"u8));
        ClassClass.InstanceVariables.Set(Names.ClassNameKey, NewString("Class"u8));

        DefineMethod(BasicObjectClass, Names.Initialize, MRubyMethod.Nop);
        DefineMethod(BasicObjectClass, Names.OpNot, BasicObjectMembers.Not);
        DefineMethod(BasicObjectClass, Names.OpEq, BasicObjectMembers.OpEq);
        DefineMethod(BasicObjectClass, Intern("__id__"u8), BasicObjectMembers.Id);
        DefineMethod(BasicObjectClass, Intern("__send__"u8), BasicObjectMembers.Send);
        DefineMethod(BasicObjectClass, Names.QEqual, BasicObjectMembers.OpEq);
        DefineMethod(BasicObjectClass, Names.InstanceEval, BasicObjectMembers.InstanceEval);
        DefineMethod(BasicObjectClass, Names.SingletonMethodAdded, MRubyMethod.Nop);
        DefineMethod(BasicObjectClass, Names.MethodMissing, BasicObjectMembers.MethodMissing);

        DefineSingletonMethod(ClassClass, Names.New, ClassMembers.NewClass);
        DefineMethod(ClassClass, Names.New, ClassMembers.New);
        DefineMethod(ClassClass, Names.Initialize, ClassMembers.Initialize);
        DefineMethod(ClassClass, Intern("superclass"u8), ClassMembers.Superclass);
        DefineMethod(ClassClass, Intern("inherited"u8), MRubyMethod.Nop);

        DefineMethod(ModuleClass, Intern("extend_object"u8), ModuleMembers.ExtendObject);
        DefineMethod(ModuleClass, Intern("extended"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("prepended"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("prepend_features"u8), ModuleMembers.PrependFeatures);
        DefineMethod(ModuleClass, Intern("include?"u8), ModuleMembers.QInclude);
        DefineMethod(ModuleClass, Intern("append_features"u8), ModuleMembers.AppendFeatures);
        DefineMethod(ModuleClass, Intern("class_eval"u8), ModuleMembers.ClassEval);
        DefineMethod(ModuleClass, Intern("module_eval"u8), ModuleMembers.ClassEval);
        DefineMethod(ModuleClass, Intern("included"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Names.Initialize, ModuleMembers.Initialize);
        DefineMethod(ModuleClass, Intern("module_function"u8), ModuleMembers.ModuleFunction);
        DefineMethod(ModuleClass, Intern("attr_reader"u8), ModuleMembers.AttrReader);
        DefineMethod(ModuleClass, Intern("attr_writer"u8), ModuleMembers.AttrWriter);
        DefineMethod(ModuleClass, Intern("attr_accessor"u8), ModuleMembers.AttrAccessor);
        DefineMethod(ModuleClass, Names.ToS, ModuleMembers.ToS);
        DefineMethod(ModuleClass, Names.Inspect, ModuleMembers.ToS);
        DefineMethod(ModuleClass, Intern("alias_method"u8), ModuleMembers.AliasMethod);
        DefineMethod(ModuleClass, Intern("ancestors"u8), ModuleMembers.Ancestors);
        DefineMethod(ModuleClass, Intern("undef_method"u8), ModuleMembers.UndefMethod);
        DefineMethod(ModuleClass, Intern("const_defined?"u8), ModuleMembers.ConstDefined);
        DefineMethod(ModuleClass, Intern("const_get"u8), ModuleMembers.ConstGet);
        DefineMethod(ModuleClass, Intern("const_set"u8), ModuleMembers.ConstSet);
        DefineMethod(ModuleClass, Intern("remove_const"u8), ModuleMembers.RemoveConst);
        DefineMethod(ModuleClass, Intern("const_missing"u8), ModuleMembers.ConstMissing);
        DefineMethod(ModuleClass, Intern("method_defined?"u8), ModuleMembers.MethodDefined);
        DefineMethod(ModuleClass, Intern("define_method"u8), ModuleMembers.DefineMethod);
        DefineMethod(ModuleClass, Names.OpEqq, ModuleMembers.Eqq);
        DefineMethod(ModuleClass, Names.Dup, ModuleMembers.Dup);
        DefineMethod(ModuleClass, Names.MethodAdded, MRubyMethod.Nop);

        DefinePrivateMethod(ModuleClass, Intern("public"u8), ModuleMembers.Public);
        DefinePrivateMethod(ModuleClass, Intern("private"u8), ModuleMembers.Private);
        DefinePrivateMethod(ModuleClass, Intern("protected"u8), ModuleMembers.Protected);

        UndefMethod(ClassClass, Intern("append_features"u8));
        UndefMethod(ClassClass, Intern("prepend_features"u8));
        UndefMethod(ClassClass, Intern("extend_object"u8));
        UndefMethod(ClassClass, Intern("module_function"u8));

        TopSelf = new RObject(MRubyVType.Object, ObjectClass);
        // DefineSingletonMethod(TopSelf, Names.Inspect, MainMembers.Inspect);
        // DefineSingletonMethod(TopSelf, Names.ToS, MainMembers.ToS);
        // DefineSingletonMethod(TopSelf, Intern("define_method"u8), MainMembers.DefineMethod);

        DefineSingletonMethod(TopSelf, Intern("public"u8), ModuleMembers.TopPublic);
        DefineSingletonMethod(TopSelf, Intern("private"u8), ModuleMembers.TopPrivate);
        DefineSingletonMethod(TopSelf, Intern("protected"u8), ModuleMembers.TopProtected);
    }

    void InitObject()
    {
        NilClass = DefineClass(Intern("NilClass"u8), ObjectClass, MRubyVType.False);
        UndefClassMethod(NilClass, Names.New);
        DefineMethod(NilClass, Names.OpAnd, FalseClassMembers.And);
        DefineMethod(NilClass, Names.OpOr, FalseClassMembers.Or);
        DefineMethod(NilClass, Names.OpXor, FalseClassMembers.Xor);
        DefineMethod(NilClass, Names.QNil, MRubyMethod.True);
        DefineMethod(NilClass, Names.ToS, NilClassMembers.Tos);
        DefineMethod(NilClass, Names.Inspect, NilClassMembers.Inspect);

        TrueClass = DefineClass(Intern("TrueClass"u8), ObjectClass, MRubyVType.True);
        UndefClassMethod(TrueClass, Names.New);
        DefineMethod(TrueClass, Names.OpAnd, TrueClassMembers.And);
        DefineMethod(TrueClass, Names.OpOr, TrueClassMembers.Or);
        DefineMethod(TrueClass, Names.OpXor, TrueClassMembers.Xor);
        DefineMethod(TrueClass, Names.ToS, TrueClassMembers.ToS);
        DefineMethod(TrueClass, Names.Inspect, TrueClassMembers.ToS);

        FalseClass = DefineClass(Intern("FalseClass"u8), ObjectClass, MRubyVType.False);
        UndefClassMethod(FalseClass, Names.New);
        DefineMethod(FalseClass, Names.OpAnd, FalseClassMembers.And);
        DefineMethod(FalseClass, Names.OpOr, FalseClassMembers.Or);
        DefineMethod(FalseClass, Names.OpXor, FalseClassMembers.Xor);
        DefineMethod(FalseClass, Names.ToS, FalseClassMembers.ToS);
        DefineMethod(FalseClass, Names.Inspect, FalseClassMembers.ToS);
    }

    void InitKernel()
    {
        KernelModule = DefineModule(Intern("Kernel"u8), ObjectClass);
        DefineClassMethod(KernelModule, Names.Raise, KernelMembers.Raise);

        DefineMethod(KernelModule, Names.OpEqq, KernelMembers.OpEqq);
        DefineMethod(KernelModule, Names.OpCmp, KernelMembers.Cmp);
        DefineMethod(KernelModule, Names.QBlockGiven, KernelMembers.BlockGiven);
        DefineMethod(KernelModule, Names.Clone, KernelMembers.Clone);
        DefineMethod(KernelModule, Names.Dup, KernelMembers.Dup);
        DefineMethod(KernelModule, Names.Inspect, KernelMembers.Inspect);
        DefineMethod(KernelModule, Names.InitializeCopy, KernelMembers.InitializeCopy);
        DefineMethod(KernelModule, Names.Raise, KernelMembers.Raise);
        DefineMethod(KernelModule, Names.Class, KernelMembers.Class);
        DefineMethod(KernelModule, Names.QEql, KernelMembers.Eql);
        DefineMethod(KernelModule, Names.QNil, MRubyMethod.False);
        DefineMethod(KernelModule, Intern("freeze"u8), KernelMembers.Freeze);
        DefineMethod(KernelModule, Intern("frozen?"u8), KernelMembers.Frozen);
        DefineMethod(KernelModule, Names.Hash, KernelMembers.Hash);
        DefineMethod(KernelModule, Intern("instance_of?"u8), KernelMembers.InstanceOf);
        DefineMethod(KernelModule, Names.QIsA, KernelMembers.KindOf);
        DefineMethod(KernelModule, Names.QKindOf, KernelMembers.KindOf);
        DefineMethod(KernelModule, Intern("iterator?"u8), KernelMembers.BlockGiven);
        DefineMethod(KernelModule, Intern("kind_of?"u8), KernelMembers.KindOf);
        DefineMethod(KernelModule, Names.Nil, MRubyMethod.Nop);
        DefineMethod(KernelModule, Intern("object_id"u8), KernelMembers.ObjectId);
        DefineMethod(KernelModule, Intern("p"u8), KernelMembers.P);
        DefineMethod(KernelModule, Intern("print"u8), KernelMembers.Print);
        DefineMethod(KernelModule, Intern("sleep"u8), KernelMembers.Sleep);
        DefineMethod(KernelModule, Intern("remove_instance_variable"u8), KernelMembers.RemoveInstanceVariable);
        DefineMethod(KernelModule, Names.QRespondTo, KernelMembers.RespondTo);
        DefineMethod(KernelModule, Names.QRespondToMissing, MRubyMethod.False);
        DefineMethod(KernelModule, Names.ToS, KernelMembers.ToS);
        DefineMethod(KernelModule, Intern("lambda"u8), KernelMembers.Lambda);
        // internally used
        DefineMethod(KernelModule, Intern("__case_eqq"u8), KernelMembers.InternalCaseEqq);
        DefineMethod(KernelModule, Intern("__to_int"u8), KernelMembers.InternalToInt);

        IncludeModule(ObjectClass, KernelModule);
    }

    void InitSymbol()
    {
        SymbolClass = DefineClass(Intern("Symbol"u8), ObjectClass, MRubyVType.Symbol);
        UndefClassMethod(SymbolClass, Names.New);

        DefineMethod(SymbolClass, Names.ToS, SymbolMembers.ToS);
        DefineMethod(SymbolClass, Names.Name, SymbolMembers.Name);
        DefineMethod(SymbolClass, Names.ToSym, MRubyMethod.Identity);
        DefineMethod(SymbolClass, Names.Inspect, SymbolMembers.Inspect);
        DefineMethod(SymbolClass, Names.OpCmp, SymbolMembers.Cmp);
        DefineMethod(SymbolClass, Names.OpEq, KernelMembers.Eql);
    }

    void InitProc()
    {
        ProcClass = DefineClass(Intern("Proc"u8), ObjectClass, MRubyVType.Proc);
        DefineClassMethod(ProcClass, Names.New, ProcMembers.New);
        DefineMethod(ProcClass, Intern("arity"u8), ProcMembers.Arity);
        DefineMethod(ProcClass, Names.OpEq, ProcMembers.Eql);
        DefineMethod(ProcClass, Names.QEql, ProcMembers.Eql);

        // NOTE: Why implement Proc#call in byte code?
        // The arguments at the time of `call` method call need to be copied to Proc execution,
        // but bytecode does not need such copying
        var callProc = new RProc(
            new Irep
            {
                RegisterVariableCount = 2,
                Sequence = [(byte)OpCode.Call],
            },
            0,
            ProcClass)
        {
            Upper = null,
            Scope = null
        };
        callProc.SetFlag(MRubyObjectFlags.ProcStrict);
        callProc.SetFlag(MRubyObjectFlags.ProcScope);
        callProc.SetFlag(MRubyObjectFlags.Frozen);

        var callMethod = new MRubyMethod(callProc);
        DefineMethod(ProcClass, Names.Call, callMethod);
        DefineMethod(ProcClass, Names.OpAref, callMethod);
    }

    void InitBinding()
    {
        BindingClass = DefineClass(Intern("Binding"u8), ObjectClass);
        UndefClassMethod(BindingClass, Names.New);

        DefineMethod(BindingClass, Intern("receiver"u8), BindingMembers.Receiver);
        DefineMethod(BindingClass, Intern("local_variables"u8), BindingMembers.LocalVariables);
        DefineMethod(BindingClass, Intern("local_variable_get"u8), BindingMembers.LocalVariableGet);
        DefineMethod(BindingClass, Intern("local_variable_set"u8), BindingMembers.LocalVariableSet);
        DefineMethod(BindingClass, Intern("local_variable_defined?"u8), BindingMembers.LocalVariableDefined);
        DefineMethod(BindingClass, Intern("irb"u8), BindingMembers.Irb);

        DefineMethod(KernelModule, Intern("binding"u8), BindingMembers.Binding);
    }

    void InitException()
    {
        ExceptionClass = DefineClass(Intern("Exception"u8), ObjectClass, MRubyVType.Exception);
        DefineSingletonMethod(ExceptionClass, Names.Exception, ExceptionMembers.New);
        DefineMethod(ExceptionClass, Names.Exception, ExceptionMembers.Exception);
        DefineMethod(ExceptionClass, Names.Initialize, ExceptionMembers.Initialize);
        DefineMethod(ExceptionClass, Names.ToS, ExceptionMembers.ToS);
        DefineMethod(ExceptionClass, Intern("message"u8), ExceptionMembers.ToS);
        DefineMethod(ExceptionClass, Names.Inspect, ExceptionMembers.Inspect);
        DefineMethod(ExceptionClass, Intern("backtrace"u8), ExceptionMembers.Backtrace);

        StandardErrorClass = DefineClass(Intern("StandardError"u8), ExceptionClass);
        DefineClass(Names.RuntimeError, StandardErrorClass);
        DefineClass(Names.TypeError, StandardErrorClass);
        DefineClass(Names.ZeroDivisionError, StandardErrorClass);
        DefineClass(Names.ArgumentError, StandardErrorClass);
        var indexErrorClass = DefineClass(Names.IndexError, StandardErrorClass);
        DefineClass(Names.RangeError, StandardErrorClass);
        DefineClass(Names.FrozenError, StandardErrorClass);
        DefineClass(Names.NotImplementedError, StandardErrorClass);
        DefineClass(Names.LocalJumpError, StandardErrorClass);
        DefineClass(Names.NoMatchingPatternError, StandardErrorClass);
        DefineClass(Names.FloatDomainError, StandardErrorClass);

        DefineClass(Intern("ScriptError"u8), ExceptionClass);
        DefineClass(Intern("SyntaxError"u8), ExceptionClass);
        DefineClass(Intern("StopIteration"u8), indexErrorClass);
    }

    void InitNumeric()
    {
        var numericClass = DefineClass(Intern("Numeric"u8), ObjectClass);
        DefineMethod(numericClass, Intern("finite?"u8), MRubyMethod.True);
        DefineMethod(numericClass, Intern("infinite?"u8), MRubyMethod.False);
        DefineMethod(numericClass, Names.QEql, NumericMembers.Eql);

        IntegerClass = DefineClass(Intern("Integer"u8), numericClass, MRubyVType.Integer);
        UndefClassMethod(IntegerClass, Names.New);
        // DefineMethod(IntegerClass);
        DefineMethod(IntegerClass, Names.OpPow, IntegerMembers.OpPow);
        DefineMethod(IntegerClass, Names.OpAdd, IntegerMembers.OpAdd);
        DefineMethod(IntegerClass, Names.OpSub, IntegerMembers.OpSub);
        DefineMethod(IntegerClass, Names.OpMul, IntegerMembers.OpMul);
        DefineMethod(IntegerClass, Names.OpDiv, IntegerMembers.OpDiv);
        DefineMethod(IntegerClass, Names.ToS, IntegerMembers.ToS);
        DefineMethod(IntegerClass, Names.Inspect, IntegerMembers.ToS);
        DefineMethod(IntegerClass, Names.OpMod, IntegerMembers.Mod);
        DefineMethod(IntegerClass, Names.OpPlus, IntegerMembers.OpPlus);
        DefineMethod(IntegerClass, Names.OpMinus, IntegerMembers.OpMinus);
        DefineMethod(IntegerClass, Intern("div"u8), IntegerMembers.IntDiv);
        DefineMethod(IntegerClass, Intern("fdiv"u8), IntegerMembers.FDiv);
        DefineMethod(IntegerClass, Intern("abs"u8), IntegerMembers.Abs);
        DefineMethod(IntegerClass, Intern("quo"u8), IntegerMembers.Quo);
        DefineMethod(IntegerClass, Intern("ceil"u8), IntegerMembers.Ceil);
        DefineMethod(IntegerClass, Intern("floor"u8), IntegerMembers.Floor);
        DefineMethod(IntegerClass, Intern("round"u8), IntegerMembers.Round);
        DefineMethod(IntegerClass, Intern("next"u8), IntegerMembers.Next);
        DefineMethod(IntegerClass, Intern("succ"u8), IntegerMembers.Next);
        DefineMethod(IntegerClass, Intern("truncate"u8), IntegerMembers.Truncate);
        DefineMethod(IntegerClass, Names.Hash, IntegerMembers.Hash);
        DefineMethod(IntegerClass, Intern("divmod"u8), IntegerMembers.DivMod);
        DefineMethod(IntegerClass, Intern("to_f"u8), IntegerMembers.ToF);
        DefineMethod(IntegerClass, Names.ToI, MRubyMethod.Identity);
        DefineMethod(IntegerClass, Intern("to_int"u8), MRubyMethod.Identity);
        DefineMethod(IntegerClass, Intern("bit_length"u8), IntegerMembers.BitLength);
        DefineMethod(IntegerClass, Intern("gcd"u8), IntegerMembers.Gcd);
        DefineMethod(IntegerClass, Intern("lcm"u8), IntegerMembers.Lcm);
        DefineMethod(IntegerClass, Names.OpAnd, IntegerMembers.OpAnd);
        DefineMethod(IntegerClass, Names.OpOr, IntegerMembers.OpOr);
        DefineMethod(IntegerClass, Names.OpXor, IntegerMembers.OpXor);
        DefineMethod(IntegerClass, Names.OpLShift, IntegerMembers.OpLShift);
        DefineMethod(IntegerClass, Names.OpRShift, IntegerMembers.OpRShift);
        DefineMethod(IntegerClass, Names.OpCmp, NumericMembers.OpCmp);
        DefineMethod(IntegerClass, Names.OpLt, NumericMembers.OpLt);
        DefineMethod(IntegerClass, Names.OpLe, NumericMembers.OpLe);
        DefineMethod(IntegerClass, Names.OpGt, NumericMembers.OpGt);
        DefineMethod(IntegerClass, Names.OpGe, NumericMembers.OpGe);

        FloatClass = DefineClass(Intern("Float"u8), numericClass, MRubyVType.Float);
        UndefClassMethod(FloatClass, Names.New);
        DefineMethod(FloatClass, Names.OpPow, FloatMembers.OpPow);
        DefineMethod(FloatClass, Names.OpAdd, FloatMembers.OpAdd);
        DefineMethod(FloatClass, Names.OpSub, FloatMembers.OpSub);
        DefineMethod(FloatClass, Names.OpMul, FloatMembers.OpMul);
        DefineMethod(FloatClass, Names.OpDiv, FloatMembers.OpDiv);
        DefineMethod(FloatClass, Names.OpMod, FloatMembers.Mod);
        DefineMethod(FloatClass, Names.OpCmp, FloatMembers.OpCmp);
        DefineMethod(FloatClass, Names.OpLt, NumericMembers.OpLt);
        DefineMethod(FloatClass, Names.OpLe, NumericMembers.OpLe);
        DefineMethod(FloatClass, Names.OpGt, NumericMembers.OpGt);
        DefineMethod(FloatClass, Names.OpGe, NumericMembers.OpGe);
        DefineMethod(FloatClass, Names.OpEq, FloatMembers.OpEq);
        DefineMethod(FloatClass, Names.OpPlus, (state, self) => self);
        DefineMethod(FloatClass, Names.OpMinus, FloatMembers.OpNeg);
        DefineMethod(FloatClass, Names.OpAnd, FloatMembers.OpAnd);
        DefineMethod(FloatClass, Names.OpOr, FloatMembers.OpOr);
        DefineMethod(FloatClass, Names.OpXor, FloatMembers.OpXor);
        DefineMethod(FloatClass, Names.OpLShift, FloatMembers.OpLshift);
        DefineMethod(FloatClass, Names.OpRShift, FloatMembers.OpRshift);
        DefineMethod(FloatClass, Intern("ceil"u8), FloatMembers.Ceil);
        DefineMethod(FloatClass, Intern("finite?"u8), FloatMembers.QFinite);
        DefineMethod(FloatClass, Intern("floor"u8), FloatMembers.Floor);
        DefineMethod(FloatClass, Intern("infinite?"u8), FloatMembers.QInfinite);
        DefineMethod(FloatClass, Intern("round"u8), FloatMembers.Round);
        DefineMethod(FloatClass, Intern("to_f"u8), FloatMembers.ToF);
        DefineMethod(FloatClass, Names.ToI, FloatMembers.ToI);
        DefineMethod(FloatClass, Intern("truncate"u8), FloatMembers.Truncate);
        DefineMethod(FloatClass, Intern("divmod"u8), FloatMembers.DivMod);
        DefineMethod(FloatClass, Names.ToS, FloatMembers.ToS);
        DefineMethod(FloatClass, Names.Inspect, FloatMembers.Inspect);
        DefineMethod(FloatClass, Intern("nan?"u8), FloatMembers.QNan);
        DefineMethod(FloatClass, Intern("abs"u8), FloatMembers.Abs);
        DefineMethod(FloatClass, Names.Hash, FloatMembers.Hash);
        DefineMethod(FloatClass, Names.QEql, FloatMembers.QEql);
        DefineMethod(FloatClass, Intern("quo"u8), FloatMembers.Quo);
        DefineMethod(FloatClass, Intern("div"u8), FloatMembers.Div);
        DefineConst(FloatClass, Intern("INFINITY"u8), double.PositiveInfinity);
        DefineConst(FloatClass, Intern("NAN"u8), double.NaN);
    }

    void InitString()
    {
        DefineMethod(KernelModule, Intern("__ENCODING__"u8), (state, _) =>
            state.NewString("UTF-8"u8));

        StringClass = DefineClass(Intern("String"u8), ObjectClass, MRubyVType.String);

        // Early bootstrap (in InitClass) calls NewString before StringClass exists,
        // producing RStrings cached on BasicObject/Object/Module/Class with Class == null.
        // Patch them up now that StringClass is available so later Class#to_s + Send works.
        FixupBootstrapClassName(BasicObjectClass);
        FixupBootstrapClassName(ObjectClass);
        FixupBootstrapClassName(ModuleClass);
        FixupBootstrapClassName(ClassClass);

        void FixupBootstrapClassName(RClass klass)
        {
            if (klass.InstanceVariables.TryGet(Names.ClassNameKey, out var v) &&
                v.Object is RString str && str.Class == null!)
            {
                str.Class = StringClass;
            }
        }

        DefineMethod(StringClass, Names.Initialize, StringMembers.Initialize);
        DefineMethod(StringClass, Names.InitializeCopy, StringMembers.InitializeCopy);
        DefineMethod(StringClass, Names.OpEq, StringMembers.OpEq);
        DefineMethod(StringClass, Names.QEql, StringMembers.OpEq);
        DefineMethod(StringClass, Names.OpCmp, StringMembers.OpCmp);
        DefineMethod(StringClass, Names.OpAref, StringMembers.OpAref);
        DefineMethod(StringClass, Names.OpAset, StringMembers.OpAset);
        DefineMethod(StringClass, Names.OpMul, StringMembers.Times);
        DefineMethod(StringClass, Names.Inspect, StringMembers.Inspect);
        DefineMethod(StringClass, Names.ToSym, StringMembers.ToSym);
        DefineMethod(StringClass, Names.ToS, StringMembers.ToS);
        DefineMethod(StringClass, Names.ToI, StringMembers.ToI);
        DefineMethod(StringClass, Intern("to_f"u8), StringMembers.ToF);
        DefineMethod(StringClass, Intern("size"u8), StringMembers.Size);
        DefineMethod(StringClass, Intern("length"u8), StringMembers.Size);
        DefineMethod(StringClass, Intern("empty?"u8), StringMembers.Empty);
        DefineMethod(StringClass, Intern("include?"u8), StringMembers.Include);
        DefineMethod(StringClass, Intern("index"u8), StringMembers.Index);
        DefineMethod(StringClass, Intern("rindex"u8), StringMembers.RIndex);
        DefineMethod(StringClass, Intern("intern"u8), StringMembers.Intern);
        DefineMethod(StringClass, Intern("replace"u8), StringMembers.Replace);
        DefineMethod(StringClass, Intern("chomp"u8), StringMembers.Chomp);
        DefineMethod(StringClass, Intern("chomp!"u8), StringMembers.ChompBang);
        DefineMethod(StringClass, Intern("chop"u8), StringMembers.Chop);
        DefineMethod(StringClass, Intern("chop!"u8), StringMembers.ChopBang);
        DefineMethod(StringClass, Intern("upcase"u8), StringMembers.Upcase);
        DefineMethod(StringClass, Intern("upcase!"u8), StringMembers.UpcaseBang);
        DefineMethod(StringClass, Intern("downcase"u8), StringMembers.Downcase);
        DefineMethod(StringClass, Intern("downcase!"u8), StringMembers.DowncaseBang);
        DefineMethod(StringClass, Intern("capitalize"u8), StringMembers.Capitalize);
        DefineMethod(StringClass, Intern("capitalize!"u8), StringMembers.CapitalizeBang);
        DefineMethod(StringClass, Intern("reverse"u8), StringMembers.Reverse);
        DefineMethod(StringClass, Intern("reverse!"u8), StringMembers.ReverseBang);
        DefineMethod(StringClass, Intern("slice"u8), StringMembers.OpAref);
        DefineMethod(StringClass, Intern("split"u8), StringMembers.Split);

        DefineMethod(StringClass, Intern("bytesize"u8), StringMembers.ByteCount);
        DefineMethod(StringClass, Intern("bytes"u8), StringMembers.Bytes);
        DefineMethod(StringClass, Intern("getbyte"u8), StringMembers.GetByte);
        DefineMethod(StringClass, Intern("setbyte"u8), StringMembers.SetByte);
        DefineMethod(StringClass, Intern("byteindex"u8), StringMembers.ByteIndex);
        DefineMethod(StringClass, Intern("byteslice"u8), StringMembers.BytesSlice);
        DefineMethod(StringClass, Intern("bytesplice"u8), StringMembers.ByteSplice);

        DefineMethod(StringClass, Intern("__sub_replace"u8), StringMembers.InternalSubReplace);
    }

    void InitArray()
    {
        ArrayClass = DefineClass(Intern("Array"u8), ObjectClass, MRubyVType.Array);

        DefineClassMethod(ArrayClass, Names.OpAref, ArrayMembers.Create);

        DefineMethod(ArrayClass, Names.OpEq, ArrayMembers.OpEq);
        DefineMethod(ArrayClass, Names.QEql, ArrayMembers.Eql);
        DefineMethod(ArrayClass, Names.OpLShift, ArrayMembers.Push);
        DefineMethod(ArrayClass, Names.OpAdd, ArrayMembers.OpAdd);
        DefineMethod(ArrayClass, Names.OpAref, ArrayMembers.OpAref);
        DefineMethod(ArrayClass, Names.OpAset, ArrayMembers.OpAset);
        DefineMethod(ArrayClass, Names.OpAdd, ArrayMembers.Plus);
        DefineMethod(ArrayClass, Names.OpMul, ArrayMembers.Times);
        DefineMethod(ArrayClass, Intern("push"u8), ArrayMembers.Push);
        DefineMethod(ArrayClass, Intern("concat"u8), ArrayMembers.Concat);
        DefineMethod(ArrayClass, Intern("size"u8), ArrayMembers.Size);
        DefineMethod(ArrayClass, Intern("length"u8), ArrayMembers.Size);
        DefineMethod(ArrayClass, Intern("empty?"u8), ArrayMembers.Empty);
        DefineMethod(ArrayClass, Intern("first"u8), ArrayMembers.First);
        DefineMethod(ArrayClass, Intern("last"u8), ArrayMembers.Last);
        DefineMethod(ArrayClass, Intern("reverse"u8), ArrayMembers.Reverse);
        DefineMethod(ArrayClass, Intern("reverse!"u8), ArrayMembers.ReverseBang);
        DefineMethod(ArrayClass, Intern("pop"u8), ArrayMembers.Pop);
        DefineMethod(ArrayClass, Intern("delete_at"u8), ArrayMembers.DeleteAt);
        DefineMethod(ArrayClass, Intern("clear"u8), ArrayMembers.Clear);
        DefineMethod(ArrayClass, Intern("index"u8), ArrayMembers.Index);
        DefineMethod(ArrayClass, Intern("rindex"u8), ArrayMembers.RIndex);
        DefineMethod(ArrayClass, Intern("join"u8), ArrayMembers.Join);
        DefineMethod(ArrayClass, Intern("replace"u8), ArrayMembers.Replace);
        DefineMethod(ArrayClass, Intern("shift"u8), ArrayMembers.Shift);
        DefineMethod(ArrayClass, Intern("unshift"u8), ArrayMembers.Unshift);
        DefineMethod(ArrayClass, Intern("slice"u8), ArrayMembers.OpAref);
        DefineMethod(ArrayClass, Names.ToS, ArrayMembers.ToS);
        DefineMethod(ArrayClass, Names.Inspect, ArrayMembers.Inspect);
        DefineMethod(ArrayClass, Names.InitializeCopy, ArrayMembers.Replace);

        DefineMethod(ArrayClass, Intern("deconstruct"u8), ArrayMembers.Deconstruct);

        DefineMethod(ArrayClass, Intern("__ary_eq"u8), ArrayMembers.InternalEq);
        DefineMethod(ArrayClass, Intern("__ary_cmp"u8), ArrayMembers.InternalCmp);
        DefineMethod(ArrayClass, Intern("__svalue"u8), ArrayMembers.InternalSValue);
    }

    void InitHash()
    {
        HashClass = DefineClass(Intern("Hash"u8), ObjectClass, MRubyVType.Hash);
        DefineMethod(HashClass, Names.Initialize, HashMembers.Initialize);
        DefineMethod(HashClass, Names.InitializeCopy, HashMembers.InitializeCopy);
        DefineMethod(HashClass, Names.ToS, HashMembers.Inspect);
        DefineMethod(HashClass, Names.Inspect, HashMembers.Inspect);
        DefineMethod(HashClass, Names.OpAref, HashMembers.OpAref);
        DefineMethod(HashClass, Names.OpAset, HashMembers.OpAset);
        DefineMethod(HashClass, Intern("size"u8), HashMembers.Size);
        DefineMethod(HashClass, Intern("length"u8), HashMembers.Size);
        DefineMethod(HashClass, Intern("keys"u8), HashMembers.Keys);
        DefineMethod(HashClass, Intern("values"u8), HashMembers.Values);
        DefineMethod(HashClass, Intern("has_key?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("key?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("has_value?"u8), HashMembers.HasValue);
        DefineMethod(HashClass, Intern("value?"u8), HashMembers.HasValue);
        DefineMethod(HashClass, Intern("include?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("member?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("empty?"u8), HashMembers.Empty);
        DefineMethod(HashClass, Intern("default"u8), HashMembers.Default);
        DefineMethod(HashClass, Intern("default_proc"u8), HashMembers.DefaultProc);
        DefineMethod(HashClass, Intern("default="u8), HashMembers.SetDefault);
        DefineMethod(HashClass, Intern("size"u8), HashMembers.Size);
        DefineMethod(HashClass, Intern("clear"u8), HashMembers.Clear);
        DefineMethod(HashClass, Intern("replace"u8), HashMembers.InitializeCopy);
        DefineMethod(HashClass, Intern("store"u8), HashMembers.OpAset);
        DefineMethod(HashClass, Intern("shift"u8), HashMembers.Shift);
        DefineMethod(HashClass, Intern("assoc"u8), HashMembers.Assoc);
        DefineMethod(HashClass, Intern("rassoc"u8), HashMembers.RAssoc);
        DefineMethod(HashClass, Intern("rehash"u8), HashMembers.Rehash);
        DefineMethod(HashClass, Intern("__delete"u8), HashMembers.InternalDelete);
        DefineMethod(HashClass, Intern("__merge"u8), HashMembers.InternalMerge);
        DefineMethod(HashClass, Intern("__except"u8), HashMembers.InternalExcept);
        DefineMethod(HashClass, Intern("__pat_values"u8), HashMembers.InternalPatValues);
        DefineMethod(HashClass, Intern("slice"u8), HashMembers.Slice);
        DefineMethod(HashClass, Intern("slice!"u8), HashMembers.SliceBang);
    }

    void InitRange()
    {
        RangeClass = DefineClass(Intern("Range"u8), ObjectClass, MRubyVType.Range);
        DefineMethod(RangeClass, Intern("begin"u8), RangeMembers.Begin);
        DefineMethod(RangeClass, Intern("end"u8), RangeMembers.End);
        DefineMethod(RangeClass, Intern("first"u8), RangeMembers.First);
        DefineMethod(RangeClass, Intern("last"u8), RangeMembers.Last);
        DefineMethod(RangeClass, Intern("exclude_end?"u8), RangeMembers.ExcludeEnd);
        DefineMethod(RangeClass, Intern("member?"u8), RangeMembers.IsInclude);
        DefineMethod(RangeClass, Intern("eql?"u8), RangeMembers.OpEql);
        DefineMethod(RangeClass, Names.Initialize, RangeMembers.Initialize);
        DefineMethod(RangeClass, Names.InitializeCopy, RangeMembers.InitializeCopy);
        DefineMethod(RangeClass, Names.OpEq, RangeMembers.OpEq);
        DefineMethod(RangeClass, Names.OpEqq, RangeMembers.IsInclude);
        DefineMethod(RangeClass, Names.QInclude, RangeMembers.IsInclude);
        DefineMethod(RangeClass, Names.ToS, RangeMembers.ToS);
        DefineMethod(RangeClass, Names.Inspect, RangeMembers.Inspect);
        DefineMethod(RangeClass, Intern("__num_to_a"u8), RangeMembers.InternalNumToA);
    }

    void InitEnumerable()
    {
        var enumerableModule = DefineModule(Intern("Enumerable"u8), ObjectClass);
        DefineMethod(enumerableModule, Intern("__update_hash"u8), EnumerableMembers.InternalUpdateHash);
    }

    void InitMrbLib()
    {
        LoadBytecode(LibEmbedded.Bytes);
    }

    void InitFiber()
    {
        FiberClass = DefineClass(Intern("Fiber"u8), ObjectClass, MRubyVType.Fiber);

        DefineMethod(FiberClass, Names.Initialize, FiberMembers.Initialize);
        DefineMethod(FiberClass, Names.OpEq, FiberMembers.OpEq);
        DefineMethod(FiberClass, Names.ToS, FiberMembers.ToS);
        DefineMethod(FiberClass, Names.Inspect, FiberMembers.ToS);
        DefineMethod(FiberClass, Intern("resume"u8), FiberMembers.Resume);
        DefineMethod(FiberClass, Intern("transfer"u8), FiberMembers.Transfer);
        DefineMethod(FiberClass, Intern("alive?"u8), FiberMembers.Alive);
        DefineClassMethod(FiberClass, Intern("yield"u8), FiberMembers.Yield);
        DefineClassMethod(FiberClass, Intern("current"u8), FiberMembers.Current);
        DefineClassMethod(FiberClass, Intern("schedule"u8), FiberMembers.Schedule);

        DefineClass(Intern("FiberError"u8), StandardErrorClass);
        DefineClass(Names.RegexpError, StandardErrorClass);
    }

    void InitThread()
    {
        // Stub Thread class — ChibiRuby doesn't expose OS threads. It exists
        // to host CRuby-compatible cooperative-scheduling entry points
        // (notably Thread.pass) that fan out to the fiber scheduler.
        var threadClass = DefineClass(Intern("Thread"u8), ObjectClass, MRubyVType.Object);
        DefineClassMethod(threadClass, Intern("pass"u8), ThreadMembers.Pass);
    }

    /// <summary>
    /// Registers Ruby <c>IO</c>, <c>File</c>, and <c>IOError</c> classes
    /// along with their built-in methods. Not called by <see cref="Create()"/>;
    /// invoke explicitly when the host wants Ruby code to access streams or
    /// the filesystem. Idempotent — calling more than once redefines.
    /// </summary>
    public void DefineIO()
    {
        IOClass = DefineClass(Intern("IO"u8), ObjectClass, MRubyVType.Object);
        DefineMethod(IOClass, Intern("read"u8), IOMembers.Read);
        DefineMethod(IOClass, Intern("write"u8), IOMembers.Write);
        DefineMethod(IOClass, Intern("close"u8), IOMembers.Close);
        DefineMethod(IOClass, Intern("closed?"u8), IOMembers.ClosedQ);

        FileClass = DefineClass(Intern("File"u8), IOClass, MRubyVType.Object);
        DefineClassMethod(FileClass, Intern("open"u8), FileMembers.Open);
        DefineClassMethod(FileClass, Intern("read"u8), FileMembers.Read);
        DefineClassMethod(FileClass, Intern("write"u8), FileMembers.Write);
        DefineClassMethod(FileClass, Intern("exist?"u8), FileMembers.ExistQ);
        DefineClassMethod(FileClass, Intern("exists?"u8), FileMembers.ExistQ);

        DefineClass(Intern("IOError"u8), StandardErrorClass);
    }

    void InitObjectExt()
    {
        DefineMethod(NilClass, Names.ToA, (state, _) => state.NewArray(0));
        DefineMethod(NilClass, Names.ToI, (state, _) => 0);
        DefineMethod(NilClass, Names.ToF, (state, _) => 0.0);
        DefineMethod(NilClass, Intern("to_h"u8), (state, self) => state.NewHash(0));

        DefineMethod(KernelModule, Intern("itself"u8), MRubyMethod.Identity);

        // TODO: impl `instance_exec`
    }

    void InitTime()
    {
        var timeClass = DefineClass(Intern("Time"u8), ObjectClass, MRubyVType.CSharpData);

        var comparableModule = GetConst(Intern("Comparable"u8), ObjectClass);
        IncludeModule(timeClass, comparableModule.As<RClass>());

        DefineClassMethod(timeClass, Intern("at"u8), TimeMembers.CreateAt);
        DefineClassMethod(timeClass, Intern("gm"u8), TimeMembers.CreateUtc);
        DefineClassMethod(timeClass, Intern("utc"u8), TimeMembers.CreateUtc);
        DefineClassMethod(timeClass, Intern("local"u8), TimeMembers.CreateLocal);
        DefineClassMethod(timeClass, Intern("mktime"u8), TimeMembers.CreateLocal);
        DefineClassMethod(timeClass, Intern("now"u8), TimeMembers.Now);

        DefineMethod(timeClass, Names.Initialize, TimeMembers.Initialize);
        DefineMethod(timeClass, Names.InitializeCopy, TimeMembers.InitializeCopy);

        DefineMethod(timeClass, Names.QEql, TimeMembers.OpEq);
        DefineMethod(timeClass, Names.OpEq, TimeMembers.OpEq);
        DefineMethod(timeClass, Names.OpCmp, TimeMembers.OpCmp);
        DefineMethod(timeClass, Names.OpAdd, TimeMembers.OpAdd);
        DefineMethod(timeClass, Names.OpSub, TimeMembers.OpSub);

        DefineMethod(timeClass, Names.Hash, TimeMembers.Hash);
        DefineMethod(timeClass, Names.ToI, TimeMembers.ToI);
        DefineMethod(timeClass, Names.ToF, TimeMembers.ToF);
        DefineMethod(timeClass, Names.ToS, TimeMembers.ToS);
        DefineMethod(timeClass, Names.Inspect, TimeMembers.ToS);
        DefineMethod(timeClass, Intern("asctime"u8), TimeMembers.Asctime);
        DefineMethod(timeClass, Intern("ctime"u8), TimeMembers.Asctime);

        DefineMethod(timeClass, Intern("dst?"u8), TimeMembers.QDaylightSavintTime);
        DefineMethod(timeClass, Intern("gmt?"u8), TimeMembers.QUtc);
        DefineMethod(timeClass, Intern("utc?"u8), TimeMembers.QUtc);
        DefineMethod(timeClass, Intern("sunday?"u8), TimeMembers.QSunday);
        DefineMethod(timeClass, Intern("monday?"u8), TimeMembers.QMonday);
        DefineMethod(timeClass, Intern("tuesday?"u8), TimeMembers.QTuesday);
        DefineMethod(timeClass, Intern("wednesday?"u8), TimeMembers.QWednesday);
        DefineMethod(timeClass, Intern("thursday?"u8), TimeMembers.QThursday);
        DefineMethod(timeClass, Intern("friday?"u8), TimeMembers.QFriday);
        DefineMethod(timeClass, Intern("saturday?"u8), TimeMembers.QSaturday);

        DefineMethod(timeClass, Intern("getgm"u8), TimeMembers.GetUtc);
        DefineMethod(timeClass, Intern("getutc"u8), TimeMembers.GetUtc);
        DefineMethod(timeClass, Intern("getlocal"u8), TimeMembers.GetLocal);
        DefineMethod(timeClass, Intern("utc"u8), TimeMembers.ConvertToUtc);
        DefineMethod(timeClass, Intern("gmtime"u8), TimeMembers.ConvertToUtc);
        DefineMethod(timeClass, Intern("local"u8), TimeMembers.ConvertToLocal);
        DefineMethod(timeClass, Intern("localtime"u8), TimeMembers.ConvertToLocal);

        DefineMethod(timeClass, Intern("year"u8), TimeMembers.Year);
        DefineMethod(timeClass, Intern("mon"u8), TimeMembers.Month);
        DefineMethod(timeClass, Intern("month"u8), TimeMembers.Month);
        DefineMethod(timeClass, Intern("day"u8), TimeMembers.Day);
        DefineMethod(timeClass, Intern("mday"u8), TimeMembers.Day);
        DefineMethod(timeClass, Intern("hour"u8), TimeMembers.Hour);
        DefineMethod(timeClass, Intern("min"u8), TimeMembers.Minute);
        DefineMethod(timeClass, Intern("sec"u8), TimeMembers.Second);
        DefineMethod(timeClass, Intern("usec"u8), TimeMembers.MicroSecond);
        DefineMethod(timeClass, Intern("nsec"u8), TimeMembers.NanoSecond);
        DefineMethod(timeClass, Intern("tv_nsec"u8), TimeMembers.NanoSecond);
        DefineMethod(timeClass, Intern("wday"u8), TimeMembers.Wday);
        DefineMethod(timeClass, Intern("yday"u8), TimeMembers.Yday);
        DefineMethod(timeClass, Intern("zone"u8), TimeMembers.Zone);
        DefineMethod(timeClass, Intern("utc_offset"u8), TimeMembers.UtcOffset);
        DefineMethod(timeClass, Intern("gmt_offset"u8), TimeMembers.UtcOffset);
        DefineMethod(timeClass, Intern("gmtoff"u8), TimeMembers.UtcOffset);
    }

    void InitRandom()
    {
        var randomClass = DefineClass(Intern("Random"u8), ObjectClass, MRubyVType.CSharpData);

        DefineMethod(KernelModule, Intern("rand"u8), RandomMembers.DefaultRand);
        DefineMethod(KernelModule, Intern("srand"u8), RandomMembers.DefaultSRand);

        DefineClassMethod(randomClass, Intern("rand"u8), RandomMembers.DefaultRand);
        DefineClassMethod(randomClass, Intern("srand"u8), RandomMembers.DefaultSRand);
        DefineClassMethod(randomClass, Intern("bytes"u8), RandomMembers.DefaultBytes);

        DefineMethod(randomClass, Names.Initialize, RandomMembers.Initialize);
        DefineMethod(randomClass, Intern("rand"u8), RandomMembers.Rand);
        DefineMethod(randomClass, Intern("srand"u8), RandomMembers.SRand);
        DefineMethod(randomClass, Intern("bytes"u8), RandomMembers.Bytes);

        // create default instance
        var defaultInstance = Send(randomClass, Names.New);
        SetInstanceVariable(randomClass, Names.Default, defaultInstance);

        // Array ext
        DefineMethod(ArrayClass, Intern("shuffle"u8), RandomMembers.ArrayShuffle);
        DefineMethod(ArrayClass, Intern("shuffle!"u8), RandomMembers.ArrayShuffleBang);
        DefineMethod(ArrayClass, Intern("sample"u8), RandomMembers.ArraySample);
    }

    /// <summary>
    /// Registers Ruby <c>Regexp</c>, <c>MatchData</c>, and the Regexp-related
    /// <c>String</c> methods (<c>=~</c>, <c>match</c>, <c>sub</c>, <c>gsub</c>,
    /// <c>scan</c>, <c>index</c>). Not called by <see cref="Create()"/>;
    /// invoke explicitly when the host wants Ruby-level regular expressions.
    /// </summary>
    public void DefineRegexp()
    {
        // Define Regexp class
        var regexpClass = DefineClass(Intern("Regexp"u8), ObjectClass, MRubyVType.CSharpData);

        // Constants
        DefineConst(regexpClass, Intern("IGNORECASE"u8), MRubyRegexpData.RubyIgnoreCase);
        DefineConst(regexpClass, Intern("EXTENDED"u8), MRubyRegexpData.RubyExtended);
        DefineConst(regexpClass, Intern("MULTILINE"u8), MRubyRegexpData.RubyMultiline);

        // Class methods
        DefineSingletonMethod(regexpClass, Intern("new"u8), RegexpMembers.New);
        DefineClassMethod(regexpClass, Intern("compile"u8), RegexpMembers.Compile);
        DefineClassMethod(regexpClass, Intern("escape"u8), RegexpMembers.Escape);
        DefineClassMethod(regexpClass, Intern("quote"u8), RegexpMembers.Quote);
        DefineClassMethod(regexpClass, Intern("union"u8), RegexpMembers.Union);
        DefineClassMethod(regexpClass, Intern("try_convert"u8), RegexpMembers.TryConvert);
        DefineClassMethod(regexpClass, Intern("last_match"u8), RegexpMembers.LastMatch);

        // Instance methods
        DefineMethod(regexpClass, Intern("initialize"u8), RegexpMembers.New);
        DefineMethod(regexpClass, Intern("match"u8), RegexpMembers.Match);
        DefineMethod(regexpClass, Intern("match?"u8), RegexpMembers.QMatch);
        DefineMethod(regexpClass, Intern("=~"u8), RegexpMembers.OpMatch);
        DefineMethod(regexpClass, Intern("==="u8), RegexpMembers.Eqq);
        DefineMethod(regexpClass, Intern("source"u8), RegexpMembers.Source);
        DefineMethod(regexpClass, Intern("options"u8), RegexpMembers.Options);
        DefineMethod(regexpClass, Intern("casefold?"u8), RegexpMembers.QCasefold);
        DefineMethod(regexpClass, Intern("to_s"u8), RegexpMembers.ToS);
        DefineMethod(regexpClass, Intern("inspect"u8), RegexpMembers.Inspect);
        DefineMethod(regexpClass, Intern("=="u8), RegexpMembers.OpEq);
        DefineMethod(regexpClass, Intern("eql?"u8), RegexpMembers.QEql);
        DefineMethod(regexpClass, Intern("hash"u8), RegexpMembers.Hash);
        DefineMethod(regexpClass, Intern("named_captures"u8), RegexpMembers.NamedCaptures);
        DefineMethod(regexpClass, Intern("names"u8), RegexpMembers.NamesMethod);

        // Define MatchData class
        var matchDataClass = DefineClass(Intern("MatchData"u8), ObjectClass, MRubyVType.CSharpData);

        // Instance methods
        DefineMethod(matchDataClass, Intern("[]"u8), MatchDataMembers.OpAref);
        DefineMethod(matchDataClass, Intern("begin"u8), MatchDataMembers.Begin);
        DefineMethod(matchDataClass, Intern("end"u8), MatchDataMembers.End);
        DefineMethod(matchDataClass, Intern("offset"u8), MatchDataMembers.Offset);
        DefineMethod(matchDataClass, Intern("captures"u8), MatchDataMembers.Captures);
        DefineMethod(matchDataClass, Intern("to_a"u8), MatchDataMembers.ToA);
        DefineMethod(matchDataClass, Intern("to_s"u8), MatchDataMembers.ToS);
        DefineMethod(matchDataClass, Intern("size"u8), MatchDataMembers.Size);
        DefineMethod(matchDataClass, Intern("length"u8), MatchDataMembers.Length);
        DefineMethod(matchDataClass, Intern("pre_match"u8), MatchDataMembers.PreMatch);
        DefineMethod(matchDataClass, Intern("post_match"u8), MatchDataMembers.PostMatch);
        DefineMethod(matchDataClass, Intern("regexp"u8), MatchDataMembers.Regexp);
        DefineMethod(matchDataClass, Intern("string"u8), MatchDataMembers.String);
        DefineMethod(matchDataClass, Intern("named_captures"u8), MatchDataMembers.NamedCaptures);
        DefineMethod(matchDataClass, Intern("names"u8), MatchDataMembers.NamesMethod);
        DefineMethod(matchDataClass, Intern("values_at"u8), MatchDataMembers.ValuesAt);
        DefineMethod(matchDataClass, Intern("inspect"u8), MatchDataMembers.Inspect);

        // Regexp-related String methods
        DefineMethod(StringClass, Intern("=~"u8), StringRegexpMembers.OpMatch);
        DefineMethod(StringClass, Intern("match"u8), StringRegexpMembers.Match);
        DefineMethod(StringClass, Intern("match?"u8), StringRegexpMembers.QMatch);
        DefineMethod(StringClass, Intern("sub"u8), StringRegexpMembers.Sub);
        DefineMethod(StringClass, Intern("sub!"u8), StringRegexpMembers.SubBang);
        DefineMethod(StringClass, Intern("gsub"u8), StringRegexpMembers.Gsub);
        DefineMethod(StringClass, Intern("gsub!"u8), StringRegexpMembers.GsubBang);
        DefineMethod(StringClass, Intern("scan"u8), StringRegexpMembers.Scan);
        DefineMethod(StringClass, Intern("index"u8), StringRegexpMembers.Index);
    }

    bool TrySetClassPathLink(RClass outer, RClass c, Symbol name)
    {
        if (c.InstanceVariables.TryGet(Names.OuterKey, out _)) return false;

        c.InstanceVariables.Set(Names.OuterKey, outer);
        outer.InstanceVariables.Set(name, c);

        if (!c.InstanceVariables.TryGet(Names.ClassNameKey, out _))
        {
            c.InstanceVariables.Set(Names.ClassNameKey, name);
        }
        return true;
    }
}
