using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using ChibiRuby.Debugger.Dap.Protocol;

namespace ChibiRuby.Debugger.Dap.Tests;

/// <summary>
/// Client-side wrapper used by tests to drive a <see cref="MRubyDapServer"/> or
/// <see cref="MRubyDapMessageHandler"/> from outside. Frames messages over a pair of
/// <see cref="PipeReader"/> / <see cref="PipeWriter"/> (sourced from a NetworkStream for
/// TCP tests, or an in-memory Pipe pair for the test harness). Exposes per-command typed
/// helpers so tests don't have to touch JSON directly — everything goes through the
/// generated NoJsonSchema formatters.
/// </summary>
sealed class ClientSession
{
    readonly PipeReader reader;
    readonly PipeWriter writer;
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly SemaphoreSlim readLock = new(1, 1);
    readonly ConcurrentQueue<PendingEvent> events = new();
    readonly ConcurrentDictionary<int, byte[]> responses = new();
    int seq;

    public ClientSession(PipeReader reader, PipeWriter writer)
    {
        this.reader = reader;
        this.writer = writer;
    }

    int NextSeq() => Interlocked.Increment(ref seq);

    // --- Per-command typed helpers ----------------------------------------------------

    public Task<InitializeResponse> InitializeAsync(string adapterId = "chibiruby")
    {
        var req = new InitializeRequest
        {
            Command = "initialize",
            Arguments = new InitializeRequestArguments { AdapterID = adapterId },
        };
        return SendRequestAsync<InitializeRequest, InitializeResponse>(req);
    }

    public Task<AttachResponse> AttachAsync()
    {
        var req = new AttachRequest
        {
            Command = "attach",
            Arguments = new AttachRequestArguments(),
        };
        return SendRequestAsync<AttachRequest, AttachResponse>(req);
    }

    /// <summary>
    /// Send a <c>launch</c> request with the adapter-specific <c>program</c> field.
    /// Generated <see cref="LaunchRequestArguments"/> doesn't know about <c>program</c>,
    /// so we hand-roll the body bytes and still go through the shared framer.
    /// </summary>
    public Task<LaunchResponse> LaunchAsync(string program)
    {
        var requestSeq = NextSeq();
        var body = BuildLaunchBody(requestSeq, program);
        return SendRawAsync<LaunchResponse>(requestSeq, body);
    }

    public Task<ConfigurationDoneResponse> ConfigurationDoneAsync()
    {
        var req = new ConfigurationDoneRequest
        {
            Command = "configurationDone",
            Arguments = new ConfigurationDoneArguments(),
        };
        return SendRequestAsync<ConfigurationDoneRequest, ConfigurationDoneResponse>(req);
    }

    public Task<SetBreakpointsResponse> SetBreakpointsAsync(string sourcePath, params int[] lines)
    {
        var bps = new SourceBreakpoint[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            bps[i] = new SourceBreakpoint { Line = (ulong)lines[i] };
        }
        var req = new SetBreakpointsRequest
        {
            Command = "setBreakpoints",
            Arguments = new SetBreakpointsArguments
            {
                Source = new Source { Path = sourcePath },
                Breakpoints = bps,
            },
        };
        return SendRequestAsync<SetBreakpointsRequest, SetBreakpointsResponse>(req);
    }

    public Task<StackTraceResponse> StackTraceAsync(int threadId)
    {
        var req = new StackTraceRequest
        {
            Command = "stackTrace",
            Arguments = new StackTraceArguments { ThreadId = threadId },
        };
        return SendRequestAsync<StackTraceRequest, StackTraceResponse>(req);
    }

    public Task<ScopesResponse> ScopesAsync(int frameId)
    {
        var req = new ScopesRequest
        {
            Command = "scopes",
            Arguments = new ScopesArguments { FrameId = frameId },
        };
        return SendRequestAsync<ScopesRequest, ScopesResponse>(req);
    }

    public Task<VariablesResponse> VariablesAsync(int variablesReference)
    {
        var req = new VariablesRequest
        {
            Command = "variables",
            Arguments = new VariablesArguments { VariablesReference = variablesReference },
        };
        return SendRequestAsync<VariablesRequest, VariablesResponse>(req);
    }

    public Task<EvaluateResponse> EvaluateAsync(string expression, string context = "repl")
    {
        var req = new EvaluateRequest
        {
            Command = "evaluate",
            Arguments = new EvaluateArguments { Expression = expression, Context = context },
        };
        return SendRequestAsync<EvaluateRequest, EvaluateResponse>(req);
    }

    public Task<ContinueResponse> ContinueAsync(int threadId)
    {
        var req = new ContinueRequest
        {
            Command = "continue",
            Arguments = new ContinueArguments { ThreadId = threadId },
        };
        return SendRequestAsync<ContinueRequest, ContinueResponse>(req);
    }

    public Task<NextResponse> NextAsync(int threadId)
    {
        var req = new NextRequest
        {
            Command = "next",
            Arguments = new NextArguments { ThreadId = threadId },
        };
        return SendRequestAsync<NextRequest, NextResponse>(req);
    }

    public Task<StepInResponse> StepInAsync(int threadId)
    {
        var req = new StepInRequest
        {
            Command = "stepIn",
            Arguments = new StepInArguments { ThreadId = threadId },
        };
        return SendRequestAsync<StepInRequest, StepInResponse>(req);
    }

    // --- Event waiters ----------------------------------------------------------------

    /// <summary>
    /// Wait for an event named <paramref name="eventName"/> and deserialize its raw bytes
    /// into <typeparamref name="TEvent"/>. Buffered events (received while we were waiting
    /// for a response) are checked first.
    /// </summary>
    public async Task<TEvent> WaitForEventAsync<TEvent>(string eventName, int timeoutMs = 10000)
        where TEvent : Event
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (true)
        {
            if (TryDequeueEvent(eventName, out var bytes))
            {
                return ProtocolSerializer.Deserialize<TEvent>(bytes);
            }
            var remaining = deadline - Environment.TickCount;
            if (remaining <= 0) throw new TimeoutException($"Timed out waiting for event '{eventName}'");
            await PumpOnceAsync(remaining).ConfigureAwait(false);
        }
    }

    /// <summary>Untyped overload: returns the envelope only (Type / EventValue / Seq).</summary>
    public Task<Event> WaitForEventAsync(string eventName, int timeoutMs = 10000)
        => WaitForEventAsync<Event>(eventName, timeoutMs);

    // --- Wire pump --------------------------------------------------------------------

    /// <summary>
    /// Generic helper: serialize <paramref name="request"/> as <typeparamref name="TRequest"/>
    /// (so the formatter dispatch picks the concrete request shape), then await the
    /// matching response deserialized as <typeparamref name="TResponse"/>.
    /// </summary>
    async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request)
        where TRequest : Request
        where TResponse : Response
    {
        request.Seq = NextSeq();
        request.Type = "request";
        var body = new ArrayBufferWriter<byte>(256);
        ProtocolSerializer.Serialize(body, request);
        await SendFramedAsync(body.WrittenMemory).ConfigureAwait(false);
        return await WaitForResponseAsync<TResponse>(request.Seq).ConfigureAwait(false);
    }

    async Task<TResponse> SendRawAsync<TResponse>(int requestSeq, byte[] body)
        where TResponse : Response
    {
        await SendFramedAsync(body).ConfigureAwait(false);
        return await WaitForResponseAsync<TResponse>(requestSeq).ConfigureAwait(false);
    }

    async ValueTask SendFramedAsync(ReadOnlyMemory<byte> body)
    {
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            WriteFrame(writer, body.Span);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    async Task<TResponse> WaitForResponseAsync<TResponse>(int requestSeq) where TResponse : Response
    {
        while (true)
        {
            if (responses.TryRemove(requestSeq, out var bytes))
            {
                return ProtocolSerializer.Deserialize<TResponse>(bytes);
            }
            await PumpOnceAsync(timeoutMs: 30000).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Read one message off the wire. Routes responses into <see cref="responses"/> (keyed
    /// by request_seq) and events into the <see cref="events"/> queue. Multiple callers can
    /// race; the <see cref="readLock"/> serialises the actual pipe read.
    /// </summary>
    async Task PumpOnceAsync(int timeoutMs)
    {
        await readLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cancellation = new CancellationTokenSource(timeoutMs);
            byte[]? bytes;
            try
            {
                bytes = await ReadFrameBytesAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Timed out waiting for DAP message");
            }
            if (bytes is null) throw new EndOfStreamException();
            // Cheap envelope peek: Response formatter ignores unknown fields (events have
            // `event` / no `command`), so this succeeds for both response and event.
            var envelope = ProtocolSerializer.Deserialize<Response>(bytes);
            if (envelope.Type == "response")
            {
                responses[envelope.RequestSeq] = bytes;
            }
            else if (envelope.Type == "event")
            {
                var evt = ProtocolSerializer.Deserialize<Event>(bytes);
                events.Enqueue(new PendingEvent(evt.EventValue, bytes));
            }
        }
        finally
        {
            readLock.Release();
        }
    }

    bool TryDequeueEvent(string eventName, out byte[] bytes)
    {
        // Drain matching events in order; non-matching events go back at the tail.
        var seen = 0;
        var total = events.Count;
        while (seen < total && events.TryDequeue(out var pending))
        {
            if (pending.EventName == eventName)
            {
                bytes = pending.Bytes;
                return true;
            }
            events.Enqueue(pending);
            seen++;
        }
        bytes = Array.Empty<byte>();
        return false;
    }

    // --- Hand-rolled launch body ------------------------------------------------------

    /// <summary>
    /// Build the raw UTF-8 JSON body for a <c>launch</c> request carrying the
    /// adapter-specific <c>program</c> field. We do this by hand because the generated
    /// <see cref="LaunchRequestArguments"/> only models the standard DAP fields
    /// (<c>noDebug</c>, <c>restart</c>); <c>program</c> would be silently dropped.
    /// </summary>
    static byte[] BuildLaunchBody(int requestSeq, string program)
    {
        var sb = new StringBuilder();
        sb.Append("{\"seq\":").Append(requestSeq)
          .Append(",\"type\":\"request\",\"command\":\"launch\",\"arguments\":{\"program\":\"");
        AppendJsonString(sb, program);
        sb.Append("\"}}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Minimal JSON-string escaper for inside-quotes content. Handles the structural
    /// escapes plus the control-character \uXXXX form — enough for filesystem paths and
    /// the trivial test inputs this helper sees.
    /// </summary>
    static void AppendJsonString(StringBuilder sb, string s)
    {
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
    }

    // --- DAP wire framing (Content-Length: N\r\n\r\n<body>) --------------------------

    static ReadOnlySpan<byte> HeaderTerminator => "\r\n\r\n"u8;
    static ReadOnlySpan<byte> ContentLengthHeader => "Content-Length:"u8;
    static ReadOnlySpan<byte> ContentLengthPrefix => "Content-Length: "u8;

    /// <summary>
    /// Read one framed DAP message off <see cref="reader"/> and return the raw UTF-8
    /// body bytes. Tests don't bother pooling — each message gets its own byte[] which
    /// then lives in the response/event queue until a caller consumes it.
    /// </summary>
    async Task<byte[]?> ReadFrameBytesAsync(CancellationToken cancellationToken)
    {
        int contentLength;
        while (true)
        {
            ReadResult readResult;
            try { readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (System.IO.IOException) { return null; }

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
            throw new System.IO.InvalidDataException("DAP message missing Content-Length header");
        }
        if (contentLength == 0) return Array.Empty<byte>();

        while (true)
        {
            var readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = readResult.Buffer;
            if (buffer.Length >= contentLength)
            {
                var bytes = new byte[contentLength];
                buffer.Slice(0, contentLength).CopyTo(bytes);
                reader.AdvanceTo(buffer.GetPosition(contentLength));
                return bytes;
            }
            if (readResult.IsCompleted) { reader.AdvanceTo(buffer.End); return null; }
            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    static void WriteFrame(PipeWriter outputWriter, ReadOnlySpan<byte> body)
    {
        // Header: "Content-Length: <N>\r\n\r\n" + body, all in one GetSpan grab.
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

    readonly struct PendingEvent
    {
        public string EventName { get; }
        public byte[] Bytes { get; }
        public PendingEvent(string name, byte[] bytes) { EventName = name; Bytes = bytes; }
    }
}
