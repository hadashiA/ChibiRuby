using System;
using System.Threading;

namespace MRubyCS.StdLib;

[RubyModule("Kernel")]
static class KernelMembers
{
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

    [RubyDef("(untyped) -> Integer")]
    public static MRubyValue InternalToInt(MRubyState state, MRubyValue self)
    {
        return state.AsInteger(self);
    }

    [RubyDef("() -> bool")]

    public static MRubyValue BlockGiven(MRubyState state, MRubyValue self)
    {
        throw new NotSupportedException();
    }

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

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEqq(MRubyState state, MRubyValue self)
    {
        var arg = state.GetArgumentAt(0);
        return state.ValueEquals(self, arg);
    }

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

    [RubyDef("() -> Class")]

    public static MRubyValue Class(MRubyState state, MRubyValue self)
    {
        return state.ClassOf(self).GetRealClass();
    }

    [RubyDef("() -> instance")]

    public static MRubyValue Clone(MRubyState state, MRubyValue self)
    {
        return state.CloneObject(self);
    }

    [RubyDef("() -> instance")]

    public static MRubyValue Dup(MRubyState state, MRubyValue self)
    {
        return state.DupObject(self);
    }

    [RubyDef("(untyped) -> bool")]
    public static MRubyValue Eql(MRubyState state, MRubyValue self)
    {
        return self == state.GetArgumentAt(0);
    }

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

    [RubyDef("() -> bool")]

    public static MRubyValue Frozen(MRubyState state, MRubyValue self)
    {
        if (self.Object is { } obj)
        {
            return obj.IsFrozen;
        }
        return MRubyValue.True;
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue Hash(MRubyState state, MRubyValue self)
    {
        return self.ObjectId;
    }

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

    [RubyDef("() -> String")]

    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        return state.InspectObject(self);
    }

    [RubyDef("(Class) -> bool")]
    public static MRubyValue InstanceOf(MRubyState state, MRubyValue self)
    {
        var c= state.GetArgumentAsClassAt(0);
        return state.InstanceOf(self, c);
    }

    [RubyDef("(Module) -> bool")]
    public static MRubyValue KindOf(MRubyState state, MRubyValue self)
    {
        var c= state.GetArgumentAsClassAt(0);
        return state.KindOf(self, c);
    }

    [RubyDef("() -> Integer")]

    public static MRubyValue ObjectId(MRubyState state, MRubyValue self)
    {
        return self.ObjectId;
    }

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

    [RubyDef("(Symbol | String) -> untyped")]
    public static MRubyValue RemoveInstanceVariable(MRubyState state, MRubyValue self)
    {
        var name = state.GetArgumentAsSymbolAt(0);
        if (self.Object is RObject obj)
        {
            if (obj.InstanceVariables.Remove(name, out var v))
            {
                return v;
            }
        }
        return MRubyValue.Undef;
    }

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

    [RubyDef("() -> String")]

    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        return state.StringifyAny(self);
    }

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

    [RubyDef("(?Numeric) -> Integer")]
    public static MRubyValue Sleep(MRubyState state, MRubyValue self)
    {
        double seconds;
        if (state.GetArgumentCount() == 0 || state.GetArgumentAt(0).IsNil)
        {
            // Sleep forever — only meaningful with a scheduler that can
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
