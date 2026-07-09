namespace ChibiRuby.StdLib;

static class BindingMembers
{
    // Kernel#binding - capture the caller's frame as a Binding.
    public static MRubyMethod Binding = new((state, self) =>
        state.CreateBinding());

    // Binding#receiver
    public static MRubyMethod Receiver = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        return binding.Receiver;
    });

    // Binding#local_variables - returns Array of Symbols.
    public static MRubyMethod LocalVariables = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        var names = binding.LocalVariableNames;
        var array = state.NewArray(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            array.Push(new MRubyValue(names[i]));
        }
        return new MRubyValue(array);
    });

    // Binding#local_variable_get(name)
    public static MRubyMethod LocalVariableGet = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        var name = state.AsSymbol(state.GetArgumentAt(0));
        if (!binding.TryGetLocal(name, out var value))
        {
            state.Raise(Names.NameError, state.NewString($"local variable `{state.NameOf(name)}' is not defined for {state.NameOf(binding.CallerMethodId)}"));
        }
        return value;
    });

    // Binding#local_variable_set(name, value)
    public static MRubyMethod LocalVariableSet = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        var name = state.AsSymbol(state.GetArgumentAt(0));
        var value = state.GetArgumentAt(1);
        // CRuby semantics: a name not already in the binding's scope is introduced
        // silently. Useful in the debugger REPL for letting users stash values.
        binding.SetLocal(name, value);
        return value;
    });

    // Binding#local_variable_defined?(name)
    public static MRubyMethod LocalVariableDefined = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        var name = state.AsSymbol(state.GetArgumentAt(0));
        return binding.TryGetLocal(name, out _) ? MRubyValue.True : MRubyValue.False;
    });

    // Binding#break (alias Binding#b) - ruby/debug compatible breakpoint.
    public static MRubyMethod Break = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        return TriggerBreak(state, binding);
    });

    // Kernel#debugger - ruby/debug compatible; equivalent to binding.break on the caller's frame.
    public static MRubyMethod Debugger = new((state, self) =>
        TriggerBreak(state, state.CreateBinding()));

    static MRubyValue TriggerBreak(MRubyState state, RBinding binding)
    {
        if (state.DebuggerHook is { } hook)
        {
            hook.OnBindingBreak(state, binding);
            return MRubyValue.Nil;
        }
        state.Raise(Names.RuntimeError, "binding.break called but no debugger is attached (ChibiRuby.Debugger not initialized)"u8);
        return MRubyValue.Nil;
    }

    static RBinding AsBinding(MRubyState state, MRubyValue self)
    {
        if (self.Object is RBinding binding) return binding;
        state.Raise(Names.TypeError, "expected Binding"u8);
        return null!;
    }
}
