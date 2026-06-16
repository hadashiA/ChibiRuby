using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChibiRuby.Compiler
{
    public class MRubyCompileException(string message) : Exception(message);

    public record MRubyCompileOptions
    {
        public static MRubyCompileOptions Default { get; set; } = new();

        public bool EnableDebugInfo { get; set; } = true;
    }

    public class MRubyCompiler : IDisposable
    {
        public static MRubyCompiler Create(MRubyState mrb, MRubyCompileOptions? options = null)
        {
            var compilerStateHandle = MrbStateHandle.Create();
            return new MRubyCompiler(mrb, compilerStateHandle, options);
        }

        public MRubyState State => mrubyState;

        readonly MRubyState mrubyState;
        readonly MrbStateHandle compileStateHandle;
        readonly MRubyCompileOptions options;
        bool disposed;

        MRubyCompiler(
            MRubyState mrubyState,
            MrbStateHandle compileStateHandle,
            MRubyCompileOptions? options = null)
        {
            this.mrubyState = mrubyState;
            this.compileStateHandle = compileStateHandle;
            this.options = options ?? MRubyCompileOptions.Default;
        }

        ~MRubyCompiler()
        {
            Dispose(false);
        }

        public MRubyValue LoadSourceCodeFile(string path)
        {
            using var compilation = CompileFile(path);
            return mrubyState.LoadBytecode(compilation.AsBytecode());
        }

        public async Task<MRubyValue> LoadSourceCodeFileAsync(
            string path,
            MRubyCompileOptions? options = null,
            CancellationToken? cancellationToken = default)
        {
            using var compilation = await CompileFileAsync(path, options, cancellationToken.GetValueOrDefault());
            return mrubyState.LoadBytecode(compilation.AsBytecode());
        }

        public MRubyValue LoadSourceCode(ReadOnlySpan<byte> utf8Source)
        {
            using var compilation = Compile(utf8Source);
            return mrubyState.LoadBytecode(compilation.AsBytecode());
        }

        public MRubyValue LoadSourceCode(string source)
        {
            var utf8Source = Encoding.UTF8.GetBytes(source);
            return LoadSourceCode(utf8Source);
        }

        public RFiber LoadSourceCodeAsFiber(ReadOnlySpan<byte> utf8Source)
        {
            using var compilation = Compile(utf8Source);
            var proc = mrubyState.CreateProc(compilation.ToIrep());
            return mrubyState.CreateFiber(proc);
        }

        public RFiber LoadSourceCodeAsFiber(string source)
        {
            var utf8Source = Encoding.UTF8.GetBytes(source);
            return LoadSourceCodeAsFiber(utf8Source);
        }

        public CompilationResult CompileFile(string filePath, MRubyCompileOptions? options = null)
        {
            options ??= this.options;
            var bytes = File.ReadAllBytes(filePath);

            return Compile(bytes, filename: Path.GetFullPath(filePath), options);
        }

        public async Task<CompilationResult> CompileFileAsync(
            string filePath,
            MRubyCompileOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= this.options;
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Compile(bytes, filename: Path.GetFullPath(filePath), options);
        }

        public CompilationResult Compile(string sourceCode, string? filename = null, MRubyCompileOptions? options = null) =>
            Compile(Encoding.UTF8.GetBytes(sourceCode), filename, options);

        /// <summary>
        /// Compile Ruby source to <c>.mrb</c> bytecode.
        /// </summary>
        public unsafe CompilationResult Compile(
            ReadOnlySpan<byte> utf8Source,
            string? filename = null,
            MRubyCompileOptions? options = null)
        {
            // Workaround for the crash that occurs when passing a blank to mrc
            if (utf8Source.IsEmpty)
            {
                Span<byte> fallback = stackalloc byte[1];
                fallback[0] = (byte)' ';
                return Compile(fallback, filename, options);
            }

            if (BomHelper.TryDetectEncoding(utf8Source, out var encoding))
            {
                if (encoding.Equals(Encoding.UTF8))
                {
                    utf8Source = utf8Source[encoding.Preamble.Length..];
                }
                else
                {
                    throw new MRubyCompileException("Only UTF-8 is supported");
                }
            }

            var context = MrcCContextHandle.Create(compileStateHandle);
            byte* bin = null;
            nint binLength = 0;

            // Set the source filename on the compile context BEFORE parsing so the resulting
            // mrc_irep has its debug_info populated with a real filename (not "(string)").
            // mrc allocates its own copy of the string, so the marshalled buffer can be freed
            // immediately after the call.
            if (!string.IsNullOrEmpty(filename))
            {
                var byteCount = Encoding.UTF8.GetByteCount(filename) + 1;
                var heap = new byte[byteCount];
                Encoding.UTF8.GetBytes(filename, heap.AsSpan(0, byteCount - 1));
                heap[byteCount - 1] = 0;
                fixed (byte* heapPtr = heap)
                {
                    NativeMethods.MrcCContextFilename(context.DangerousGetPtr(), heapPtr);
                }
            }

            options ??= this.options;
            // MRB_DUMP_DEBUG_INFO == 1; include the DBG section in the serialized .mrb so
            // the C# RiteParser can recover (file, line) info for each pc.
            var dumpFlags = (byte)(options.EnableDebugInfo ? 1 : 0);

            fixed (byte* sourcePtr = utf8Source)
            {
                var irepPtr = NativeMethods.MrcLoadStringCxt(context.DangerousGetPtr(), &sourcePtr, utf8Source.Length);
                if (irepPtr == null || context.HasError)
                {
                    // error
                    return new CompilationResult(mrubyState, context);
                }
                NativeMethods.MrcDumpIrep(context.DangerousGetPtr(), irepPtr, dumpFlags, &bin, &binLength);
                NativeMethods.MrcIrepFree(context.DangerousGetPtr(), irepPtr);
                return new CompilationResult(mrubyState, context, (IntPtr)bin, (int)binLength);
            }
        }

        public void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;
            compileStateHandle.Dispose();
            disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
