using System;
using System.Runtime.InteropServices;

namespace ChibiRuby.Compiler
{
public class MrbNativeBytesHandle : SafeHandle
{
    readonly MrbStateHandle stateHandle1;

    internal MrbNativeBytesHandle(
        MrbStateHandle stateHandle,
        IntPtr ptr,
        int length) : base(ptr, true)
    {
        stateHandle1 = stateHandle;
        Length = length;
    }

    public override bool IsInvalid => handle == IntPtr.Zero;
    public int Length { get; }

    public unsafe ReadOnlySpan<byte> AsSpan()
    {
        return new ReadOnlySpan<byte>(DangerousGetHandle().ToPointer(), Length);
    }

    protected override unsafe bool ReleaseHandle()
    {
        if (IsClosed) return false;
        // NativeMethods.MrbFree(stateHandle1.DangerousGetPtr(), DangerousGetHandle().ToPointer());
        NativeMethods.MrcFree(DangerousGetHandle().ToPointer());
        return true;
    }
}
}
