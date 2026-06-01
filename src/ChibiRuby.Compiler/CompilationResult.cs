using System;
using System.Collections.Generic;

namespace ChibiRuby.Compiler
{
public class CompilationResult : IDisposable
{
    public IReadOnlyList<DiagnosticsDescriptor> Diagnostics { get; }

    public bool HasError => contextHandle.HasError;

    readonly MRubyState mrb;
    readonly MrbStateHandle stateHandle;
    readonly MrcCContextHandle contextHandle;
    readonly IntPtr bytecodeDataPtr;
    readonly int bytecodeLength;
    bool disposed;

    internal CompilationResult(
        MRubyState mrb,
        MrbStateHandle stateHandle,
        MrcCContextHandle contextHandle,
        IntPtr bytecodeDataPtr,
        int bytecodeLength)
    {
        this.mrb = mrb;
        this.stateHandle = stateHandle;
        this.contextHandle = contextHandle;
        this.bytecodeDataPtr = bytecodeDataPtr;
        this.bytecodeLength = bytecodeLength;
        Diagnostics = contextHandle.GetDiagnostics();
    }

    internal CompilationResult(
        MRubyState mrb,
        MrbStateHandle stateHandle,
        MrcCContextHandle contextHandle)
    {
        this.mrb = mrb;
        this.stateHandle = stateHandle;
        this.contextHandle = contextHandle;
        Diagnostics = contextHandle.GetDiagnostics();
    }

    ~CompilationResult()
    {
        Dispose(disposing: false);
    }

    public unsafe ReadOnlySpan<byte> AsBytecode()
    {
        if (HasError || bytecodeDataPtr == IntPtr.Zero || bytecodeLength <= 0)
        {
            throw new MRubyCompileException(FormatDiagnostics());
        }
        return new ReadOnlySpan<byte>((byte*)bytecodeDataPtr, bytecodeLength);
    }

    string FormatDiagnostics()
    {
        return Diagnostics.Count > 0
            ? string.Join(Environment.NewLine, Diagnostics)
            : "Ruby compilation failed (no diagnostics available).";
    }

    public ReadOnlySpan<byte> AsSpan() => AsBytecode();

    public Irep ToIrep()
    {
        return mrb.RiteParser.Parse(AsBytecode());
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this); // ファイナライザの呼び出しを抑制
    }

    unsafe void Dispose(bool disposing)
    {
        if (disposed) return;

        if (bytecodeDataPtr != IntPtr.Zero)
        {
            // NativeMethods.MrcFree(stateHandle.DangerousGetPtr(), bytecodeDataPtr.ToPointer());
            NativeMethods.MrcFree(bytecodeDataPtr.ToPointer());
        }

        contextHandle.Dispose();
        disposed = true;
    }
}
}
