using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ChibiRuby.Compiler;

class MrcCContextHandle(IntPtr invalidHandleValue) : SafeHandle(invalidHandleValue, true)
{
    public static unsafe MrcCContextHandle Create(MrbStateHandle mrbStateHandle)
    {
        var ptr = NativeMethods.MrcCContextNew(mrbStateHandle.DangerousGetPtr());
        return new MrcCContextHandle((IntPtr)ptr);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public unsafe bool HasError => (DangerousGetPtr()->Flags & 0x01) != 0;

    public unsafe MrcCContext* DangerousGetPtr() => (MrcCContext*)DangerousGetHandle();

    protected override unsafe bool ReleaseHandle()
    {
        if (IsClosed) return false;
        NativeMethods.MrcCContextFree(DangerousGetPtr());
        return true;
    }

    public unsafe IReadOnlyList<DiagnosticsDescriptor> GetDiagnostics()
    {
        var list = new List<DiagnosticsDescriptor>();
        var nodePtr = DangerousGetPtr()->DiagnosticList;
        while (nodePtr != null)
        {
            var severity = nodePtr->DiagnosticCode switch
            {
                MrcDiagnosticCode.Warning => DiagnosticSeverity.Warning,
                MrcDiagnosticCode.Error => DiagnosticSeverity.Error,
                MrcDiagnosticCode.GeneratorWarning => DiagnosticSeverity.GeneratorWarning,
                MrcDiagnosticCode.GeneratorError => DiagnosticSeverity.GeneratorError,
                _ => throw new ArgumentOutOfRangeException()
            };
            var message = Marshal.PtrToStringUTF8((IntPtr)nodePtr->Message);
            var descriptor = new DiagnosticsDescriptor(severity, nodePtr->Line, nodePtr->Column, message);
            list.Add(descriptor);
            nodePtr = nodePtr->Next;
        }

        return list;
    }
}
