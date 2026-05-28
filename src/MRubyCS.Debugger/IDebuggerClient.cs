namespace MRubyCS.Debugger;

/// <summary>Connector between <see cref="MRubyDebugger"/> and an outer protocol (DAP, test harness, ...).</summary>
public interface IDebuggerClient
{
    /// <summary>Called on the VM thread when the VM has just suspended.</summary>
    void OnStopped(MRubyDebugger debugger, StopEvent ev);

    /// <summary>Called on the VM thread just before the VM resumes user code.</summary>
    void OnResumed(MRubyDebugger debugger);
}
