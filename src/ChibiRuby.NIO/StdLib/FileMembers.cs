using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace ChibiRuby.StdLib;

/// <summary>
/// Filesystem I/O -- a subclass of <c>IO</c> for reading and writing files on
/// disk. Opened via <c>File.open(path, mode)</c>; the block form
/// (<c>File.open(path) { |f| ... }</c>) auto-closes the handle. In ChibiRuby,
/// path/mode handling is delegated to the host runtime via .NET file APIs.
/// </summary>
[RubyClass("File", Superclass = "IO")]
static class FileMembers
{
    /// <summary>
    /// <c>File.open(path, mode = "r")</c> -- open the file, return an
    /// <see cref="RFile"/>.
    /// </summary>
    [RubyDef("(String, ?String) -> File")]
    public static MRubyValue Open(MRubyState state, MRubyValue self)
    {
        var path = PathArg(state);
        var mode = ModeArg(state);
        var (fileMode, fileAccess) = ResolveMode(state, mode);

        // useAsync only when a scheduler is installed (= async I/O might be
        // requested later). With no scheduler, IO#read/write go through the
        // synchronous Stream.Read/Write path so async overhead is wasted.
        var useAsync = state.FiberScheduler is not null;
        var stream = new FileStream(
            path,
            fileMode,
            fileAccess,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: useAsync);
        var fileClass = state.GetConst(state.Intern("File"u8)).As<RClass>();
        var rfile = new RFile(fileClass, stream, path);
        return new MRubyValue(rfile);
    }

    /// <summary>
    /// <c>File.read(path)</c> -- convenience: read whole file as a String.
    /// </summary>
    [RubyDef("(String) -> String")]
    public static MRubyValue Read(MRubyState state, MRubyValue self)
    {
        var path = PathArg(state);

        if (state.TryGetActiveFiberScheduler(out var scheduler))
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, useAsync: true);
            scheduler.Await(async mrb =>
            {
                try
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
                }
                finally { stream.Dispose(); }
            });
            return MRubyValue.Nil;
        }

        return state.NewStringOwned(File.ReadAllBytes(path));
    }

    /// <summary>
    /// <c>File.write(path, content)</c> -- convenience: replace file content.
    /// Returns the number of bytes written.
    /// </summary>
    [RubyDef("(String, String) -> Integer")]
    public static MRubyValue Write(MRubyState state, MRubyValue self)
    {
        var path = PathArg(state);
        var content = state.GetArgumentAsStringAt(1);
        var data = content.AsSpan().ToArray();

        if (state.TryGetActiveFiberScheduler(out var scheduler))
        {
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, useAsync: true);
            scheduler.Await(async _ =>
            {
                try
                {
                    await stream.WriteAsync(data);
                    return new MRubyValue((long)data.Length);
                }
                finally { stream.Dispose(); }
            });
            return MRubyValue.Nil;
        }

        File.WriteAllBytes(path, data);
        return new MRubyValue((long)data.Length);
    }

    /// <summary>
    /// Returns <c>true</c> if a file exists at the given path.
    /// </summary>
    /// <example>
    /// <code>
    /// File.exist?("config.rb")   # => true
    /// File.exist?("missing.rb")  # => false
    /// </code>
    /// </example>
    [RubyDef("(String) -> bool")]
    public static MRubyValue ExistQ(MRubyState state, MRubyValue self)
    {
        var path = PathArg(state);
        return File.Exists(path) ? MRubyValue.True : MRubyValue.False;
    }

    static string PathArg(MRubyState state)
        => Encoding.UTF8.GetString(state.GetArgumentAsStringAt(0).AsSpan());

    static string ModeArg(MRubyState state)
    {
        if (state.GetArgumentCount() < 2 || state.GetArgumentAt(1).IsNil) return "r";
        return Encoding.UTF8.GetString(state.GetArgumentAsStringAt(1).AsSpan());
    }

    static (FileMode, FileAccess) ResolveMode(MRubyState state, string mode)
    {
        // Ignore the "b" binary marker -- all I/O here is byte-oriented.
        var m = mode.Replace("b", "");
        return m switch
        {
            "r" => (FileMode.Open, FileAccess.Read),
            "r+" => (FileMode.Open, FileAccess.ReadWrite),
            "w" => (FileMode.Create, FileAccess.Write),
            "w+" => (FileMode.Create, FileAccess.ReadWrite),
            "a" => (FileMode.Append, FileAccess.Write),
            "a+" => (FileMode.Append, FileAccess.ReadWrite),
            _ => ((Func<(FileMode, FileAccess)>)(() =>
            {
                state.Raise(Names.ArgumentError, $"invalid file mode: {mode}");
                return default;
            }))()
        };
    }

}
