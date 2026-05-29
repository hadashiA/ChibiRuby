using System;
using System.Text;

namespace ChibiRuby.Compiler
{
static class BomHelper
{
    static readonly Encoding[] encodings =
    {
        Encoding.UTF8,
        Encoding.Unicode,
        Encoding.BigEndianUnicode,
        Encoding.UTF32
    };

    public static bool TryDetectEncoding(ReadOnlySpan<byte> source, out Encoding bomEncoding)
    {
        foreach (var encoding in encodings)
        {
            if (source.StartsWith(encoding.Preamble))
            {
                bomEncoding = encoding;
                return true;
            }
        }
        bomEncoding = default!;
        return false;
    }
}
}
