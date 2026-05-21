namespace MRubyCS.StdLib;

[RubyClass("Exception")]
static class ExceptionMembers
{
    [RubyDef("(*untyped) -> Exception")]
    public static MRubyValue New(MRubyState state, MRubyValue self)
    {
        var args = state.GetRestArgumentsAfter(0);
        var block = state.GetBlockArgument();

        var c = self.As<RClass>();
        var o = new RException(null, c);
        var value = new MRubyValue(o);
        if (state.TryFindMethod(c, Names.Initialize, out var method, out _) &&
            method != MRubyMethod.Nop)
        {
            state.Send(value, Names.Initialize, args, kargs: null, block: block);
        }
        return value;
    }

    [RubyDef("(?String) -> Exception")]
    public static MRubyValue Exception(MRubyState state, MRubyValue self)
    {
        if (!state.TryGetArgumentAt(0, out var arg) || arg == self)
        {
            return self;
        }

        var ex = state.CloneObject(self);
        ex.As<RException>().Message = state.Stringify(arg);
        return ex;
    }

    [RubyDef("(?String) -> void")]
    public static MRubyValue Initialize(MRubyState state, MRubyValue self)
    {
        if (state.TryGetArgumentAt(0, out var arg))
        {
            self.As<RException>().Message = state.Stringify(arg);
        }
        return self;
    }

    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState state, MRubyValue self)
    {
        if (self.As<RException>().Message is { } message)
        {
            return message;
        }
        return state.NameOf(state.ClassOf(self));
    }

    [RubyDef("() -> String")]
    public static MRubyValue Inspect(MRubyState state, MRubyValue self)
    {
        var className = state.NameOf(state.ClassOf(self));
        var message = self.As<RException>().Message;
        if (message is { Length: > 0 })
        {
            return state.NewString($"{message} ({className})");
        }
        return className;
    }

    [RubyDef("() -> Array[String]")]
    public static MRubyValue Backtrace(MRubyState state, MRubyValue self)
    {
        var backtrace = self.As<RException>().Backtrace;
        if (backtrace is null)
        {
            return MRubyValue.Nil;
        }
        return backtrace.ToRArray(state);
    }
}
