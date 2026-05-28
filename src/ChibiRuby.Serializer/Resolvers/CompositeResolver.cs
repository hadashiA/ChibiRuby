using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ChibiRuby.Serializer;

public static class CompositeResolver
{
    public static IMRubyValueFormatterResolver Create(
        IReadOnlyList<IMRubyValueFormatter> formatters,
        IReadOnlyList<IMRubyValueFormatterResolver> resolvers)
    {
        var immutableFormatters = formatters.ToArray();
        var immutableResolvers = resolvers.ToArray();

        return new CachingResolver(immutableFormatters, immutableResolvers);
    }

    public static IMRubyValueFormatterResolver Create( params IMRubyValueFormatterResolver[] resolvers) =>
        Create([], resolvers);

    public static IMRubyValueFormatterResolver Create(params IMRubyValueFormatter[] formatters) =>
        Create(formatters, []);

    class CachingResolver(
        IMRubyValueFormatter[] subFormatters,
        IMRubyValueFormatterResolver[] subResolvers)
        : IMRubyValueFormatterResolver
    {
        readonly ConcurrentDictionary<Type, IMRubyValueFormatter?> formattersCache = new();

        public IMRubyValueFormatter<T>? GetFormatter<T>()
        {
            if (!formattersCache.TryGetValue(typeof(T), out var formatter))
            {
                foreach (var subFormatter in subFormatters)
                {
                    if (subFormatter is IMRubyValueFormatter<T>)
                    {
                        formatter = subFormatter;
                        goto CACHE;
                    }
                }

                foreach (var resolver in subResolvers)
                {
                    formatter = resolver.GetFormatter<T>();
                    if (formatter != null)
                    {
                        goto CACHE;
                    }
                }

// when not found, cache null.
                CACHE:
                formattersCache.TryAdd(typeof(T), formatter);
            }

            return (IMRubyValueFormatter<T>?)formatter;
        }
    }
}
