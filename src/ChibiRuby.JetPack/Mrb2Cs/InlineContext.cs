using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChibiRuby;
namespace ChibiRuby.JetPack.Mrb2Cs;

// Per-method emission context for block inlining, kept in a thread-static so it doesn't
// have to thread through every EmitInstruction/arith helper. Inside an inlined block body:
// ForceSend makes numeric slow paths Send (never deopt — a block body must not re-execute
// a partially-applied loop iteration); UpvarCells maps a depth-0 upvar register to the C#
// ref-param cell it reads/writes; AuxMethods collects the generated __blk method sources.
sealed class BlockEmitState
{
    public bool ForceSend;
    // Scope level of the body currently being emitted: 0 = the method, 1 = a block directly
    // in the method, 2 = a block in a block, ... A variable is identified by an absolute
    // coordinate (scopeLevel, register) — a register number alone is ambiguous across levels.
    public int CurrentLevel;
    // Coordinates the current block received as `cell_<scope>_<register>` ref params (the
    // captured variables it or its descendants read/write above its own scope).
    public HashSet<(int Scope, int Register)>? Cells;
    public readonly List<string> AuxMethods = [];
    public int BlockCounter;
    public string OwnerName = "";                        // prefix for unique __blk names
    // Program-wide accessor map so inlined block bodies can devirtualize their (very common)
    // cross-object getter/setter sends to guarded field access. Only accessor devirt is
    // enabled in blocks — never self-send / cross-object __inline calls, which are direct C#
    // calls that would break fiber switching from inside a loop body.
    public IReadOnlyDictionary<Symbol, AccessorTarget>? AccessorRegistry;
}

// Per-method inline-emission data. Holds the resolution inputs (definingClass/registry drive
// self-send inlining; accessors drives cross-object getter/setter devirtualization; either may
// be absent → that capability is off) plus the per-site counters the emitter increments and
// EmitInlineFields reads back. Pure data — all C# emission lives in Emitter.
sealed class InlineContext
{
    internal readonly MRubyState state;
    internal readonly RClass? definingClass;
    internal readonly IReadOnlyDictionary<ulong, int>? registry;
    internal readonly IReadOnlyDictionary<Symbol, AccessorTarget>? accessors;
    internal readonly IReadOnlyDictionary<Symbol, ConstReturnTarget>? constReturns;
    internal readonly string methodName;
    internal readonly SymbolCache sym;
    internal readonly RubyIRMethod exe;

    internal int icCount;
    internal int pureUnaryCount;
    internal int candGroupCount;
    internal int constReadCount;
    internal readonly List<int> candGroupSizes = []; // per class-switch site: number of candidate classes

    public InlineContext(
        MRubyState state,
        RClass? definingClass,
        IReadOnlyDictionary<ulong, int>? registry,
        IReadOnlyDictionary<Symbol, AccessorTarget>? accessors,
        IReadOnlyDictionary<Symbol, ConstReturnTarget>? constReturns,
        string methodName,
        SymbolCache sym,
        RubyIRMethod exe)
    {
        this.state = state;
        this.definingClass = definingClass;
        this.registry = registry;
        this.accessors = accessors;
        this.constReturns = constReturns;
        this.methodName = methodName;
        this.sym = sym;
        this.exe = exe;
    }
}
