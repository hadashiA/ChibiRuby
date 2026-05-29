using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChibiRuby.Benchmark;

[StructLayout(LayoutKind.Sequential)]
struct MrbStateNative;

[StructLayout(LayoutKind.Sequential)]
struct RProcNative;

// ReSharper disable InconsistentNaming
public enum MrbVtypeNative : byte
{
    MRB_TT_FALSE,
    MRB_TT_TRUE,
    MRB_TT_SYMBOL,
    MRB_TT_UNDEF,
    MRB_TT_FREE,
    MRB_TT_FLOAT,
    MRB_TT_INTEGER,
    MRB_TT_CPTR,
    MRB_TT_OBJECT,
    MRB_TT_CLASS,
    MRB_TT_MODULE,
    MRB_TT_SCLASS,
    MRB_TT_HASH,
    MRB_TT_CDATA,
    MRB_TT_EXCEPTION,
    MRB_TT_ICLASS,
    MRB_TT_PROC,
    MRB_TT_ARRAY,
    MRB_TT_STRING,
    MRB_TT_RANGE,
    MRB_TT_ENV,
    MRB_TT_FIBER,
    MRB_TT_STRUCT,
    MRB_TT_ISTRUCT,
    MRB_TT_BREAK,
    MRB_TT_COMPLEX,
    MRB_TT_RATIONAL,
    MRB_TT_BIGINT,
    MRB_TT_BACKTRACE,
}


[StructLayout(LayoutKind.Sequential)]
unsafe struct RBasicNative
{
    IntPtr c;
    IntPtr gcnext;
    public MrbVtypeNative TT;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct RIntegerNative
{
    IntPtr c;
    IntPtr gcnext;
    public MrbVtypeNative TT;
    fixed byte Footer[3];
    public nint IntValue;
}


[StructLayout(LayoutKind.Sequential)]
unsafe struct RFloatNative
{
    IntPtr c;
    IntPtr gcnext;
    public MrbVtypeNative TT;
    fixed byte Footer[3];
    public double FloatValue;
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct MrbValueNative
{
    static readonly bool Is64bitTarget = IntPtr.Size == 8;

    IntPtr ptr;

    public MrbVtypeNative TT => this switch
    {
        { IsFalse: true } => MrbVtypeNative.MRB_TT_FALSE,
        { IsTrue: true } => MrbVtypeNative.MRB_TT_TRUE,
        { IsUndef: true } => MrbVtypeNative.MRB_TT_UNDEF,
        { IsSymbol: true } => MrbVtypeNative.MRB_TT_SYMBOL,
        { IsFixnum: true } => MrbVtypeNative.MRB_TT_INTEGER,
        { IsFloat: true } => MrbVtypeNative.MRB_TT_FLOAT,
        { IsObject: true} => ((RBasicNative*)ptr)->TT,
        _ => default
    };

    public bool IsNil => ptr == IntPtr.Zero;
    public bool IsFalse => ptr.ToInt64() == 0b0000_0100;
    public bool IsTrue => ptr.ToInt64() == 0b0000_1100;
    public bool IsUndef => ptr.ToInt64() == 0b0001_0100;
    public bool IsSymbol => (ptr.ToInt64() & 0b1_1111) == 0b1_1100;
    public bool IsFixnum => (ptr.ToInt64() & 1) == 1;
    public bool IsFloat => Is64bitTarget
        ? (ptr.ToInt64() & 0b11) == 0b10
        : IsObject && ((RBasicNative*)ptr)->TT == MrbVtypeNative.MRB_TT_FLOAT;

    public bool IsObject => (ptr.ToInt64() & 0b111) == 0;

    public long IntValue
    {
        get
        {
            if (IsObject && ((RBasicNative *)ptr)->TT == MrbVtypeNative.MRB_TT_INTEGER)
            {
                return ((RIntegerNative*)ptr)->IntValue;
            }
            return ptr.ToInt64() >> 1;
        }
    }

    public double FloatValue
    {
        get
        {
            // Assume that MRB_USE_FLOAT32 is not defined
            // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
            if (Is64bitTarget)
            {
                var fbits = (ptr.ToInt64() & ~3) | 2;
                return Unsafe.As<long, double>(ref fbits);
            }
            else
            {
                return ((RFloatNative*)ptr)->FloatValue;
            }
        }
    }
}

unsafe class RProcHandle(MrbStateNative* mrbStatePtr, RProcNative* procPtr) : SafeHandle((IntPtr)procPtr, true)
{
    public override bool IsInvalid => handle == IntPtr.Zero;


    public unsafe RProcNative* DangerousGetPtr() => (RProcNative*)DangerousGetHandle();

    protected override unsafe bool ReleaseHandle()
    {
        if (IsClosed) return false;
        NativeMethods.MrbcsReleaseProc(mrbStatePtr, DangerousGetPtr());
        return true;
    }
}

unsafe class NativeMethods
{
    const string DllName = "libchibiruby_benchmark_helper";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
    }

    static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == DllName)
        {
            var path = "runtimes";
            string extname;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = Path.Join(path, "win");
                extname = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path = Path.Join(path, "osx");
                extname = ".dylib";
            }
            else
            {
                path = Path.Join(path, "linux");
                extname = ".so";
            }

            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    path += "-x86";
                    break;
                case Architecture.X64:
                    path += "-x64";
                    break;
                case Architecture.Arm64:
                    path += "-arm64";
                    break;
            }

            path = Path.Join(path, "native", $"{DllName}{extname}");
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, path), assembly, searchPath);
        }
        return IntPtr.Zero;
    }

    [DllImport(DllName, EntryPoint = "mrb_open", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrbStateNative* MrbOpen();

    [DllImport(DllName, EntryPoint = "mrb_close", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrbClose(MrbStateNative* mrb);

    [DllImport(DllName, EntryPoint = "mrb_load_proc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrbValueNative MrbLoadProc(MrbStateNative* mrb, RProcNative *proc);

    [DllImport(DllName, EntryPoint = "chibiruby_mrbc_compile_to_proc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MrbcsCompileToProc(
        MrbStateNative* mrb,
        byte* source,
        int sourceLength,
        RProcNative** proc,
        byte** errorMessage);

    [DllImport(DllName, EntryPoint = "chibiruby_mrbc_release_proc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MrbcsReleaseProc(MrbStateNative* mrb, RProcNative* procPtr);
}
