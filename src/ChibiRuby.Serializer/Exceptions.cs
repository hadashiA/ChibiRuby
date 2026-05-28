using System;

namespace ChibiRuby.Serializer;

public class MRubySerializationException(string message) : Exception(message)
{
    public static void ThrowIfTypeMismatch(
        MRubyValue value,
        MRubyVType expectedType,
        string? csharpTypeName = null,
        MRubyState? state = null)
    {
        if (value.VType != expectedType)
        {
            var s = state != null ? state.Stringify(value).ToString() : "";
            throw new MRubySerializationException(csharpTypeName != null
                ? $"An MRubyValue cannot convert to `{csharpTypeName}`. Expected={expectedType} Actual={value.VType}) `{s}`"
                : $"An MRubyValue is not an {expectedType}. ({value.VType})");
        }
    }

    public static void ThrowIfNotEnoughArrayLength(
        MRubyValue value,
        int expectedLength,
        string? expectedTypeName = null,
        MRubyState? state = null)
    {
        ThrowIfTypeMismatch(value, MRubyVType.Array, expectedTypeName, state);
        var actualLength = value.As<RArray>().Length;
        if (actualLength < expectedLength)
        {
            throw new MRubySerializationException(expectedTypeName != null
                ? $"An MRubyValue cannot convert to `{expectedTypeName}`. The length of the array is not long enough. Expected={expectedLength} Actual={actualLength}"
                : $"The length of the mruby array is not long enough. Expected={expectedLength} Actual={actualLength}");
        }
    }
}
