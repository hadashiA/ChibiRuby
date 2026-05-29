using System.Text;

namespace ChibiRuby.Serializer.SourceGenerator;

public static class StringBuilderExtensions
{
    public static void AppendByteArrayString(this StringBuilder stringBuilder, byte[] bytes)
    {
        stringBuilder.Append("{ ");
        var first = true;
        foreach (var x in bytes)
        {
            if (!first)
            {
                stringBuilder.Append(", ");
            }
            stringBuilder.Append(x);
            first = false;
        }
        stringBuilder.Append(" }");
    }
}
