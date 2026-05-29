namespace ChibiRuby.Compiler
{
public enum DiagnosticSeverity
{
    Warning,
    Error,
    GeneratorWarning,
    GeneratorError,
}

public class DiagnosticsDescriptor
{
    public DiagnosticSeverity Severity { get; }
    public int Line { get; }
    public int Column { get; }
    public string? Message { get; }

    public DiagnosticsDescriptor(DiagnosticSeverity severity, int line, int column, string? message)
    {
        Severity = severity;
        Line = line;
        Column = column;
        Message = message;
    }

        public override string ToString() => $"{Severity}: {Message} ({Line}:{Column})";
    }
}
