using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChibiRuby.Compiler;
using ChibiRuby.Debugger.Dap.Protocol;

namespace ChibiRuby.Debugger.Dap;

/// <summary>
/// Per-session DAP protocol handler. Transport-agnostic (operates on a PipeReader/Writer pair).
/// <see cref="MRubyDapServer"/> spawns one of these per accepted TCP client.
/// </summary>
public sealed class MRubyDapMessageHandler : IDebuggerClient, IDisposable
{
    enum HandshakePhase
    {
        PreInitialize,
        Initialized,
        Active,
    }

    public MRubyState State { get; }
    public MRubyCompiler Compiler { get; }
    public MRubyDebugger Debugger { get; }

    const int ThreadId = 1;
    const int FrameIdBindingBreak = 1;
    const int VariablesRefLocals = 1000;

    readonly PipeReader reader;
    readonly PipeWriter writer;
    readonly IDisposable? subsystem;
    readonly bool ownsDebugger;
    readonly Func<string, CancellationToken, Task>? onLaunch;
    readonly LogDelegate? log;
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly CancellationTokenSource lifecycle = new();

    HandshakePhase phase = HandshakePhase.PreInitialize;
    StopEvent? pendingStopEvent;
    string? scriptPath;
    RBinding? currentBinding;
    int nextSeq;
    readonly System.Collections.Generic.HashSet<string> warnedMissingSourcePaths = new(StringComparer.Ordinal);

    /// <summary>Wire to stdio (editor-spawned child-process model). Owns the debugger it creates.</summary>
    public static MRubyDapMessageHandler StdioListen(
        MRubyState state,
        MRubyCompiler compiler,
        Func<string, CancellationToken, Task>? onLaunch = null,
        LogDelegate? log = null)
    {
        var debugger = new MRubyDebugger(state, compiler);
        debugger.Attach();
        var reader = PipeReader.Create(Console.OpenStandardInput());
        var writer = PipeWriter.Create(Console.OpenStandardOutput());
        return new MRubyDapMessageHandler(debugger, reader, writer, subsystem: null, onLaunch, log, ownsDebugger: true);
    }

    /// <summary>
    /// Caller-supplied pipe pair. <paramref name="subsystem"/> is disposed alongside the handler
    /// (e.g. a per-connection TcpClient). The <paramref name="debugger"/> is caller-owned.
    /// </summary>
    public MRubyDapMessageHandler(
        MRubyDebugger debugger,
        PipeReader reader,
        PipeWriter writer,
        IDisposable? subsystem = null,
        Func<string, CancellationToken, Task>? onLaunch = null,
        LogDelegate? log = null)
        : this(debugger, reader, writer, subsystem, onLaunch, log, ownsDebugger: false)
    {
    }

    MRubyDapMessageHandler(
        MRubyDebugger debugger,
        PipeReader reader,
        PipeWriter writer,
        IDisposable? subsystem,
        Func<string, CancellationToken, Task>? onLaunch,
        LogDelegate? log,
        bool ownsDebugger)
    {
        State = debugger.State;
        Compiler = debugger.Compiler;
        Debugger = debugger;
        this.reader = reader;
        this.writer = writer;
        this.subsystem = subsystem;
        this.onLaunch = onLaunch;
        this.log = log;
        this.ownsDebugger = ownsDebugger;
        debugger.AttachClient(this);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifecycle.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                Request? request;
                try
                {
                    request = await ReadRequestAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                catch (NoJsonFormatException ex)
                {
                    log?.Invoke(LogLevel.Warning, $"[dap] malformed message: {ex.Message}", ex);
                    break;
                }

                if (request is null) break;
                await DispatchAsync(request, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    /// <summary>Send an <c>output</c> event.</summary>
    public Task NotifyOutputAsync(string category, string text, CancellationToken cancellationToken = default)
    {
        var evt = new OutputEvent
        {
            Seq = NextSeq(),
            Type = "event",
            EventValue = "output",
            Body = new OutputEventBody
            {
                Category = category,
                Output = text,
            },
        };
        return SendAsync(evt, cancellationToken).AsTask();
    }

    /// <summary>
    /// Send a <c>terminated</c> event. <paramref name="restart"/>=true hints spec-compliant
    /// clients (VSCode etc.) to auto-reconnect on the next session.
    /// </summary>
    public Task NotifyTerminatedAsync(bool restart = false, CancellationToken cancellationToken = default)
    {
        var evt = new TerminatedEvent
        {
            Seq = NextSeq(),
            Type = "event",
            EventValue = "terminated",
            Body = restart ? new TerminatedEventBody(true) : null,
        };
        return SendAsync(evt, cancellationToken).AsTask();
    }

    async Task DispatchAsync(Request request, CancellationToken cancellationToken)
    {
        var seq = request.Seq;
        try
        {
            switch (request)
            {
                case InitializeRequest:
                    await HandleInitializeAsync(seq, cancellationToken).ConfigureAwait(false);
                    break;
                case LaunchRequest launch:
                    await HandleLaunchAsync(seq, launch.Arguments?.Program, cancellationToken).ConfigureAwait(false);
                    break;
                case AttachRequest attach:
                    await HandleAttachAsync(seq, attach.Arguments?.Program, cancellationToken).ConfigureAwait(false);
                    break;
                case ConfigurationDoneRequest:
                    await RespondSuccessAsync(new ConfigurationDoneResponse
                    {
                        Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "configurationDone",
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case SetBreakpointsRequest bp:
                    await HandleSetBreakpointsAsync(seq, bp.Arguments, cancellationToken).ConfigureAwait(false);
                    break;
                case ThreadsRequest:
                    await HandleThreadsAsync(seq, cancellationToken).ConfigureAwait(false);
                    break;
                case StackTraceRequest:
                    await HandleStackTraceAsync(seq, cancellationToken).ConfigureAwait(false);
                    break;
                case ScopesRequest:
                    await HandleScopesAsync(seq, cancellationToken).ConfigureAwait(false);
                    break;
                case VariablesRequest v:
                    await HandleVariablesAsync(seq, v.Arguments, cancellationToken).ConfigureAwait(false);
                    break;
                case EvaluateRequest e:
                    await HandleEvaluateAsync(seq, e.Arguments, cancellationToken).ConfigureAwait(false);
                    break;
                case ContinueRequest:
                    await HandleContinueAsync(seq, cancellationToken).ConfigureAwait(false);
                    break;
                case NextRequest:
                    await HandleStepAsync(seq, "next", StepMode.StepOver, cancellationToken).ConfigureAwait(false);
                    break;
                case StepInRequest:
                    await HandleStepAsync(seq, "stepIn", StepMode.StepIn, cancellationToken).ConfigureAwait(false);
                    break;
                case StepOutRequest:
                    await HandleStepAsync(seq, "stepOut", StepMode.StepOut, cancellationToken).ConfigureAwait(false);
                    break;
                case PauseRequest:
                    await RespondErrorAsync(seq, "pause", "pause is not supported in Phase 1; use binding.break", cancellationToken).ConfigureAwait(false);
                    break;
                case DisconnectRequest:
                    await HandleDisconnectAsync(seq, "disconnect", cancellationToken).ConfigureAwait(false);
                    break;
                case TerminateRequest:
                    await HandleDisconnectAsync(seq, "terminate", cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await RespondErrorAsync(seq, request.Command, $"command not supported: {request.Command}", cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke(LogLevel.Error, $"[dap] handler crashed for {request.Command}: {ex}", ex);
            await RespondErrorAsync(seq, request.Command, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    Task HandleInitializeAsync(int seq, CancellationToken cancellationToken)
    {
        var response = new InitializeResponse
        {
            Seq = NextSeq(),
            Type = "response",
            RequestSeq = seq,
            Success = true,
            Command = "initialize",
            Body = new Capabilities
            {
                SupportsConfigurationDoneRequest = true,
                SupportsEvaluateForHovers = true,
                SupportsTerminateRequest = true,
                SupportsFunctionBreakpoints = false,
                SupportsConditionalBreakpoints = false,
                SupportsStepBack = false,
                SupportsRestartRequest = false,
            },
        };
        phase = HandshakePhase.Initialized;
        // Shared writeLock ensures the response precedes the initialized event on the wire.
        var responseTask = RespondSuccessAsync(response, cancellationToken);
        var initializedEvent = new InitializedEvent
        {
            Seq = NextSeq(),
            Type = "event",
            EventValue = "initialized",
        };
        var eventTask = SendEventAsync(initializedEvent, cancellationToken);
        return Task.WhenAll(responseTask, eventTask);
    }

    async Task HandleLaunchAsync(int seq, string? program, CancellationToken cancellationToken)
    {
        if (onLaunch is null)
        {
            await RespondErrorAsync(seq, "launch", "launch is not supported; this adapter is in attach mode", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrEmpty(program))
        {
            await RespondErrorAsync(seq, "launch", "launch requires a 'program' argument", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!File.Exists(program))
        {
            await RespondErrorAsync(seq, "launch", $"program not found: {program}", cancellationToken).ConfigureAwait(false);
            return;
        }
        scriptPath = program;
        try
        {
            await onLaunch(program, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RespondErrorAsync(seq, "launch", $"launch failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return;
        }
        phase = HandshakePhase.Active;
        await RespondSuccessAsync(new LaunchResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "launch",
        }, cancellationToken).ConfigureAwait(false);
        await FlushPendingStopAsync().ConfigureAwait(false);
    }

    async Task HandleAttachAsync(int seq, string? program, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(program) && File.Exists(program))
        {
            scriptPath = program;
        }
        phase = HandshakePhase.Active;
        await RespondSuccessAsync(new AttachResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "attach",
        }, cancellationToken).ConfigureAwait(false);
        await FlushPendingStopAsync().ConfigureAwait(false);
    }

    Task HandleThreadsAsync(int seq, CancellationToken cancellationToken) =>
        RespondSuccessAsync(new ThreadsResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "threads",
            Body = new ThreadsResponseBody
            {
                Threads = [new Protocol.Thread { Id = ThreadId, Name = "main" }],
            },
        }, cancellationToken);

    async Task HandleSetBreakpointsAsync(int seq, SetBreakpointsArguments args, CancellationToken cancellationToken)
    {
        var sourcePath = args.Source?.Path;
        if (string.IsNullOrEmpty(sourcePath))
        {
            await RespondErrorAsync(seq, "setBreakpoints", "setBreakpoints requires source.path", cancellationToken).ConfigureAwait(false);
            return;
        }
        var bpsArg = args.Breakpoints;
        var lineList = new System.Collections.Generic.List<int>(bpsArg?.Length ?? 0);
        if (bpsArg is not null)
        {
            foreach (var entry in bpsArg)
            {
                lineList.Add(checked((int)entry.Line));
            }
        }
        var ack = Debugger.SetBreakpoints(sourcePath, lineList.ToArray());

        var bpArray = new Breakpoint[ack.Count];
        for (var i = 0; i < ack.Count; i++)
        {
            var info = ack[i];
            bpArray[i] = new Breakpoint
            {
                Verified = info.Verified,
                Line = (ulong)info.Line,
                Message = info.Message,
            };
        }
        await RespondSuccessAsync(new SetBreakpointsResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "setBreakpoints",
            Body = new SetBreakpointsResponseBody { Breakpoints = bpArray },
        }, cancellationToken).ConfigureAwait(false);
    }

    Task HandleStackTraceAsync(int seq, CancellationToken cancellationToken)
    {
        var binding = currentBinding;
        var frames = new System.Collections.Generic.List<StackFrame>(1);
        if (binding is not null)
        {
            string? sourcePath = null;
            var line = 1;
            if (binding.TryGetSourcePosition(out var dbgFile, out var dbgLine))
            {
                sourcePath = ResolveSourcePath(dbgFile);
                line = dbgLine;
            }
            sourcePath ??= scriptPath;

            var frame = new StackFrame
            {
                Id = FrameIdBindingBreak,
                Name = sourcePath is null ? "(toplevel)" : Path.GetFileName(sourcePath),
                Line = (ulong)line,
                Column = 1,
            };
            if (sourcePath is not null)
            {
                // Don't send Source.Path for files not on disk — VSCode would otherwise show
                // "Could not load source 'X': Canceled" on every stop.
                var source = new Source { Name = Path.GetFileName(sourcePath) };
                if (File.Exists(sourcePath))
                {
                    source.Path = sourcePath;
                }
                else
                {
                    source.PresentationHint = SourcePresentationHint.Deemphasize;
                    if (warnedMissingSourcePaths.Add(sourcePath))
                    {
                        log?.Invoke(LogLevel.Warning,
                            $"[dap] source path does not exist on disk: '{sourcePath}'. " +
                            "The frame will show the file name only and won't navigate. " +
                            "Pass an existing absolute path as `filename:` to " +
                            "`MRubyCompiler.Compile(...)` so the editor can open the source.",
                            null);
                    }
                }
                frame.Source = source;
            }
            frames.Add(frame);
        }
        return RespondSuccessAsync(new StackTraceResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "stackTrace",
            Body = new StackTraceResponseBody
            {
                StackFrames = frames.ToArray(),
                TotalFrames = (uint)frames.Count,
            },
        }, cancellationToken);
    }

    string? ResolveSourcePath(string? dbgFilename)
    {
        if (string.IsNullOrEmpty(dbgFilename)) return null;
        if (Path.IsPathRooted(dbgFilename)) return dbgFilename;
        if (scriptPath is not null)
        {
            var baseDir = Path.GetDirectoryName(scriptPath);
            if (!string.IsNullOrEmpty(baseDir))
            {
                return Path.GetFullPath(Path.Combine(baseDir, dbgFilename));
            }
        }
        return dbgFilename;
    }

    Task HandleScopesAsync(int seq, CancellationToken cancellationToken) =>
        RespondSuccessAsync(new ScopesResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "scopes",
            Body = new ScopesResponseBody
            {
                Scopes = new[]
                {
                    new Scope { Name = "Locals", VariablesReference = VariablesRefLocals, Expensive = false },
                },
            },
        }, cancellationToken);

    async Task HandleVariablesAsync(int seq, VariablesArguments args, CancellationToken cancellationToken)
    {
        var refId = args.VariablesReference;
        var variables = new System.Collections.Generic.List<Variable>();
        var binding = currentBinding;
        if (refId == VariablesRefLocals && binding is not null)
        {
            variables.Add(new Variable
            {
                Name = "self",
                Value = SafeInspect(binding.Receiver),
                Type = binding.Receiver.VType.ToString(),
                VariablesReference = 0,
            });
            var names = binding.LocalVariableNames;
            var values = binding.LocalVariableValues;
            for (var i = 0; i < names.Length; i++)
            {
                var name = State.NameOf(names[i]);
                var value = values[i];
                variables.Add(new Variable
                {
                    Name = Encoding.UTF8.GetString(name.AsSpan()),
                    Value = SafeInspect(value),
                    Type = value.VType.ToString(),
                    VariablesReference = 0,
                });
            }
        }
        await RespondSuccessAsync(new VariablesResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "variables",
            Body = new VariablesResponseBody { Variables = variables.ToArray() },
        }, cancellationToken).ConfigureAwait(false);
    }

    string SafeInspect(MRubyValue value)
    {
        if (!Debugger.IsSuspended) return value.VType.ToString();
        try
        {
            var inspected = State.Inspect(value);
            return Encoding.UTF8.GetString(inspected.AsSpan());
        }
        catch
        {
            return value.VType.ToString();
        }
    }

    async Task HandleEvaluateAsync(int seq, EvaluateArguments args, CancellationToken cancellationToken)
    {
        var expression = args.Expression;
        if (string.IsNullOrEmpty(expression))
        {
            await RespondErrorAsync(seq, "evaluate", "evaluate requires 'expression'", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!Debugger.IsSuspended)
        {
            await RespondErrorAsync(seq, "evaluate", "VM is not suspended; nothing to evaluate against", cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await Debugger.EvaluateAsync(expression).ConfigureAwait(false);
        if (result.IsError)
        {
            await RespondErrorAsync(seq, "evaluate", result.DisplayString, cancellationToken).ConfigureAwait(false);
            return;
        }
        await RespondSuccessAsync(new EvaluateResponse
        {
            Seq = NextSeq(),
            Type = "response",
            RequestSeq = seq,
            Success = true,
            Command = "evaluate",
            Body = new EvaluateResponseBody
            {
                Result = result.DisplayString,
                Type = result.Value.VType.ToString(),
                VariablesReference = 0,
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    Task HandleContinueAsync(int seq, CancellationToken cancellationToken)
    {
        Debugger.Continue();
        return RespondSuccessAsync(new ContinueResponse
        {
            Seq = NextSeq(), Type = "response", RequestSeq = seq, Success = true, Command = "continue",
            Body = new ContinueResponseBody { AllThreadsContinued = true },
        }, cancellationToken);
    }

    async Task HandleStepAsync(int seq, string command, StepMode mode, CancellationToken cancellationToken)
    {
        if (!Debugger.Step(mode))
        {
            await RespondErrorAsync(seq, command, "VM is not suspended; cannot step", cancellationToken).ConfigureAwait(false);
            return;
        }
        await RespondSuccessAsync(new Response
        {
            Seq = NextSeq(),
            Type = "response",
            RequestSeq = seq,
            Success = true,
            Command = command,
        }, cancellationToken).ConfigureAwait(false);
    }

    async Task HandleDisconnectAsync(int seq, string command, CancellationToken cancellationToken)
    {
        try
        {
            Debugger.Disconnect();
            await RespondSuccessAsync(new Response
            {
                Seq = NextSeq(),
                Type = "response",
                RequestSeq = seq,
                Success = true,
                Command = command,
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Cancel();
        }
    }

    void IDebuggerClient.OnStopped(MRubyDebugger sender, StopEvent stopEvent)
    {
        currentBinding = stopEvent.Binding;
        if (phase >= HandshakePhase.Active)
        {
            _ = SendStoppedEventAsync(stopEvent);
        }
        else
        {
            pendingStopEvent = stopEvent;
        }
    }

    void IDebuggerClient.OnResumed(MRubyDebugger sender)
    {
        currentBinding = null;
        var evt = new ContinuedEvent
        {
            Seq = NextSeq(),
            Type = "event",
            EventValue = "continued",
            Body = new ContinuedEventBody
            {
                ThreadId = ThreadId,
                AllThreadsContinued = true,
            },
        };
        _ = SendEventAsync(evt, CancellationToken.None);
    }

    Task SendStoppedEventAsync(StopEvent stopEvent)
    {
        var (reason, description) = stopEvent.Reason switch
        {
            StopReason.LineBreakpoint => ("breakpoint", stopEvent.Line > 0
                ? $"Paused at line breakpoint {Path.GetFileName(stopEvent.File ?? "")}:{stopEvent.Line}"
                : "Paused at line breakpoint"),
            StopReason.BindingBreak => ("pause", "Paused at binding.break"),
            StopReason.Step => ("step", stopEvent.Line > 0
                ? $"Paused after step at {Path.GetFileName(stopEvent.File ?? "")}:{stopEvent.Line}"
                : "Paused after step"),
            _ => ("pause", "Paused"),
        };
        var evt = new StoppedEvent
        {
            Seq = NextSeq(),
            Type = "event",
            EventValue = "stopped",
            Body = new StoppedEventBody
            {
                Reason = reason,
                Description = description,
                ThreadId = ThreadId,
                AllThreadsStopped = true,
            },
        };
        return SendEventAsync(evt, CancellationToken.None);
    }

    async Task FlushPendingStopAsync()
    {
        if (pendingStopEvent is { } stopEvent)
        {
            pendingStopEvent = null;
            await SendStoppedEventAsync(stopEvent).ConfigureAwait(false);
        }
    }

    Task RespondSuccessAsync<T>(T response, CancellationToken cancellationToken) where T : Response =>
        SendAsync(response, cancellationToken).AsTask();

    Task RespondErrorAsync(int requestSeq, string command, string message, CancellationToken cancellationToken)
    {
        var response = new Response
        {
            Seq = NextSeq(),
            Type = "response",
            RequestSeq = requestSeq,
            Success = false,
            Command = command,
            Message = message,
        };
        return SendAsync(response, cancellationToken).AsTask();
    }

    Task SendEventAsync<T>(T evt, CancellationToken cancellationToken) where T : Event =>
        SendAsync(evt, cancellationToken).AsTask();

    async ValueTask SendAsync<T>(T message, CancellationToken cancellationToken)
    {
        // Buffer body first — Content-Length must precede it on the wire.
        var body = new ArrayBufferWriter<byte>(256);
        ProtocolSerializer.Serialize(body, message);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WriteFrame(writer, body.WrittenSpan);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    int NextSeq() => Interlocked.Increment(ref nextSeq);

    public void Dispose()
    {
        try { lifecycle.Cancel(); } catch { /* already disposed */ }
        Debugger.DetachClient();
        try { reader.CompleteAsync().AsTask().Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        try { writer.CompleteAsync().AsTask().Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        subsystem?.Dispose();
        if (ownsDebugger)
        {
            Debugger.Dispose();
        }
        writeLock.Dispose();
        lifecycle.Dispose();
    }

    // --- DAP wire framing (Content-Length: N\r\n\r\n<body>) --------------------------

    static ReadOnlySpan<byte> HeaderTerminator => "\r\n\r\n"u8;
    static ReadOnlySpan<byte> ContentLengthHeader => "Content-Length:"u8;
    static ReadOnlySpan<byte> ContentLengthPrefix => "Content-Length: "u8;

    async ValueTask<Request?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        int contentLength;
        while (true)
        {
            ReadResult readResult;
            try { readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (IOException) { return null; }

            var buffer = readResult.Buffer;
            if (TryFindHeaderTerminator(buffer, out var headerEnd))
            {
                contentLength = ParseContentLength(buffer.Slice(0, headerEnd));
                reader.AdvanceTo(buffer.GetPosition(headerEnd + HeaderTerminator.Length));
                break;
            }
            if (readResult.IsCompleted) { reader.AdvanceTo(buffer.End); return null; }
            reader.AdvanceTo(buffer.Start, buffer.End);
        }

        if (contentLength < 0)
        {
            throw new InvalidDataException("DAP message missing Content-Length header");
        }
        if (contentLength == 0) return null;

        while (true)
        {
            var readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = readResult.Buffer;
            if (buffer.Length >= contentLength)
            {
                var rented = ArrayPool<byte>.Shared.Rent(contentLength);
                try
                {
                    buffer.Slice(0, contentLength).CopyTo(rented);
                    reader.AdvanceTo(buffer.GetPosition(contentLength));
                    return ProtocolSerializer.Deserialize<Request>(new ReadOnlySpan<byte>(rented, 0, contentLength));
                }
                finally { ArrayPool<byte>.Shared.Return(rented); }
            }
            if (readResult.IsCompleted) { reader.AdvanceTo(buffer.End); return null; }
            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    static void WriteFrame(PipeWriter outputWriter, ReadOnlySpan<byte> body)
    {
        var headerSpan = outputWriter.GetSpan(ContentLengthPrefix.Length + 20 + HeaderTerminator.Length);
        ContentLengthPrefix.CopyTo(headerSpan);
        var headerCursor = ContentLengthPrefix.Length;
        if (!Utf8Formatter.TryFormat(body.Length, headerSpan[headerCursor..], out var lengthWritten))
        {
            throw new InvalidOperationException("Utf8Formatter failed for content length");
        }
        headerCursor += lengthWritten;
        HeaderTerminator.CopyTo(headerSpan[headerCursor..]);
        headerCursor += HeaderTerminator.Length;
        outputWriter.Advance(headerCursor);

        var bodyTarget = outputWriter.GetSpan(body.Length);
        body.CopyTo(bodyTarget);
        outputWriter.Advance(body.Length);
    }

    static bool TryFindHeaderTerminator(in ReadOnlySequence<byte> buffer, out long position)
    {
        var sequenceReader = new SequenceReader<byte>(buffer);
        while (sequenceReader.TryReadTo(out ReadOnlySequence<byte> _, (byte)'\r', advancePastDelimiter: true))
        {
            if (sequenceReader.Remaining < 3) break;
            if (sequenceReader.TryRead(out var b1) && b1 == (byte)'\n' &&
                sequenceReader.TryRead(out var b2) && b2 == (byte)'\r' &&
                sequenceReader.TryRead(out var b3) && b3 == (byte)'\n')
            {
                position = sequenceReader.Consumed - HeaderTerminator.Length;
                return true;
            }
        }
        position = 0;
        return false;
    }

    static int ParseContentLength(in ReadOnlySequence<byte> headers)
    {
        var sequenceReader = new SequenceReader<byte>(headers);
        while (!sequenceReader.End)
        {
            if (!sequenceReader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n', advancePastDelimiter: true))
            {
                line = headers.Slice(sequenceReader.Position);
                sequenceReader.Advance(sequenceReader.Remaining);
            }
            if (line.Length > 0 && line.Slice(line.Length - 1).FirstSpan[0] == (byte)'\r')
            {
                line = line.Slice(0, line.Length - 1);
            }
            if (line.Length < ContentLengthHeader.Length) continue;
            Span<byte> prefix = stackalloc byte[ContentLengthHeader.Length];
            line.Slice(0, ContentLengthHeader.Length).CopyTo(prefix);
            if (!IsContentLengthHeader(prefix)) continue;

            var rest = line.Slice(ContentLengthHeader.Length);
            while (rest.Length > 0 && rest.FirstSpan[0] is (byte)' ' or (byte)'\t')
            {
                rest = rest.Slice(1);
            }
            Span<byte> digits = stackalloc byte[20];
            var digitCount = 0;
            foreach (var segment in rest)
            {
                for (var i = 0; i < segment.Length && digitCount < digits.Length; i++)
                {
                    var b = segment.Span[i];
                    if (b is < (byte)'0' or > (byte)'9') break;
                    digits[digitCount++] = b;
                }
            }
            if (digitCount == 0) continue;
            if (!Utf8Parser.TryParse(digits[..digitCount], out int value, out _)) continue;
            return value;
        }
        return -1;
    }

    static bool IsContentLengthHeader(ReadOnlySpan<byte> candidate)
    {
        if (candidate.Length != ContentLengthHeader.Length) return false;
        for (var i = 0; i < candidate.Length; i++)
        {
            var a = candidate[i];
            var b = ContentLengthHeader[i];
            if (a == b) continue;
            if (a is >= (byte)'A' and <= (byte)'Z') a += 32;
            if (b is >= (byte)'A' and <= (byte)'Z') b += 32;
            if (a != b) return false;
        }
        return true;
    }
}
