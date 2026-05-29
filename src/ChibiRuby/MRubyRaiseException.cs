using System;
using System.Text;

namespace ChibiRuby;

public class MRubyRaiseException(
    string message,
    MRubyState state,
    RException exceptionObject,
    int callDepth)
    : MRubyLongJumpException(BuildMessage(message, state, exceptionObject))
{
    public MRubyState State { get; } = state;
    public RException ExceptionObject { get; } = exceptionObject;
    public int CallDepth { get; } = callDepth;

    public MRubyRaiseException(
        MRubyState state,
        RException exceptionObject,
        int callDepth)
        : this(exceptionObject.Message?.ToString() ?? "exception raised", state, exceptionObject, callDepth)
    {
    }

    // Embed the full mruby backtrace into `Message` so generic .NET log sinks
    // (Unity Console etc.) that surface only `Exception.Message` still show
    // where the raise originated. `base.ToString()` then naturally yields
    // "FullName: <message-with-backtrace>\n<C# stack>".
    static string BuildMessage(string message, MRubyState state, RException exceptionObject)
    {
        if (exceptionObject.Backtrace is not { Entries.Count: > 0 } bt) return message;
        var sb = new StringBuilder();
        sb.Append(message);
        sb.AppendLine();
        sb.Append("mruby backtrace:");
        foreach (var line in bt.ToString(state)
                     .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine();
            sb.Append("\tfrom ");
            sb.Append(line);
        }
        return sb.ToString();
    }

    public string GetMRubyStacktrace()
    {
        if (ExceptionObject.Backtrace is not { Entries.Count: > 0 } bt) return string.Empty;
        return bt.ToString(State);
    }
}