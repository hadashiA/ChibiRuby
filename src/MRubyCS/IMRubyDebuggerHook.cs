namespace MRubyCS;

/// <summary>Optional VM-side hook installed via <see cref="MRubyState.DebuggerHook"/>. Invoked on the VM thread.</summary>
public interface IMRubyDebuggerHook
{
    /// <summary>Called when user code invokes <c>binding.irb</c>; expected to suspend the VM thread until resumed.</summary>
    void OnBindingIrb(MRubyState state, RBinding binding);

    /// <summary>Fires on every instruction while the hook is installed; implementations must early-out fast.</summary>
    void OnInstruction(MRubyState state, Irep irep, int pc);
}
