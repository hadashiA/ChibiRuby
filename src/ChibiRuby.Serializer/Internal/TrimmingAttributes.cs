#if NETSTANDARD2_1
// netstandard2.1 does not ship the trimming/AOT analysis attributes.
// Internal polyfills so the shared source compiles; the analyzers only run on the net8.0+ builds.
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Event |
        AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Struct,
        Inherited = false, AllowMultiple = true)]
    sealed class UnconditionalSuppressMessageAttribute(string category, string checkId) : Attribute
    {
        public string Category { get; } = category;
        public string CheckId { get; } = checkId;
        public string? Justification { get; set; }
    }
}
#endif
