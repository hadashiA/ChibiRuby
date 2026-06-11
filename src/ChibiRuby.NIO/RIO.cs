using System.IO;

namespace ChibiRuby;

/// <summary>
/// Ruby <c>IO</c> instance backing a <see cref="System.IO.Stream"/>.
/// The stream is the integration surface: any <c>Stream</c> subclass works
/// (FileStream, MemoryStream, NetworkStream, or a host-provided wrapper
/// around UnityWebRequest, etc.). Whether the I/O is sync- or async-friendly
/// is determined entirely by the supplied stream's implementation.
/// </summary>
public class RIO(RClass klass, Stream stream, bool leaveOpen = false) : RObject(klass)
{
    /// <summary>
    /// Underlying byte stream. <c>null</c> after <see cref="Close"/>.
    /// </summary>
    public Stream? Stream { get; private set; } = stream;

    /// <summary>
    /// If true, <see cref="Close"/> will not dispose the underlying stream.
    /// Used for handles like <c>$stdout</c> that should outlive the RIO.
    /// </summary>
    public bool LeaveOpen { get; } = leaveOpen;

    public bool Closed => Stream is null;

    /// <summary>
    /// Detach + dispose the underlying stream (unless <see cref="LeaveOpen"/>).
    /// Safe to call multiple times.
    /// </summary>
    public void Close()
    {
        if (Stream is null) return;
        var s = Stream;
        Stream = null;
        if (!LeaveOpen) s.Dispose();
    }
}

/// <summary>
/// Ruby <c>File</c> instance — an <see cref="RIO"/> with the originating
/// path attached. <c>File.open</c> creates one; <c>$stdout</c> etc. do not.
/// </summary>
public sealed class RFile(RClass klass, Stream stream, string path, bool leaveOpen = false)
    : RIO(klass, stream, leaveOpen)
{
    public string Path { get; } = path;
}
