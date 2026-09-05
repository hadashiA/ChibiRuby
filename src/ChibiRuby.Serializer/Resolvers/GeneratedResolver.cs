using System;
using System.Diagnostics.CodeAnalysis;
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

    // Fallback for assemblies compiled with a pre-1.7 source generator. Assemblies built with the
    // bundled generator register all formatters eagerly from a [ModuleInitializer], which is what
    // trimmed/NativeAOT builds rely on; in those builds this reflection lookup may simply find
    // nothing (the generated method can be trimmed) and returns false.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reflection fallback only. AOT/trimming-safe registration is done eagerly by generated module initializers.")]
    static bool TryInvokeRegisterFormatter(Type type)
    {
        // Do not gate on [MRubyObject] here: Unity 6000.5+'s linker strips instances of
        // PreserveAttribute-derived attributes from player builds, so the attribute can be
        // absent at runtime even for generated types. The generated method is the reliable signal.
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
