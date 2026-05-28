using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ChibiRuby.Compiler;

namespace ChibiRuby.Benchmark;

unsafe class RubyScriptLoader : IDisposable
{
    readonly MRubyState mrubyCSState;
    readonly MrbStateNative* mrbStateNative;

    readonly MRubyCompiler mrubyCSCompiler;
    bool disposed;

    Irep? currentChibiRubyIrep;
    RProcHandle? currentMRubyNativeProc;

    public RubyScriptLoader()
    {
        mrubyCSState = MRubyState.Create();
        RegisterMathModule(mrubyCSState);
        mrubyCSCompiler = MRubyCompiler.Create(mrubyCSState);

        mrbStateNative = NativeMethods.MrbOpen();
    }

    static void RegisterMathModule(MRubyState state)
    {
        state.DefineModule(state.Intern("Math"u8), mod =>
        {
            mod.DefineClassMethod(state.Intern("sqrt"u8), new MRubyMethod((s, self) =>
            {
                var value = s.GetArgumentAsFloatAt(0);
                return System.Math.Sqrt(value);
            }));

            mod.DefineClassMethod(state.Intern("cos"u8), new MRubyMethod((s, self) =>
            {
                var value = s.GetArgumentAsFloatAt(0);
                return System.Math.Cos(value);
            }));

            mod.DefineClassMethod(state.Intern("sin"u8), new MRubyMethod((s, self) =>
            {
                var value = s.GetArgumentAsFloatAt(0);
                return System.Math.Sin(value);
            }));
        });
    }

    public void PreloadScript(ReadOnlySpan<byte> source)
    {
        using var compilation = mrubyCSCompiler.Compile(source);
        currentChibiRubyIrep = compilation.ToIrep();

        currentMRubyNativeProc?.Dispose();

        RProcNative* procPtr = null;
        byte* errorMessageCStr = null;
        fixed (byte* sourcePtr = source)
        {
            var resultCode = NativeMethods.MrbcsCompileToProc(
                mrbStateNative,
                sourcePtr,
                source.Length,
                &procPtr,
                &errorMessageCStr);

            if (resultCode != 0)
            {
                if (errorMessageCStr != null)
                {
                    var errorMessage = Marshal.PtrToStringUTF8((IntPtr)errorMessageCStr)!;
                    throw new MRubyCompileException(errorMessage);
                }
            }
        }

        currentMRubyNativeProc = new RProcHandle(mrbStateNative, procPtr);
    }

    public void PreloadScriptFromFile(string fileName)
    {
        var source = ReadBytes(fileName);
        PreloadScript(source);
    }

    public MRubyValue RunChibiRuby()
    {
        return mrubyCSState.Execute(currentChibiRubyIrep!);
    }

    public MrbValueNative RunMRubyNative()
    {
        return NativeMethods.MrbLoadProc(mrbStateNative, currentMRubyNativeProc!.DangerousGetPtr());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        currentMRubyNativeProc?.Dispose();
        NativeMethods.MrbClose(mrbStateNative);
        disposed = true;
    }

    static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
        // return Path.Join(Assembly.GetEntryAssembly()!.Location, relativePath);
    }

    byte[] ReadBytes(string fileName)
    {
        var path = GetAbsolutePath(Path.Join("ruby", fileName));
        return File.ReadAllBytes(path);
    }
}