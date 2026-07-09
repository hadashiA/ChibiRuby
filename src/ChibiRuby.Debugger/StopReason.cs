namespace ChibiRuby.Debugger;

public enum StopReason
{
    BindingBreak,
    LineBreakpoint,
    Step,
}

public sealed class StopEvent
{
    public StopReason Reason { get; init; }
    public RBinding Binding { get; init; } = default!;
    public string? File { get; init; }
    public int Line { get; init; } = -1;
}
