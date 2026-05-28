using System;

namespace ChibiRuby.Serializer;

public static class MRubyStateExtensions
{
    public static T? Evaluate<T>(this MRubyState mrb, ReadOnlySpan<byte> bytecode, MRubyValueSerializerOptions? options = null)
    {
        var result = mrb.LoadBytecode(bytecode);
        options ??= MRubyValueSerializerOptions.Default;
        return MRubyValueSerializer.Deserialize<T>(result, mrb, options);
    }
}