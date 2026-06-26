// netstandard2.0 does not ship IsExternalInit, which `record` init-only setters require.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
