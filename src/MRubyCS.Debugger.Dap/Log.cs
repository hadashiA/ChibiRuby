using System;

namespace MRubyCS.Debugger.Dap;

public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

public delegate void LogDelegate(
    LogLevel level,
    string message,
    Exception? exception);
