using System;
using System.Reflection;

namespace ChibiRuby.Serializer;

public class GeneratedResolver : IMRubyValueFormatterResolver
{
    static class Check<T>
    {
        internal static bool Registered;
    }

    static class Cache<T>
    {
        internal static IMRubyValueFormatter<T>? Formatter;

        static Cache()
        {
            if (Check<T>.Registered) return;

            var type = typeof(T);

            TryInvokeRegisterFormatter(type);
        }
    }

    static bool TryInvokeRegisterFormatter(Type type)
    {
        if (type.GetCustomAttribute<MRubyObjectAttribute>() == null) return false;

        var m = type.GetMethod("__RegisterMRubyValueFormatter",
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static);

        if (m == null)
        {
            return false;
        }

        m.Invoke(null, null); // Cache<T>.formatter will set from method
        return true;
    }

    public static void Register<T>(IMRubyValueFormatter<T> formatter)
    {
        Check<T>.Registered = true; // avoid to call Cache() constructor called.
        Cache<T>.Formatter = formatter;
    }

    public static readonly GeneratedResolver Instance = new();

    public IMRubyValueFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;
}
