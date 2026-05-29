using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ChibiRuby.Internals;

[DebuggerDisplay("Value = {BoxedValue}")]
readonly struct TypeObjectUnion : IEquatable<TypeObjectUnion>
{
    public bool Equals(TypeObjectUnion other)
    {
        return ReferenceEquals(RawObject,other.RawObject);
    }

    public override bool Equals(object? obj)
    {
        return obj is TypeObjectUnion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return RawObject.GetHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TypeObjectUnion(RObject obj) => RawObject = obj;
    public TypeObjectUnion(MRubyVType type) => Unsafe.As<TypeObjectUnion, nint>(ref this) = (nint)type;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public readonly RObject RawObject;
    public nint RawValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.As<TypeObjectUnion, nint>(ref Unsafe.AsRef(in this));
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public MRubyVType TypeValue
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (MRubyVType)RawValue;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]

    public RObject? Object
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawValue > 255 ? RawObject : null;
    }

    public bool IsType
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawValue <= 255;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public bool IsObject
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawValue > 255;
    }

    public object BoxedValue => IsType ? TypeValue : RawObject;
    public override string ToString() => BoxedValue.ToString()!;

    public static bool operator ==(TypeObjectUnion left, TypeObjectUnion right) => ReferenceEquals(left.RawObject,right.RawObject);

    public static bool operator !=(TypeObjectUnion left, TypeObjectUnion right) => !ReferenceEquals(left.RawObject,right.RawObject);
}