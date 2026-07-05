using System.Runtime.CompilerServices;

namespace ChibiRuby.Internals;

// Operand decoding for the VM bytecode.
//
// mruby widens operands with the EXT1/EXT2/EXT3 prefix opcodes when an operand
// does not fit in a single byte (e.g. a literal-pool or symbol index over 255):
//   EXT1 -> the 1st operand is widened from B (1 byte) to S (2 bytes)
//   EXT2 -> the 2nd operand is widened
//   EXT3 -> both the 1st and 2nd operands are widened
// Only the first two operands are ever widened; a trailing B (the 3rd operand of
// a BBB, the C of a BSS) always stays one byte. Operands that are already S/W are
// never prefixed, so those readers ignore `ext`.
//
// Fields are `ushort` (a widened operand is at most 2 bytes = 65535), which keeps
// these structs small and register-friendly. `ext` is 0 on the overwhelmingly
// common path, so each Read keeps a single-shot fast path and pushes the widened
// decode out of line.

struct OperandZ
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref byte sequence, ref int pc)
    {
        pc += 1;
    }
}

struct OperandB
{
    public ushort A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandB Read(ref byte sequence, ref int pc, int ext = 0)
    {
        if (ext != 0) return ReadExtended(ref sequence, ref pc, ext);
        pc += 2;
        return new OperandB { A = Unsafe.Add(ref sequence, pc - 1) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static OperandB ReadExtended(ref byte sequence, ref int pc, int ext)
    {
        var p = pc + 1;
        var a = Operands.ReadOperand(ref sequence, ref p, Operands.Widen1(ext));
        pc = p;
        return new OperandB { A = (ushort)a };
    }
}

struct OperandBB
{
    public ushort A;
    public ushort B;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBB Read(ref byte sequence, ref int pc, int ext = 0)
    {
        if (ext != 0) return ReadExtended(ref sequence, ref pc, ext);
        ref var p = ref Unsafe.Add(ref sequence, pc + 1);
        pc += 3;
        return new OperandBB { A = p, B = Unsafe.Add(ref p, 1) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static OperandBB ReadExtended(ref byte sequence, ref int pc, int ext)
    {
        var p = pc + 1;
        var a = Operands.ReadOperand(ref sequence, ref p, Operands.Widen1(ext));
        var b = Operands.ReadOperand(ref sequence, ref p, Operands.Widen2(ext));
        pc = p;
        return new OperandBB { A = (ushort)a, B = (ushort)b };
    }
}

struct OperandS
{
    public int A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandS Read(ref byte sequence, ref int pc)
    {
        ref var p = ref Unsafe.Add(ref sequence, pc + 1);
        pc += 3;
        return new OperandS { A = (p << 8) | Unsafe.Add(ref p, 1) };
    }
}

struct OperandBS
{
    public ushort A;
    public ushort B;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBS Read(ref byte sequence, ref int pc, int ext = 0)
    {
        if (ext != 0) return ReadExtended(ref sequence, ref pc, ext);
        ref var p = ref Unsafe.Add(ref sequence, pc + 1);
        pc += 4;
        return new OperandBS { A = p, B = (ushort)((Unsafe.Add(ref p, 1) << 8) | Unsafe.Add(ref p, 2)) };
    }

    // Only the leading B can widen (the trailing operand is already S).
    [MethodImpl(MethodImplOptions.NoInlining)]
    static OperandBS ReadExtended(ref byte sequence, ref int pc, int ext)
    {
        var p = pc + 1;
        var a = Operands.ReadOperand(ref sequence, ref p, Operands.Widen1(ext));
        var b = Operands.ReadOperand(ref sequence, ref p, widen: true);
        pc = p;
        return new OperandBS { A = (ushort)a, B = (ushort)b };
    }
}

struct OperandBBB
{
    public ushort A;
    public ushort B;
    public ushort C;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBBB Read(ref byte sequence, ref int pc, int ext = 0)
    {
        if (ext != 0) return ReadExtended(ref sequence, ref pc, ext);
        ref var p = ref Unsafe.Add(ref sequence, pc + 1);
        pc += 4;
        return new OperandBBB { A = p, B = Unsafe.Add(ref p, 1), C = Unsafe.Add(ref p, 2) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static OperandBBB ReadExtended(ref byte sequence, ref int pc, int ext)
    {
        var p = pc + 1;
        var a = Operands.ReadOperand(ref sequence, ref p, Operands.Widen1(ext));
        var b = Operands.ReadOperand(ref sequence, ref p, Operands.Widen2(ext));
        var c = Operands.ReadOperand(ref sequence, ref p, widen: false);
        pc = p;
        return new OperandBBB { A = (ushort)a, B = (ushort)b, C = (ushort)c };
    }
}

struct OperandBSS
{
    public ushort A;
    public int B;
    public int C;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBSS Read(ref byte sequence, ref int pc, int ext = 0)
    {
        if (ext != 0) return ReadExtended(ref sequence, ref pc, ext);
        ref var p = ref Unsafe.Add(ref sequence, pc + 1);
        pc += 6;
        return new OperandBSS
        {
            A = p,
            B = (Unsafe.Add(ref p, 1) << 8) | Unsafe.Add(ref p, 2),
            C = (Unsafe.Add(ref p, 3) << 8) | Unsafe.Add(ref p, 4),
        };
    }

    // Only the leading B can widen (the two trailing operands are already S).
    [MethodImpl(MethodImplOptions.NoInlining)]
    static OperandBSS ReadExtended(ref byte sequence, ref int pc, int ext)
    {
        var p = pc + 1;
        var a = Operands.ReadOperand(ref sequence, ref p, Operands.Widen1(ext));
        var b = Operands.ReadOperand(ref sequence, ref p, widen: true);
        var c = Operands.ReadOperand(ref sequence, ref p, widen: true);
        pc = p;
        return new OperandBSS { A = (ushort)a, B = b, C = c };
    }
}

struct OperandW
{
    public int A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandW Read(ref byte sequence, ref int pc)
    {
        ref var p = ref Unsafe.Add(ref sequence, pc + 1);
        pc += 4;
        return new OperandW { A = (p << 16) | (Unsafe.Add(ref p, 1) << 8) | Unsafe.Add(ref p, 2) };
    }
}

static class Operands
{
    // EXT1 widens operand 1, EXT3 widens both.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Widen1(int ext) => ext == 1 || ext == 3;

    // EXT2 widens operand 2, EXT3 widens both.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Widen2(int ext) => ext == 2 || ext == 3;

    // Read one operand, advancing `pc`. Widened operands are 2 bytes big-endian.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadOperand(ref byte sequence, ref int pc, bool widen)
    {
        if (widen)
        {
            var value = (Unsafe.Add(ref sequence, pc) << 8) | Unsafe.Add(ref sequence, pc + 1);
            pc += 2;
            return value;
        }
        else
        {
            var value = Unsafe.Add(ref sequence, pc);
            pc += 1;
            return value;
        }
    }
}
