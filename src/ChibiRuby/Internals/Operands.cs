using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChibiRuby.Internals;

enum OperandType
{
    Z,
    B,
    S,
    BB,
    BS,
    BBB,
    BSS,
    W
}

[StructLayout(LayoutKind.Explicit)]
struct Operand
{
    [FieldOffset(0)]
    public OperandType Type;

    [FieldOffset(1)]
    public OperandB B;

    [FieldOffset(1)]
    public OperandS S;

    [FieldOffset(1)]
    public OperandBB BB;

    [FieldOffset(1)]
    public OperandBS BS;

    [FieldOffset(1)]
    public OperandBBB BBB;

    [FieldOffset(1)]
    public OperandBSS BSS;

    [FieldOffset(1)]
    public OperandW W;
}

[StructLayout(LayoutKind.Explicit)]
struct OperandZ
{
    public static void Read(ref byte sequence, ref int pc)
    {
        pc += 1;
    }
}

[StructLayout(LayoutKind.Explicit)]
struct OperandB
{
    [FieldOffset(0)]
    public byte A;

    public static OperandB Read(ref byte sequence, ref int pc)
    {
        pc += 2;
        var result = Unsafe.ReadUnaligned<OperandB>(ref Unsafe.Add(ref sequence, (pc - 1)));

        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
struct OperandBB
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public byte B;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBB Read(ref byte sequence, ref int pc)
    {
        pc += 3;
        var result = Unsafe.ReadUnaligned<OperandBB>(ref Unsafe.Add(ref sequence, (pc - 2)));

        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
unsafe struct OperandS
{
    [FieldOffset(0)]
    fixed byte bytesA[2];

    public int A => (bytesA[0] << 8) | bytesA[1];

    public static OperandS Read(ref byte sequence, ref int pc)
    {
        pc += 3;
        return Unsafe.ReadUnaligned<OperandS>(ref Unsafe.Add(ref sequence, pc - 2));
    }
}

[StructLayout(LayoutKind.Explicit)]
unsafe struct OperandBS
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    fixed byte bytesB[2];

    public int B => (bytesB[0] << 8) | bytesB[1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBS Read(ref byte sequence, ref int pc)
    {
        pc += 4;
        return Unsafe.ReadUnaligned<OperandBS>(ref Unsafe.Add(ref sequence, (pc - 3)));
    }
}

[StructLayout(LayoutKind.Explicit)]
struct OperandBBB
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public byte B;

    [FieldOffset(2)]
    public byte C;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBBB Read(ref byte sequence, ref int pc)
    {
        pc += 4;
        return Unsafe.ReadUnaligned<OperandBBB>(ref Unsafe.Add(ref sequence, (pc - 3)));
    }
}

[StructLayout(LayoutKind.Explicit)]
unsafe struct OperandBSS
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    fixed byte bytesB[2];

    [FieldOffset(3)]
    fixed byte bytesC[2];

    public int B => (bytesB[0] << 8) | bytesB[1];
    public int C => (bytesC[0] << 8) | bytesC[1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBSS Read(ref byte sequence, ref int pc)
    {
        pc += 6;
        return Unsafe.ReadUnaligned<OperandBSS>(ref Unsafe.Add(ref sequence, pc - 5));
    }
}

[StructLayout(LayoutKind.Explicit)]
unsafe struct OperandW
{
    // ReSharper disable once UnassignedField.Local
    [FieldOffset(0)]
    public fixed byte Bytes[3];
    public int A => (Bytes[0] << 16) | (Bytes[1] << 8) | Bytes[2];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandW Read(ref byte sequence, ref int pc)
    {
        pc += 4;
        return Unsafe.ReadUnaligned<OperandW>(ref Unsafe.Add(ref sequence, pc - 3));
    }
}