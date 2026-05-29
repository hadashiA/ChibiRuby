using System;
using System.Buffers;
using System.IO;

namespace ChibiRuby.StdLib;

/// <summary>
/// Base class for byte streams -- files, sockets, pipes, and the like. Provides
/// <c>read</c>, <c>write</c>, <c>gets</c>, <c>puts</c>, and friends. Subclassed
/// by <c>File</c> for filesystem access. In ChibiRuby, instances wrap a .NET
/// <c>Stream</c>.
/// </summary>
[RubyClass("IO")]
static class IOMembers
{
    /// <summary>
    /// Reads up to the given number of bytes from <c>self</c>; with no
    /// argument, reads to end-of-stream. Returns <c>nil</c> at EOF.
    /// </summary>
    /// <example>
    /// <code>
    /// f = File.open("data.txt")
    /// f.read(4)         # => "1234"
    /// f.read            # => "rest of file"
    /// </code>
    /// </example>
    [RubyDef("(?Integer?) -> String?")]
    public static MRubyValue Read(MRubyState state, MRubyValue self)
    {
        var io = self.As<RIO>();
        EnsureOpen(state, io);
        var stream = io.Stream!;
        var hasArg = state.GetArgumentCount() > 0 && !state.GetArgumentAt(0).IsNil;

        if (!hasArg)
        {
            if (state.TryGetActiveFiberScheduler(out var scheduler))
            {
                scheduler.Await(async mrb =>
                {
                    var writer = new ArrayBufferWriter<byte>();
                    while (true)
                    {
                        var mem = writer.GetMemory(4096);
                        var read = await stream.ReadAsync(mem);
                        if (read == 0) break;
                        writer.Advance(read);
                    }
                    return new MRubyValue(mrb.NewString(writer.WrittenSpan));
                });
                return MRubyValue.Nil;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return state.NewStringOwned(ms.ToArray());
        }

        var n = (int)state.GetArgumentAsIntegerAt(0);
        if (n < 0) state.Raise(Names.ArgumentError, "negative length"u8);
        if (n == 0) return state.NewString([]);

        if (state.TryGetActiveFiberScheduler(out var sched))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(n);
            sched.Await(async mrb =>
            {
                try
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, n));
                    return read == 0
                        ? MRubyValue.Nil
                        : new MRubyValue(mrb.NewString(buffer.AsSpan(0, read)));
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            });
            return MRubyValue.Nil;
        }

        var buf = new byte[n];
        var rd = stream.Read(buf, 0, n);
        return rd == 0
            ? MRubyValue.Nil
            : state.NewString(buf.AsSpan(0, rd));
    }

    /// <summary>
    /// Writes the given string to <c>self</c> and returns the number of bytes written.
    /// </summary>
    /// <example>
    /// <code>
    /// f = File.open("out.txt", "w")
    /// f.write("hello")    # => 5
    /// </code>
    /// </example>
    [RubyDef("(String) -> Integer")]
    public static MRubyValue Write(MRubyState state, MRubyValue self)
    {
        var io = self.As<RIO>();
        EnsureOpen(state, io);
        var stream = io.Stream!;

        var arg = state.GetArgumentAsStringAt(0);
        var bytes = arg.AsSpan();

        if (state.TryGetActiveFiberScheduler(out var scheduler))
        {
            // Copy because the source may outlive `bytes`'s lifetime once we
            // yield. Cheap relative to the syscall.
            var data = bytes.ToArray();
            scheduler.Await(async _ =>
            {
                await stream.WriteAsync(data);
                return new MRubyValue((long)data.Length);
            });
            return MRubyValue.Nil;
        }

        stream.Write(bytes);
        return new MRubyValue((long)bytes.Length);
    }

    /// <summary>
    /// Closes the underlying stream. Subsequent reads or writes raise <c>IOError</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// f = File.open("data.txt")
    /// f.close           # => nil
    /// </code>
    /// </example>
    [RubyDef("() -> nil")]
    public static MRubyValue Close(MRubyState state, MRubyValue self)
    {
        self.As<RIO>().Close();
        return MRubyValue.Nil;
    }

    /// <summary>
    /// Returns <c>true</c> if <c>self</c> has been closed.
    /// </summary>
    /// <example>
    /// <code>
    /// f = File.open("data.txt")
    /// f.closed?         # => false
    /// f.close
    /// f.closed?         # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue ClosedQ(MRubyState state, MRubyValue self)
    {
        return self.As<RIO>().Closed ? MRubyValue.True : MRubyValue.False;
    }

    static void EnsureOpen(MRubyState state, RIO io)
    {
        if (io.Closed)
        {
            state.Raise(state.Intern("IOError"u8), "closed stream"u8);
        }
    }
}
