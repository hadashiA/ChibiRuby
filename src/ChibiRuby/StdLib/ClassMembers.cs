using System;

namespace ChibiRuby.StdLib;

/// <summary>
/// A Ruby class object -- a <c>Module</c> that can be instantiated via
/// <c>new</c>. Every object has a class (reachable via <c>obj.class</c>), and
/// classes themselves are first-class objects. <c>Class.new</c> creates an
/// anonymous class; subclassing is done with <c>class Foo &lt; Bar</c>.
/// </summary>
[RubyClass("Class", Superclass = "Module")]
static class ClassMembers
{
    /// <summary>
    /// Creates a new anonymous class, optionally inheriting from <c>superclass</c> (defaults to <c>Object</c>).
    /// If a block is given, it is evaluated in the context of the new class.
    /// </summary>
    /// <example>
    /// <code>
    /// c = Class.new                # => #&lt;Class:...&gt;
    /// c = Class.new(Array)         # subclass of Array
    /// c = Class.new { def hi; "hi"; end }
    /// c.new.hi                     # => "hi"
    /// </code>
    /// </example>
    [RubyDef("(?Class) ?{ (Class) -> void } -> Class")]
    public static MRubyValue NewClass(MRubyState state, MRubyValue self)
    {
        var superClass = state.TryGetArgumentAt(0, out var superValue)
            ? superValue.As<RClass>()
            : state.ObjectClass;

        var newClass = new RClass(state.ClassClass)
        {
            Super = superClass,
            InstanceVType = superClass.InstanceVType
        };

        superClass.SetFlag(MRubyObjectFlags.ClassInherited);

        var newClassValue = new MRubyValue(newClass);
        if (state.TryFindMethod(newClass.Class, Names.Initialize, out var method, out _) &&
            method == Initialize)
        {
            Initialize(state, newClassValue);
        }
        else
        {
            var block = state.GetBlockArgument();
            state.Send(newClassValue, Names.Initialize,
                [superClass],
                default,
                block);
        }
        state.ClassInheritedHook(superClass, newClass);
        return newClassValue;
    }

    /// <summary>
    /// Allocates a new instance of <c>self</c> and calls <c>initialize</c> on it with the given arguments and block.
    /// </summary>
    /// <example>
    /// <code>
    /// String.new("hello")    # => "hello"
    /// Array.new(3, 0)        # => [0, 0, 0]
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> instance")]

    public static MRubyValue New(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        var kargs = state.GetKeywordArguments();
        var block = state.GetBlockArgument();

        var c = self.As<RClass>();
        if (c.VType == MRubyVType.SClass)
        {
            state.Raise(Names.TypeError, "can't create instance of singleton class"u8);
        }

        var instance = c.InstanceVType switch
        {
            MRubyVType.Array => new RArray(0, c),
            MRubyVType.Hash => new RHash(0, state.HashKeyEqualityComparer, state.ValueEqualityComparer, c),
            MRubyVType.String => new RString(0, c),
            MRubyVType.Range => new RRange(default, default, false, c),
            MRubyVType.Exception => new RException(null!, c),
            MRubyVType.Object => new RObject(c.InstanceVType, c),
            MRubyVType.Class => new RClass(c, c.InstanceVType)
            {
                InstanceVType = c.InstanceVType,
                Super = state.ObjectClass
            },
            MRubyVType.Module => new RClass(c, c.InstanceVType)
            {
                InstanceVType = MRubyVType.Undef,
                Super = null!
            },
            MRubyVType.Fiber => new RFiber(state, c),
            MRubyVType.CSharpData => new RData(c),
            MRubyVType.Proc => state.NewClosure(state.GetBlockArgument(false)!.Irep, procClass: c),
            _ => throw new ArgumentOutOfRangeException($"Cannot instantiate: {c.InstanceVType}")
        };
        var instanceValue = new MRubyValue(instance);
        state.Send(instanceValue, Names.Initialize, args, kargs, block);
        return instanceValue;

    }

    /// <summary>
    /// Returns the superclass of <c>self</c>, or <c>nil</c> if there is none.
    /// </summary>
    /// <example>
    /// <code>
    /// String.superclass        # => Object
    /// Object.superclass        # => BasicObject
    /// BasicObject.superclass   # => nil
    /// </code>
    /// </example>
    [RubyDef("() -> Class?")]
    public static MRubyValue Superclass(MRubyState state, MRubyValue self)
    {
        var c = self.As<RClass>().AsOrigin().Super;
        while (c != null! && c.VType == MRubyVType.IClass)
        {
            c = c.AsOrigin().Super;
        }
        return c == null ? MRubyValue.Nil : new MRubyValue(c);
    }


    /// <summary>
    /// Initializes a newly-created class. If a block is given, it is evaluated in the class context.
    /// Called automatically by <c>Class.new</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// c = Class.new(Object) do
    ///   def greet; "hi"; end
    /// end
    /// c.new.greet    # => "hi"
    /// </code>
    /// </example>
    [RubyDef("(?Class) ?{ (Class) -> void } -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        var c = self.As<RClass>();
        var block = state.GetBlockArgument();
        if (block is { } proc)
        {
            state.YieldWithClass(c, self, [self], proc);
        }
        return self;
    }

}
