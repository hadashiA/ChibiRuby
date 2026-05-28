namespace MRubyCS.Debugger;

public sealed class EvalResult
{
    public MRubyValue Value { get; init; }
    public string DisplayString { get; init; } = "";
    public bool IsError { get; init; }
}
