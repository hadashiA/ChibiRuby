using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ChibiRuby.Compiler
{
[StructLayout(LayoutKind.Explicit)]
struct MrbState { }

[StructLayout(LayoutKind.Explicit)]
struct MrcIrep { }

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrcCContext
{
    public MrbState* Mrb;
    public void* Jmp;
    public void* P;
    public void* Options;
    public int SLen;
    public byte* Filename;
    public ushort LineNo;
    public void* TargetClass;
    // Six C bit-fields (capture_errors..no_ext_ops) packed into one byte.
    // bit 0 = capture_errors, bit 1 = dump_result, bit 2 = no_exec,
    // bit 3 = keep_lv, bit 4 = no_optimize, bit 5 = no_ext_ops
    public byte Flags;
    public void* Upper;
    public MrcDiagnosticList* DiagnosticList;
}

enum MrcDiagnosticCode
{
    Warning = 0,
    Error = 1,
    GeneratorWarning = 2,
    GeneratorError = 3,
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrcDiagnosticList
{
    public MrcDiagnosticCode DiagnosticCode;
    public byte* Message;
    public byte* Filename;
    public int Line;
    public int Column;
    public MrcDiagnosticList* Next;
}

unsafe class NativeMethods
{
    const string DllName = "libmruby";

    public const int Ok = 0;
    public const int Failed = 11;

    [DllImport(DllName, EntryPoint = "mrb_open", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrbState* MrbOpen();

    [DllImport(DllName, EntryPoint = "mrb_close", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrbClose(MrbState* mrb);

    [DllImport(DllName, EntryPoint = "chibiruby_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcFree(void* ptr);

    [DllImport(DllName, EntryPoint = "mrc_ccontext_new", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrcCContext* MrcCContextNew(MrbState* mrb);

    [DllImport(DllName, EntryPoint = "mrc_ccontext_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrcCContext* MrcCContextFree(MrcCContext* c);

    // const char *mrc_ccontext_filename(mrc_ccontext *c, const char *s);
    // Sets the source filename used to populate the DBG section. mrc owns the resulting
    // copy; the returned char* points into mrc-managed memory.
    [DllImport(DllName, EntryPoint = "mrc_ccontext_filename", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern byte* MrcCContextFilename(MrcCContext* c, byte* filename);

    [DllImport(DllName, EntryPoint = "mrc_load_string_cxt", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrcIrep* MrcLoadStringCxt(
        MrcCContext* c,
        byte** source,
        nint sourceLength);

    // int mrc_dump_irep(mrc_ccontext *c, const mrc_irep *irep, uint8_t flags, uint8_t **bin, size_t *bin_size);
    [DllImport(DllName, EntryPoint = "mrc_dump_irep", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcDumpIrep(
        MrcCContext* c,
        MrcIrep* irep,
        byte flags,
        byte** bin,
        nint* binSize);

    [DllImport(DllName, EntryPoint = "mrc_irep_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcIrepFree(MrcCContext* c, MrcIrep* irep);

    [DllImport(DllName, EntryPoint = "mrc_diagnostic_list_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcDiagnosticListFree(MrcCContext* c);

}
}
