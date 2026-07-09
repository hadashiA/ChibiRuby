using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Compiler;

namespace ChibiRuby.Debugger;

public sealed class BreakpointInfo
{
    public int Line { get; init; }
    public bool Verified { get; init; }
    public string? Message { get; init; }
}

public enum StepMode
{
    StepIn,
    StepOver,
    StepOut,
}

sealed class StepRequest
{
    public StepMode Mode { get; init; }
    public int CallDepthAtRequest { get; init; }
}

/// <summary>
/// Protocol-agnostic debugger core. Implements <see cref="IMRubyDebuggerHook"/> on the VM
/// thread and exposes a command surface (<see cref="EvaluateAsync"/>, <see cref="Continue"/>,
/// <see cref="Disconnect"/>, <see cref="SetBreakpoints"/>) callable from any thread.
/// </summary>
public sealed class MRubyDebugger(MRubyState mrb, MRubyCompiler compiler) : IMRubyDebuggerHook, IDisposable
{
    IDebuggerClient? client;
    readonly ManualResetEventSlim clientReady = new(initialState: false);
    readonly object clientLock = new();

    BlockingCollection<DebugCommand>? commandQueue;
    RBinding? currentBinding;

    readonly Dictionary<string, HashSet<int>> breakpoints = new(StringComparer.Ordinal);
    readonly object breakpointLock = new();
    volatile int breakpointFileCount;

    bool evalInProgress;
    StepRequest? stepRequest;

    // Once any client has attached: subsequent stops with no client attached become no-op
    // (so binding.break doesn't hang the host thread after Rider/VSCode disconnects).
    bool hadAttachedClient;

    int disposed;

    public MRubyState State => mrb;
    public MRubyCompiler Compiler => compiler;
    public RBinding? CurrentBinding => currentBinding;
    public bool IsSuspended => commandQueue is not null;
    public IDebuggerClient? Client => client;
    public bool IsClientAttached => client is not null;

    public void AttachClient(IDebuggerClient newClient)
    {
        if (newClient is null) throw new ArgumentNullException(nameof(newClient));
        lock (clientLock)
        {
            if (client is not null)
            {
                throw new InvalidOperationException(
                    "A debugger client is already attached. Detach it before attaching a new one.");
            }
            client = newClient;
            hadAttachedClient = true;
            clientReady.Set();
        }
    }

    public void DetachClient()
    {
        IDebuggerClient? previous;
        BlockingCollection<DebugCommand>? queueToWake;
        lock (clientLock)
        {
            previous = client;
            queueToWake = commandQueue;
            client = null;
            clientReady.Reset();
        }
        if (previous is null) return;
        queueToWake?.TryAdd(ContinueCommand.Instance);
    }

    /// <summary>Install this debugger as <see cref="MRubyState.DebuggerHook"/>.</summary>
    public void Attach()
    {
        mrb.DebuggerHook = this;
    }

    public void Detach()
    {
        if (ReferenceEquals(mrb.DebuggerHook, this))
        {
            mrb.DebuggerHook = null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Detach();
        DetachClient();
        clientReady.Dispose();
        commandQueue?.Dispose();
    }

    /// <summary>
    /// Replace breakpoints for <paramref name="file"/>. Empty <paramref name="lines"/> clears.
    /// </summary>
    public IReadOnlyList<BreakpointInfo> SetBreakpoints(string file, ReadOnlySpan<int> lines)
    {
        if (string.IsNullOrEmpty(file)) throw new ArgumentException("file must be non-empty", nameof(file));

        var result = new List<BreakpointInfo>(lines.Length);
        lock (breakpointLock)
        {
            if (lines.IsEmpty)
            {
                if (breakpoints.Remove(file))
                {
                    breakpointFileCount = breakpoints.Count;
                }
            }
            else
            {
                var set = new HashSet<int>(lines.Length);
                foreach (var l in lines) set.Add(l);
                breakpoints[file] = set;
                breakpointFileCount = breakpoints.Count;
            }
        }
        foreach (var l in lines)
        {
            result.Add(new BreakpointInfo { Line = l, Verified = true });
        }
        return result;
    }

    public void ClearAllBreakpoints()
    {
        lock (breakpointLock)
        {
            breakpoints.Clear();
            breakpointFileCount = 0;
        }
    }

    static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    // Caller must hold breakpointLock. Tries exact, normalized, then tail-match on / boundary.
    bool TryMatchBreakpointFile(string dbgFilename, out HashSet<int>? bpLines)
    {
        if (breakpoints.TryGetValue(dbgFilename, out bpLines)) return true;

        var dbgNormalized = NormalizePath(dbgFilename);
        if (!ReferenceEquals(dbgNormalized, dbgFilename) &&
            breakpoints.TryGetValue(dbgNormalized, out bpLines))
        {
            return true;
        }

        foreach (var (stored, lines) in breakpoints)
        {
            var storedNormalized = NormalizePath(stored);
            if (string.Equals(storedNormalized, dbgNormalized, StringComparison.Ordinal) ||
                PathsLooselyEqual(stored, dbgFilename) ||
                PathsLooselyEqual(stored, dbgNormalized) ||
                PathsLooselyEqual(storedNormalized, dbgFilename) ||
                PathsLooselyEqual(storedNormalized, dbgNormalized))
            {
                bpLines = lines;
                return true;
            }
        }

        bpLines = null;
        return false;
    }

    static bool PathsLooselyEqual(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (a.Length < b.Length) (a, b) = (b, a);
        if (b.Length == 0) return false;
        if (!a.EndsWith(b, StringComparison.Ordinal)) return false;
        var sep = a[a.Length - b.Length - 1];
        return sep == '/' || sep == '\\';
    }

    void IMRubyDebuggerHook.OnBindingBreak(MRubyState state, RBinding binding)
    {
        SuspendInPump(StopReason.BindingBreak, binding);
    }

    void IMRubyDebuggerHook.OnInstruction(MRubyState state, Irep irep, int pc)
    {
        if (breakpointFileCount == 0 && stepRequest is null) return;
        if (evalInProgress) return;
        if (irep.DebugInfo is not { } dbg) return;

        // Only react at line boundaries — collapses multi-pc lines and avoids re-firing on
        // post-Send cleanup pcs that still map to the call-site line.
        var file = dbg.FindFile(pc);
        if (file is null) return;
        if (!file.IsLineBoundary(pc)) return;
        var line = file.FindLine(pc);
        if (line <= 0) return;
        var filename = file.Filename;

        var step = stepRequest;
        if (step is not null)
        {
            var depth = state.Context.CallDepth;
            var stop = step.Mode switch
            {
                StepMode.StepIn  => true,
                StepMode.StepOver => depth <= step.CallDepthAtRequest,
                StepMode.StepOut => depth < step.CallDepthAtRequest,
                _ => false,
            };
            if (stop)
            {
                stepRequest = null;
                var binding = state.CreateBindingForCurrentFrame();
                SuspendInPump(StopReason.Step, binding, filename, line);
                return;
            }
        }

        if (breakpointFileCount == 0) return;
        HashSet<int>? bpLines;
        lock (breakpointLock)
        {
            if (!TryMatchBreakpointFile(filename, out bpLines)) return;
        }
        if (!bpLines.Contains(line)) return;

        var binding2 = state.CreateBindingForCurrentFrame();
        SuspendInPump(StopReason.LineBreakpoint, binding2, filename, line);
    }

    void SuspendInPump(StopReason reason, RBinding binding, string? file = null, int line = -1)
    {
        // First boot → block until first attach. After detach → skip (don't hang host thread).
        if (client is null)
        {
            if (hadAttachedClient) return;
            WaitForClient();
        }
        var c = client!;

        var queue = new BlockingCollection<DebugCommand>(boundedCapacity: 64);
        commandQueue = queue;
        currentBinding = binding;
        try
        {
            c.OnStopped(this, new StopEvent
            {
                Reason = reason,
                Binding = binding,
                File = file,
                Line = line,
            });

            while (true)
            {
                var cmd = queue.Take();
                switch (cmd)
                {
                    case EvalCommand eval:
                        HandleEval(binding, eval);
                        continue;
                    case ContinueCommand:
                    case DisconnectCommand:
                        return;
                }
            }
        }
        finally
        {
            try { client?.OnResumed(this); } catch { /* ignore */ }
            commandQueue = null;
            currentBinding = null;
            // Intentionally don't CompleteAdding/Dispose: races with MRubyDebugger.Dispose during teardown.
        }
    }

    void WaitForClient()
    {
        while (client is null)
        {
            clientReady.Wait();
        }
    }

    void HandleEval(RBinding binding, EvalCommand cmd)
    {
        // Suppress breakpoint hooks during REPL eval to avoid recursive re-suspends.
        var wasEvalInProgress = evalInProgress;
        evalInProgress = true;

        // mrc has no upper-scope hook for compilation; hydrate the eval scope at the source
        // level via a temporary global + per-local prefix that copies captured locals into
        // the eval's own scope. Read-only — assignments don't write back.
        var bindingGlobal = mrb.Intern("$__chibiruby_dbg_binding"u8);
        var hadPrevGlobal = mrb.GlobalVariableDefined(bindingGlobal);
        var prevGlobalValue = hadPrevGlobal ? mrb.GetGlobalVariable(bindingGlobal) : MRubyValue.Nil;
        mrb.SetGlobalVariable(bindingGlobal, new MRubyValue(binding));
        try
        {
            try
            {
                var wrappedSource = BuildBindingScopedSource(binding, cmd.Source);
                using var compilation = compiler.Compile(Encoding.UTF8.GetBytes(wrappedSource));
                if (compilation.HasError)
                {
                    var sb = new StringBuilder();
                    foreach (var d in compilation.Diagnostics)
                    {
                        sb.AppendLine($"{d.Severity}: {d.Message} (line {d.Line}, column {d.Column})");
                    }
                    cmd.Completion.SetResult(new EvalResult
                    {
                        Value = MRubyValue.Nil,
                        DisplayString = sb.ToString().TrimEnd(),
                        IsError = true,
                    });
                    return;
                }

                var irep = compilation.ToIrep();
                var proc = mrb.CreateProc(irep);

                var snapshot = mrb.SaveCallStateForSandbox();
                MRubyValue result;
                try
                {
                    result = mrb.Send(binding.Receiver, mrb.Intern("instance_eval"u8), proc);
                }
                catch (MRubyRaiseException ex)
                {
                    mrb.RestoreCallStateForSandbox(snapshot);
                    var inspected = mrb.Inspect(new MRubyValue(ex.ExceptionObject));
                    var head = Encoding.UTF8.GetString(inspected.AsSpan());
                    var bt = ex.ExceptionObject.Backtrace;
                    var errorDisplay = bt is null
                        ? head
                        : head + "\n" + bt.ToString(mrb).TrimEnd();
                    cmd.Completion.SetResult(new EvalResult
                    {
                        Value = new MRubyValue(ex.ExceptionObject),
                        DisplayString = errorDisplay,
                        IsError = true,
                    });
                    return;
                }

                var display = Encoding.UTF8.GetString(mrb.Inspect(result).AsSpan());
                cmd.Completion.SetResult(new EvalResult
                {
                    Value = result,
                    DisplayString = display,
                    IsError = false,
                });
            }
            catch (Exception ex)
            {
                cmd.Completion.SetException(ex);
            }
        }
        finally
        {
            if (hadPrevGlobal)
            {
                mrb.SetGlobalVariable(bindingGlobal, prevGlobalValue);
            }
            else
            {
                mrb.RemoveGlobalVariable(bindingGlobal, out _);
            }
            evalInProgress = wasEvalInProgress;
        }
    }

    string BuildBindingScopedSource(RBinding binding, string userSource)
    {
        var names = binding.LocalVariableNames;
        var sb = new StringBuilder();

        // Shadow Kernel#binding with the captured outer binding so the user's
        // `binding.local_variable_set / get / local_variables` operates on the outer
        // scope, not on a fresh binding for the eval scope. Skip the shadow if the
        // user's scope already has a local named `binding` (rare; collision falls back
        // to using `$__chibiruby_dbg_binding` directly).
        var bindingSym = mrb.Intern("binding"u8);
        var userHasBinding = false;
        foreach (var n in names) if (n == bindingSym) { userHasBinding = true; break; }
        if (!userHasBinding)
        {
            sb.Append("binding=$__chibiruby_dbg_binding;");
        }

        foreach (var name in names)
        {
            if (name == Symbol.Empty) continue;
            var ident = Encoding.UTF8.GetString(mrb.NameOf(name).AsSpan());
            if (!IsPlainIdentifier(ident)) continue;
            sb.Append(ident);
            sb.Append("=$__chibiruby_dbg_binding.local_variable_get(:");
            sb.Append(ident);
            sb.Append(");");
        }
        sb.Append(userSource);
        return sb.ToString();
    }

    static bool IsPlainIdentifier(string s)
    {
        if (s.Length == 0) return false;
        var c0 = s[0];
        if (c0 is not ('_' or >= 'a' and <= 'z')) return false;
        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (c is not ('_' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')) return false;
        }
        return true;
    }

    public Task<EvalResult> EvaluateAsync(string source)
    {
        var queue = commandQueue;
        if (queue is null)
        {
            return Task.FromException<EvalResult>(new InvalidOperationException("VM is not suspended; no binding to evaluate in."));
        }

        var tcs = new TaskCompletionSource<EvalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryAdd(new EvalCommand(source, tcs)))
        {
            tcs.TrySetException(new InvalidOperationException("Debugger command queue is closed."));
        }
        return tcs.Task;
    }

    public EvalResult Evaluate(string source) => EvaluateAsync(source).GetAwaiter().GetResult();

    public bool Continue()
    {
        var queue = commandQueue;
        if (queue is null) return false;
        return queue.TryAdd(ContinueCommand.Instance);
    }

    public bool Disconnect()
    {
        var queue = commandQueue;
        if (queue is null) return false;
        return queue.TryAdd(DisconnectCommand.Instance);
    }

    /// <summary>Arm a step request and resume. No-op when the VM isn't suspended.</summary>
    public bool Step(StepMode mode)
    {
        var queue = commandQueue;
        if (queue is null) return false;

        var depth = mrb.Context.CallDepth;
        stepRequest = new StepRequest { Mode = mode, CallDepthAtRequest = depth };

        return queue.TryAdd(ContinueCommand.Instance);
    }

    public bool StepIn() => Step(StepMode.StepIn);
    public bool StepOver() => Step(StepMode.StepOver);
    public bool StepOut() => Step(StepMode.StepOut);

    abstract class DebugCommand;

    sealed class EvalCommand(string source, TaskCompletionSource<EvalResult> completion) : DebugCommand
    {
        public string Source { get; } = source;
        public TaskCompletionSource<EvalResult> Completion { get; } = completion;
    }

    sealed class ContinueCommand : DebugCommand
    {
        public static readonly ContinueCommand Instance = new();
    }

    sealed class DisconnectCommand : DebugCommand
    {
        public static readonly DisconnectCommand Instance = new();
    }
}
