namespace MRubyCS.StdLib;

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

    // Binding#irb - default implementation when no debugger is attached.
    // The MRubyCS.Debugger package replaces this with a hook-triggering implementation
    // by calling DefineMethod again at attach time.
    public static MRubyMethod Irb = new((state, self) =>
    {
        var binding = AsBinding(state, self);
        if (state.DebuggerHook is { } hook)
        {
            hook.OnBindingIrb(state, binding);
            return MRubyValue.Nil;
        }
        state.Raise(Names.RuntimeError, "binding.irb called but no debugger is attached (MRubyCS.Debugger not initialized)"u8);
        return MRubyValue.Nil;
    });

    static RBinding AsBinding(MRubyState state, MRubyValue self)
    {
        if (self.Object is RBinding binding) return binding;
        state.Raise(Names.TypeError, "expected Binding"u8);
        return null!;
    }
}
