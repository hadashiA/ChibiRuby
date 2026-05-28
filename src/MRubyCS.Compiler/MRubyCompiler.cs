using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS.Compiler
{
    public class MRubyCompileException(string message) : Exception(message);

    public record MRubyCompileOptions
    {
        public static MRubyCompileOptions Default { get; set; } = new();
    }

    public class MRubyCompiler : IDisposable
    {
        public static MRubyCompiler Create(MRubyState mrb, MRubyCompileOptions? options = null)
        {
            var compilerStateHandle = MrbStateHandle.Create();
            return new MRubyCompiler(mrb, compilerStateHandle, options);
        }

        public MRubyState State => mruby;

        readonly MRubyState mruby;
        readonly MrbStateHandle compileStateHandle;
        readonly MRubyCompileOptions options;
        bool disposed;

        MRubyCompiler(
            MRubyState mruby,
            MrbStateHandle compileStateHandle,
            MRubyCompileOptions? options = null)
        {
            this.mruby = mruby;
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
            return mruby.LoadBytecode(compilation.AsBytecode());
        }

        public async Task<MRubyValue> LoadSourceCodeFileAsync(string path, CancellationToken cancellationToken = default)
        {
            using var compilation = await CompileFileAsync(path, cancellationToken);
            return mruby.LoadBytecode(compilation.AsBytecode());
        }

        public MRubyValue LoadSourceCode(ReadOnlySpan<byte> utf8Source)
        {
            using var compilation = Compile(utf8Source);
            return mruby.LoadBytecode(compilation.AsBytecode());
        }

        public MRubyValue LoadSourceCode(string source)
        {
            var utf8Source = Encoding.UTF8.GetBytes(source);
            return LoadSourceCode(utf8Source);
        }

        public RFiber LoadSourceCodeAsFiber(ReadOnlySpan<byte> utf8Source)
        {
            using var compilation = Compile(utf8Source);
            var proc = mruby.CreateProc(compilation.ToIrep());
            return mruby.CreateFiber(proc);
        }

        public RFiber LoadSourceCodeAsFiber(string source)
        {
            var utf8Source = Encoding.UTF8.GetBytes(source);
            return LoadSourceCodeAsFiber(utf8Source);
        }

        public CompilationResult CompileFile(string filePath, bool debugInfo = true)
        {
            var bytes = File.ReadAllBytes(filePath);

            return Compile(bytes,
                filename: Path.GetFullPath(filePath),
                debugInfo: debugInfo);
        }

        public async Task<CompilationResult> CompileFileAsync(string filePath, CancellationToken cancellationToken = default, bool debugInfo = true)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Compile(bytes,
                filename: Path.GetFullPath(filePath),
                debugInfo: debugInfo);
        }

        public CompilationResult Compile(string sourceCode, string? filename = null, bool debugInfo = true) =>
            Compile(Encoding.UTF8.GetBytes(sourceCode), filename, debugInfo);

        /// <summary>
        /// Compile Ruby source to <c>.mrb</c> bytecode.
        /// </summary>
        /// <param name="utf8Source">UTF-8 encoded source bytes.</param>
        /// <param name="filename">
        /// Optional source file name to record in the bytecode's DBG section. When non-null
        /// (and <paramref name="debugInfo"/> is true), tools like the debugger / Backtrace
        /// resolve <c>pc</c> back to <c>filename:line</c>.
        /// </param>
        /// <param name="debugInfo">
        /// When true (default), the bytecode includes a DBG section with line numbers. Set
        /// to false to produce smaller bytecode for production distribution.
        /// </param>
        public unsafe CompilationResult Compile(
            ReadOnlySpan<byte> utf8Source,
            string? filename = null,
            bool debugInfo = true)
        {
            // Workaround for the crash that occurs when passing a blank to mrc
            if (utf8Source.IsEmpty)
            {
                Span<byte> fallback = stackalloc byte[1];
                fallback[0] = (byte)' ';
                return Compile(fallback, filename, debugInfo);
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

            // MRB_DUMP_DEBUG_INFO == 1; include the DBG section in the serialized .mrb so
            // the C# RiteParser can recover (file, line) info for each pc.
            var dumpFlags = (byte)(debugInfo ? 1 : 0);

            fixed (byte* sourcePtr = utf8Source)
            {
                var irepPtr = NativeMethods.MrcLoadStringCxt(context.DangerousGetPtr(), &sourcePtr, utf8Source.Length);
                if (irepPtr == null || context.HasError)
                {
                    // error
                    return new CompilationResult(mruby, compileStateHandle, context);
                }
                NativeMethods.MrcDumpIrep(context.DangerousGetPtr(), irepPtr, dumpFlags, &bin, &binLength);
                NativeMethods.MrcIrepFree(context.DangerousGetPtr(), irepPtr);
                return new CompilationResult(mruby, compileStateHandle, context, (IntPtr)bin, (int)binLength);
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