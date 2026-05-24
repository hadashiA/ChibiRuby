namespace MRubyCS.StdLib;

/// <summary>
/// Object has no direct method definitions in MRubyCS -- its public API surface
/// comes entirely through <c>include Kernel</c> (wired in MRubyState.InitObject).
/// This placeholder exists so the rbs generator emits <c>sig/object.rbs</c> with
/// the proper header and <c>include Kernel</c>.
/// </summary>
[RubyClass("Object", Superclass = "BasicObject")]
static class ObjectMembers
{
}
