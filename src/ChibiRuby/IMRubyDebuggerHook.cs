namespace ChibiRuby;

/// <summary>Optional VM-side hook installed via <see cref="MRubyState.DebuggerHook"/>. Invoked on the VM thread.</summary>
public interface IMRubyDebuggerHook
{
    /// <summary>Called when user code invokes <c>binding.break</c> (or its aliases <c>binding.b</c> / <c>debugger</c>); expected to suspend the VM thread until resumed.</summary>
    void OnBindingBreak(MRubyState state, RBinding binding);

    /// <summary>Fires on every instruction while the hook is installed; implementations must early-out fast.</summary>
    void OnInstruction(MRubyState state, Irep irep, int pc);
}
