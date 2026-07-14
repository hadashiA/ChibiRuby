using System;
using System.Threading;

namespace ChibiRuby.StdLib;

/// <summary>
/// Mixin included by <c>Object</c> that provides the methods available on
/// every object: <c>puts</c>, <c>raise</c>, <c>require</c>, <c>send</c>,
/// <c>respond_to?</c>, etc. Top-level <c>def</c>s become private
/// <c>Kernel</c> methods, which is why they can be called anywhere without
/// an explicit receiver.
/// </summary>
[RubyModule("Kernel")]
static class KernelMembers
{
    /// <summary>
    /// Internal helper used by <c>case</c>/<c>when</c> matching when the pattern is array-like.
    /// </summary>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue InternalCaseEqq(MRubyState state, MRubyValue self)
    {
        if (self.IsNil)
        {
            return MRubyValue.False;
        }

        var other = state.GetArgumentAt(0);
        RArray? array = null;
        if (self.Object is RArray x)
        {
            array = x;
        }
        else if (state.RespondTo(self, Names.ToA))
        {
            var arrayValue = state.Send(self, Names.ToA);
            if (!arrayValue.IsNil)
            {
                state.EnsureValueType(arrayValue, MRubyVType.Array);
                array = arrayValue.As<RArray>();
            }
        }
        if (array is null)
        {
            return state.Send(self, Names.OpEqq, other);
        }

        for (var i = 0; i < array.Length; i++)
        {
            var c = state.Send(array[i], Names.OpEqq, other);
            if (c.Truthy)
            {
                return MRubyValue.True;
            }
        }
        return MRubyValue.False;
    }

    /// <summary>
    /// Internal helper used by VM and integer coercion paths to convert a value to an <c>Integer</c>.
    /// </summary>
    [RubyDef("(untyped) -> Integer")]
    public static MRubyValue InternalToInt(MRubyState state, MRubyValue self)
    {
        return state.AsInteger(self);
    }

    /// <summary>
    /// Returns <c>true</c> when the enclosing method was called with a block.
    /// </summary>
    /// <example>
    /// <code>
    /// def foo
    ///   block_given?
    /// end
    /// foo            # => false
    /// foo {}         # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]

    public static MRubyValue BlockGiven(MRubyState state, MRubyValue self)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Raises an exception. With no arguments, re-raises or raises <c>RuntimeError</c>.
    /// With a String, raises <c>RuntimeError</c> with that message. With a class (and optional message),
    /// raises an instance of that exception class.
    /// </summary>
    /// <example>
    /// <code>
    /// raise "oops"
    /// raise ArgumentError, "bad value"
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> bot")]
    public static MRubyValue Raise(MRubyState state, MRubyValue self)
    {
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 0:
                state.Raise(Names.RuntimeError, []);
                break;
            case 1:
                var arg = state.GetArgumentAt(0);
                switch (arg.VType)
                {
                    case MRubyVType.String:
                        state.Raise(Names.RuntimeError, arg.As<RString>());
                        break;
                    case MRubyVType.Exception:
                    {
                        state.Raise(arg.As<RException>());
                        break;
                    }
                    case MRubyVType.Class:
                    {
                        var ex = new RException(state.NewString(""u8), arg.As<RClass>());
                        state.Raise(ex);
                        break;
                    }
                    default:
                        state.Raise(Names.TypeError, $"exception class/object expected");
                        break;
                }
                break;
            case 2:
                var exceptionClass = state.GetArgumentAsClassAt(0);
                var message = state.GetArgumentAsStringAt(1);
                state.Raise(exceptionClass, message);
                break;
        }
        return MRubyValue.Nil; // not reached
    }

    /// <summary>
    /// Case-equality (<c>===</c>). For most objects, equivalent to <c>==</c>. Overridden by classes
    /// like <c>Module</c>, <c>Range</c>, and <c>Regexp</c> for use in <c>case</c>/<c>when</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1 === 1        # => true
    /// 1 === 2        # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEqq(MRubyState state, MRubyValue self)
    {
        var arg = state.GetArgumentAt(0);
        return state.ValueEquals(self, arg);
    }

    /// <summary>
    /// Generic three-way comparison. Returns <c>0</c> when <c>self</c> is the same object as
    /// the argument, otherwise <c>nil</c>. Subclasses override this for ordered comparison.
    /// </summary>
    /// <example>
    /// <code>
    /// obj = Object.new
    /// obj &lt;=&gt; obj            # => 0
    /// obj &lt;=&gt; Object.new     # => nil
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue Cmp(MRubyState state, MRubyValue self)
    {
        var other = state.GetArgumentAt(0);
        if (state.IsRecursiveCalling(Names.OpCmp, self, other))
        {
            return MRubyValue.Nil;
        }
        if (self == other)
        {
            return 0;
        }
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns the class of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.class           # => Integer
    /// "hi".class        # => String
    /// nil.class         # => NilClass
    /// </code>
    /// </example>
    [RubyDef("() -> Class")]

    public static MRubyValue Class(MRubyState state, MRubyValue self)
    {
        return state.ClassOf(self).GetRealClass();
    }

    /// <summary>
    /// Returns a shallow copy of <c>self</c>, preserving the frozen state and singleton methods.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// b = a.clone
    /// b.equal?(a)    # => false
    /// b == a         # => true
    /// </code>
    /// </example>
    [RubyDef("() -> instance")]

    public static MRubyValue Clone(MRubyState state, MRubyValue self)
    {
        return state.CloneObject(self);
    }

    /// <summary>
    /// Returns a shallow copy of <c>self</c>. Unlike <c>clone</c>, the copy is not frozen
    /// and singleton methods are not copied.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// b = a.dup
    /// b.equal?(a)    # => false
    /// b == a         # => true
    /// </code>
    /// </example>
    [RubyDef("() -> instance")]

    public static MRubyValue Dup(MRubyState state, MRubyValue self)
    {
        return state.DupObject(self);
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> and the argument refer to the same object (identity by default).
    /// </summary>
    /// <example>
    /// <code>
    /// 1.eql?(1)        # => true
    /// 1.eql?(1.0)      # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eql(MRubyState state, MRubyValue self)
    {
        return self == state.GetArgumentAt(0);
    }

    /// <summary>
    /// Marks <c>self</c> as frozen so it can no longer be modified, and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// s = "hi".freeze
    /// s.frozen?      # => true
    /// </code>
    /// </example>
    [RubyDef("() -> self")]

    public static MRubyValue Freeze(MRubyState state, MRubyValue self)
    {
        if (self.Object is { } obj)
        {
            if (!obj.IsFrozen)
            {
                obj.MarkAsFrozen();
                if (obj.Class.VType == MRubyVType.SClass)
                {
                    obj.Class.MarkAsFrozen();
                }
            }
        }
        return self;
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> is frozen, otherwise <c>false</c>.
    /// Immediate values (numbers, symbols, <c>nil</c>, <c>true</c>, <c>false</c>) are always frozen.
    /// </summary>
    /// <example>
    /// <code>
    /// "hi".frozen?         # => false
    /// "hi".freeze.frozen?  # => true
    /// 1.frozen?            # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]

    public static MRubyValue Frozen(MRubyState state, MRubyValue self)
    {
        if (self.Object is { } obj)
        {
            return obj.IsFrozen;
        }
        return MRubyValue.True;
    }

    /// <summary>
    /// Returns a hash code for <c>self</c>, suitable for use as a <c>Hash</c> key.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.hash         # => Integer
    /// "hi".hash      # => Integer
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        return self.ObjectId;
    }

    // Shared delegate instance for the default `hash`. Registered in Init and compared
    // by reference in MRubyValueHashKeyEqualityComparer to detect "hash not overridden".
    internal static readonly MRubyFunc HashFunc = Hash;

    /// <summary>
    /// Returns a bound <c>Method</c> object for the named method on <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// "hi".method(:upcase).call  # => "HI"
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String) -> Method")]
    public static MRubyValue Method(MRubyState state, MRubyValue self)
    {
        state.EnsureArgumentCount(1);

        var methodId = state.GetArgumentAsSymbolAt(0);
        var receiverClass = state.ClassOf(self);
        if (!state.TryFindMethod(receiverClass, methodId, out var method, out var owner) ||
            method == MRubyMethod.Undef)
        {
            state.RaiseNameError(methodId, state.NewString($"undefined method '{state.NameOf(methodId)}' for class '{state.ClassNameOf(self)}'"));
        }

        return new RMethod(self, methodId, owner, method, state.MethodClass);
    }

    /// <summary>
    /// Initializer for object copies. Invoked by <c>clone</c> and <c>dup</c>. Raises <c>TypeError</c>
    /// if the source object is of a different class.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def initialize_copy(other); super; end
    /// end
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> self")]
    public static MRubyValue InitializeCopy(MRubyState state, MRubyValue self)
    {
        var original = state.GetArgumentAt(0);
        if (original == self) return self;
        if (self.VType != original.VType ||
            state.ClassOf(self) != state.ClassOf(original))
        {
            state.Raise(Names.TypeError, "initialize_copy shoud take same class object"u8);
        }
        return self;
    }

    /// <summary>
    /// Returns a human-readable string representation of <c>self</c>, useful for debugging.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2].inspect      # => "[1, 2]"
    /// "hi".inspect        # => "\"hi\""
    /// nil.inspect         # => "nil"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]

    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        return state.InspectObject(self);
    }

    /// <summary>
    /// Returns <c>true</c> if the class of <c>self</c> is exactly <c>klass</c> (not a subclass).
    /// </summary>
    /// <example>
    /// <code>
    /// 1.instance_of?(Integer)   # => true
    /// 1.instance_of?(Numeric)   # => false
    /// </code>
    /// </example>
    [RubyDef("(Class) -> bool")]
    public static MRubyValue InstanceOf(MRubyState state, MRubyValue self)
    {
        var c= state.GetArgumentAsClassAt(0);
        return state.InstanceOf(self, c);
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> is an instance of <c>mod</c> or one of its descendants
    /// (or includes <c>mod</c> when it is a module). Alias for <c>is_a?</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.kind_of?(Integer)   # => true
    /// 1.kind_of?(Numeric)   # => true
    /// 1.is_a?(String)       # => false
    /// </code>
    /// </example>
    [RubyDef("(Module) -> bool")]
    public static MRubyValue KindOf(MRubyState state, MRubyValue self)
    {
        var c= state.GetArgumentAsClassAt(0);
        return state.KindOf(self, c);
    }

    /// <summary>
    /// Returns an integer identifier unique to <c>self</c> for its lifetime.
    /// </summary>
    /// <example>
    /// <code>
    /// a = "hi"
    /// a.object_id == a.object_id   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]

    public static MRubyValue ObjectId(MRubyState state, MRubyValue self)
    {
        return self.ObjectId;
    }

    /// <summary>
    /// Writes each argument to standard output by converting it via <c>to_s</c>. Returns <c>nil</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// print "hello", " ", "world"   # => nil  (writes: hello world)
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> nil")]
    public static MRubyValue Print(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            var s = state.Stringify(arg);
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(s.AsSpan()));
        }
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Writes the <c>inspect</c> representation of each argument to standard output, each followed by a newline.
    /// Returns the single argument, the array of arguments, or <c>nil</c> if no arguments are given.
    /// </summary>
    /// <example>
    /// <code>
    /// p "hi"        # writes: "hi"   => "hi"
    /// p 1, 2, 3     # writes each line; returns [1, 2, 3]
    /// </code>
    /// </example>
    [RubyDef("(*untyped) -> untyped")]
    public static MRubyValue P(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            var s = state.Inspect(arg);
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(s.AsSpan()));
        }

        if (args.Length == 1)
        {
            return args[0];
        }
        return state.NewArray(args);
    }

    /// <summary>
    /// Removes and returns the value of the instance variable named <c>name</c> from <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// class Foo
    ///   def initialize; @x = 1; end
    /// end
    /// f = Foo.new
    /// f.remove_instance_variable(:@x)   # => 1
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String) -> untyped")]
    public static MRubyValue RemoveInstanceVariable(MRubyState state, MRubyValue self)
    {
        var name = state.GetArgumentAsSymbolAt(0);
        state.EnsureInstanceVariableName(name);
        if (self.Object is not RObject obj)
        {
            return MRubyValue.Undef;
        }

        return state.RemoveInstanceVariable(obj, name);
    }

    /// <summary>
    /// Returns the value of the instance variable named <c>name</c>, or <c>nil</c> if it is not set.
    /// </summary>
    /// <example>
    /// <code>
    /// obj.instance_variable_get(:@x)
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String) -> untyped")]
    public static MRubyValue InstanceVariableGet(MRubyState state, MRubyValue self)
    {
        var name = state.GetArgumentAsSymbolAt(0);
        state.EnsureInstanceVariableName(name);
        return self.Object is RObject obj
            ? state.GetInstanceVariable(obj, name)
            : MRubyValue.Nil;
    }

    /// <summary>
    /// Sets the instance variable named <c>name</c> to <c>value</c> and returns <c>value</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// obj.instance_variable_set(:@x, 1)   # => 1
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String, untyped) -> untyped")]
    public static MRubyValue InstanceVariableSet(MRubyState state, MRubyValue self)
    {
        var name = state.GetArgumentAsSymbolAt(0);
        state.EnsureInstanceVariableName(name);
        var value = state.GetArgumentAt(1);
        if (self.Object is RObject obj)
        {
            state.SetInstanceVariable(obj, name, value);
            return value;
        }

        state.Raise(Names.TypeError, $"can't set instance variable on {state.ClassNameOf(self)}");
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> responds to the named method.
    /// Pass <c>true</c> for the second argument to include private methods.
    /// </summary>
    /// <example>
    /// <code>
    /// "hi".respond_to?(:upcase)    # => true
    /// 1.respond_to?(:foo)          # => false
    /// </code>
    /// </example>
    [RubyDef("(Symbol | String, ?bool) -> bool")]
    public static MRubyValue RespondTo(MRubyState state, MRubyValue self)
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        var includesPrivate = state.GetArgumentAt(1).Truthy;
        var result = state.RespondTo(self, methodId);
        if (!result)
        {
            if (state.RespondTo(state.ClassOf(self), methodId))
            {
                return state.Send(self, methodId, methodId, includesPrivate);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns a string representation of <c>self</c>. The default form is <c>"#&lt;ClassName:0x...&gt;"</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// 1.to_s          # => "1"
    /// nil.to_s        # => ""
    /// :sym.to_s       # => "sym"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]

    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        return state.StringifyAny(self);
    }

    /// <summary>
    /// Creates a <c>Proc</c> object from the given block with strict argument checking
    /// (lambda semantics: wrong-arity raises <c>ArgumentError</c>).
    /// </summary>
    /// <example>
    /// <code>
    /// add = lambda { |a, b| a + b }
    /// add.call(1, 2)    # => 3
    /// </code>
    /// </example>
    [RubyDef("() { (*untyped) -> untyped } -> Proc")]

    public static MRubyValue Lambda(MRubyState state, MRubyValue self)
    {
        var block = state.GetBlockArgument();
        if (block == null)
        {
            state.Raise(Names.ArgumentError, "tried to create Proc object without a block"u8);
        }

        if (!block!.HasFlag(MRubyObjectFlags.ProcStrict))
        {
            var dup = block.Dup();
            dup.SetFlag(MRubyObjectFlags.ProcStrict);
            return dup;
        }
        return block;
    }

    /// <summary>
    /// Suspends execution for <c>duration</c> seconds. With no argument or <c>nil</c>, sleeps forever
    /// (only meaningful when a fiber scheduler can wake it). Returns the number of seconds slept.
    /// </summary>
    /// <example>
    /// <code>
    /// sleep 0.1     # => 0
    /// sleep 1       # => 1
    /// </code>
    /// </example>
    [RubyDef("(?Numeric) -> Integer")]
    public static MRubyValue Sleep(MRubyState state, MRubyValue self)
    {
        double seconds;
        if (state.GetArgumentCount() == 0 || state.GetArgumentAt(0).IsNil)
        {
            // Sleep forever -- only meaningful with a scheduler that can
            // wake the fiber via Unblock.
            seconds = double.PositiveInfinity;
        }
        else
        {
            seconds = state.GetArgumentAsFloatAt(0);
        }

        // Dispatch to the scheduler when one is installed and the call site
        // is inside a non-root fiber. The scheduler hook performs the
        // Fiber.yield itself (CRuby-style); the resume value is delivered
        // to the VM stack via the existing vmexec=true path, so the C#
        // return below is unused on the resume path.
        if (state.TryGetActiveFiberScheduler(out var scheduler))
        {
            // sleep 0 → cooperative yield (Thread.pass semantics).
            if (seconds <= 0 && !double.IsPositiveInfinity(seconds))
            {
                scheduler.Yield();
                return MRubyValue.Nil;
            }

            var duration = double.IsPositiveInfinity(seconds)
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(seconds);
            scheduler.KernelSleep(duration);
            return MRubyValue.Nil;
        }

        // Blocking-fiber path: synchronous host-thread sleep.
        if (double.IsPositiveInfinity(seconds))
        {
            state.Raise(Names.NotImplementedError,
                "sleep without a duration requires a non-blocking fiber and a scheduler"u8);
        }
        if (seconds > 0)
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
        return new MRubyValue((long)seconds);
    }
}
