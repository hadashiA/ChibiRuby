using System;
using System.Collections.Generic;

namespace ChibiRuby;

/// <summary>Format of the per-pc line table inside an <see cref="IrepDebugInfoFileEntry"/>.</summary>
public enum DebugLineType : byte
{
    /// <summary>One <see cref="ushort"/> per pc (legacy, rarely emitted).</summary>
    Ary = 0,

    /// <summary>Array of <c>(uint32 start_pos, uint16 line)</c> pairs (legacy).</summary>
    FlatMap = 1,

    /// <summary>VLQ-packed delta encoding of (pc, line) — the format mruby uses today.</summary>
    PackedMap = 2,
}

/// <summary>
/// Per-Irep debug information recovered from the <c>DBG\0</c> section of a .mrb file.
/// Maps program-counter values back to source <c>(filename, line)</c> pairs.
/// </summary>
public sealed class IrepDebugInfo
{
    /// <summary>Number of bytecode positions covered by this debug info (typically <c>Irep.Sequence.Length</c>).</summary>
    public uint PcCount { get; init; }

    /// <summary>File entries covering disjoint pc ranges of this Irep. Sorted by <see cref="IrepDebugInfoFileEntry.StartPos"/> ascending.</summary>
    public IrepDebugInfoFileEntry[] Files { get; init; } = [];

    /// <summary>Look up the source line for a given program counter. Returns -1 if no entry covers it.</summary>
    public int FindLine(int pc)
    {
        var file = FindFile(pc);
        return file?.FindLine(pc) ?? -1;
    }

    /// <summary>Look up the source filename for a given program counter. Returns null if no entry covers it.</summary>
    public string? FindFilename(int pc) => FindFile(pc)?.Filename;

    /// <summary>Try to resolve both filename and line in one call.</summary>
    public bool TryFindPosition(int pc, out string? filename, out int line)
    {
        var file = FindFile(pc);
        if (file is null)
        {
            filename = null;
            line = -1;
            return false;
        }
        filename = file.Filename;
        line = file.FindLine(pc);
        return line > 0;
    }

    /// <summary>
    /// Binary-search-style lookup for the <see cref="IrepDebugInfoFileEntry"/> whose pc-range
    /// covers <paramref name="pc"/>. Mirrors the algorithm used by mruby's <c>debug.c</c>.
    /// </summary>
    public IrepDebugInfoFileEntry? FindFile(int pc)
    {
        if (pc < 0 || pc >= PcCount || Files.Length == 0) return null;
        // Upper-bound search: find the last file whose StartPos <= pc.
        var lo = 0;
        var hi = Files.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (Files[mid].StartPos <= pc) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return null;
        return Files[lo - 1];
    }
}

/// <summary>
/// Debug-info entry covering a single source file for some pc range of an Irep.
/// One Irep may contain multiple of these if a single function body was assembled from
/// several source files (extremely rare in mruby; the common case is one entry per Irep).
/// </summary>
public sealed class IrepDebugInfoFileEntry
{
    /// <summary>Inclusive pc at which this file entry starts being authoritative.</summary>
    public uint StartPos { get; init; }

    /// <summary>Source file name (UTF-8 decoded from the DBG section's filename table).</summary>
    public string Filename { get; init; } = "";

    /// <summary>Encoding of <see cref="LineData"/>.</summary>
    public DebugLineType LineType { get; init; }

    /// <summary>
    /// Per-spec entry count. Semantics depend on <see cref="LineType"/>:
    /// <list type="bullet">
    ///   <item><see cref="DebugLineType.Ary"/>: number of <see cref="ushort"/> entries.</item>
    ///   <item><see cref="DebugLineType.FlatMap"/>: number of (pc, line) pairs.</item>
    ///   <item><see cref="DebugLineType.PackedMap"/>: byte length of <see cref="LineData"/>.</item>
    /// </list>
    /// </summary>
    public uint LineEntryCount { get; init; }

    /// <summary>
    /// Raw bytes of the line table. Interpretation depends on <see cref="LineType"/>:
    /// for <see cref="DebugLineType.PackedMap"/> it's a sequence of VLQ-encoded
    /// (pc_delta, line_delta) pairs.
    /// </summary>
    public byte[] LineData { get; init; } = [];

    /// <summary>
    /// Return the source line corresponding to <paramref name="pc"/>, or -1 if outside
    /// this file's range or if the line type is not <see cref="DebugLineType.PackedMap"/>.
    /// </summary>
    /// <remarks>
    /// mruby's own runtime only decodes <see cref="DebugLineType.PackedMap"/>; the other
    /// types are accepted by the parser for completeness but not resolved here.
    /// </remarks>
    public int FindLine(int pc)
    {
        if (LineType != DebugLineType.PackedMap) return -1;
        var p = (ReadOnlySpan<byte>)LineData;
        uint pos = 0;
        uint line = 0;
        var i = 0;
        while (i < p.Length)
        {
            if (!TryDecodePackedInt(p, ref i, out var posDelta)) break;
            pos += posDelta;
            if (!TryDecodePackedInt(p, ref i, out var lineDelta)) break;
            if ((uint)pc < pos) break;
            line += lineDelta;
        }
        return (int)line;
    }

    // Cached set of pc values that begin a new source line (each entry in the packed_map
    // marks one such boundary). Computed on first access from <see cref="LineData"/>; used
    // by debuggers to filter "this pc is the first instruction of a line" so a single
    // source line generates exactly one stop opportunity, no matter how many pcs it spans.
    HashSet<int>? lineBoundaries;

    /// <summary>
    /// Returns true when <paramref name="pc"/> is the *first* pc associated with its
    /// source line (per the DBG section's packed_map encoding). Subsequent pcs that fall
    /// inside the same line range return false. Useful for debuggers that want to fire
    /// breakpoints / step stops once per source line entry rather than at every fetched
    /// opcode.
    /// </summary>
    public bool IsLineBoundary(int pc)
    {
        if (LineType != DebugLineType.PackedMap) return false;
        var set = lineBoundaries;
        if (set is null)
        {
            set = ComputeBoundaries();
            // Idempotent install; concurrent calls can race but produce identical sets.
            System.Threading.Interlocked.CompareExchange(ref lineBoundaries, set, null);
            set = lineBoundaries;
        }
        return set!.Contains(pc);
    }

    HashSet<int> ComputeBoundaries()
    {
        var set = new HashSet<int>();
        var p = (ReadOnlySpan<byte>)LineData;
        uint pos = 0;
        var i = 0;
        while (i < p.Length)
        {
            if (!TryDecodePackedInt(p, ref i, out var posDelta)) break;
            pos += posDelta;
            if (!TryDecodePackedInt(p, ref i, out _)) break; // skip line delta
            set.Add((int)pos);
        }
        return set;
    }

    /// <summary>
    /// Decode a 32-bit varint from <paramref name="bytes"/> starting at <paramref name="offset"/>,
    /// advancing <paramref name="offset"/> past the consumed bytes. Mirrors mruby's
    /// <c>mrb_packed_int_decode</c> in <c>debug.c</c>. Returns false if the input runs out
    /// before a complete varint is read.
    /// </summary>
    static bool TryDecodePackedInt(ReadOnlySpan<byte> bytes, ref int offset, out uint value)
    {
        uint n = 0;
        var shift = 0;
        while (offset < bytes.Length)
        {
            var b = bytes[offset++];
            n |= (uint)(b & 0x7f) << shift;
            if ((b & 0x80) == 0 || shift >= 21)
            {
                value = n;
                return true;
            }
            shift += 7;
        }
        value = 0;
        return false;
    }
}
