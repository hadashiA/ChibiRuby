using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using ChibiRuby.Internals;
using static Utf8StringInterpolation.Utf8String;
#if NET7_0_OR_GREATER
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static ChibiRuby.Internal.MemoryMarshalEx;
#endif

namespace ChibiRuby;

partial class MRubyState
{
    static ReadOnlySpan<byte> GetOpCodeName(OpCode code) =>
        code switch
        {
            OpCode.Nop => "NOP"u8,
            OpCode.Move => "MOVE"u8,
            OpCode.LoadL => "LOADL"u8,
            OpCode.LoadI8 => "LOADI8"u8,
            OpCode.LoadINeg => "LOADINEG"u8,
            OpCode.LoadI__1 => "LOADI__1"u8,
            OpCode.LoadI_0 => "LOADI_0"u8,
            OpCode.LoadI_1 => "LOADI_1"u8,
            OpCode.LoadI_2 => "LOADI_2"u8,
            OpCode.LoadI_3 => "LOADI_3"u8,
            OpCode.LoadI_4 => "LOADI_4"u8,
            OpCode.LoadI_5 => "LOADI_5"u8,
            OpCode.LoadI_6 => "LOADI_6"u8,
            OpCode.LoadI_7 => "LOADI_7"u8,
            OpCode.LoadI16 => "LOADI16"u8,
            OpCode.LoadI32 => "LOADI32"u8,
            OpCode.LoadSym => "LOADSYM"u8,
            OpCode.LoadNil => "LOADNIL"u8,
            OpCode.LoadSelf => "LOADSELF"u8,
            OpCode.LoadT => "LOADT"u8,
            OpCode.LoadF => "LOADF"u8,
            OpCode.GetGV => "GETGV"u8,
            OpCode.SetGV => "SETGV"u8,
            OpCode.GetSV => "GETSV"u8,
            OpCode.SetSV => "SETSV"u8,
            OpCode.GetIV => "GETIV"u8,
            OpCode.SetIV => "SETIV"u8,
            OpCode.GetCV => "GETCV"u8,
            OpCode.SetCV => "SETCV"u8,
            OpCode.GetConst => "GETCONST"u8,
            OpCode.SetConst => "SETCONST"u8,
            OpCode.GetMCnst => "GETMCNST"u8,
            OpCode.SetMCnst => "SETMCNST"u8,
            OpCode.GetUpVar => "GETUPVAR"u8,
            OpCode.SetUpVar => "SETUPVAR"u8,
            OpCode.GetIdx => "GETIDX"u8,
            OpCode.GetIdx0 => "GETIDX0"u8,
            OpCode.SetIdx => "SETIDX"u8,
            OpCode.Jmp => "JMP"u8,
            OpCode.JmpIf => "JMPIF"u8,
            OpCode.JmpNot => "JMPNOT"u8,
            OpCode.JmpNil => "JMPNIL"u8,
            OpCode.JmpUw => "JMPUW"u8,
            OpCode.Except => "EXCEPT"u8,
            OpCode.Rescue => "RESCUE"u8,
            OpCode.RaiseIf => "RAISEIF"u8,
            OpCode.MatchErr => "MATCHERR"u8,
            OpCode.SSend => "SSEND"u8,
            OpCode.SSend0 => "SSEND0"u8,
            OpCode.SSendB => "SSENDB"u8,
            OpCode.Send => "SEND"u8,
            OpCode.Send0 => "SEND0"u8,
            OpCode.SendB => "SENDB"u8,
            OpCode.Call => "CALL"u8,
            OpCode.BlkCall => "BLKCALL"u8,
            OpCode.Super => "SUPER"u8,
            OpCode.ArgAry => "ARGARY"u8,
            OpCode.Enter => "ENTER"u8,
            OpCode.KeyP => "KEY_P"u8,
            OpCode.KeyEnd => "KEYEND"u8,
            OpCode.KArg => "KARG"u8,
            OpCode.Return => "RETURN"u8,
            OpCode.ReturnBlk => "RETURN_BLK"u8,
            OpCode.RetSelf => "RETSELF"u8,
            OpCode.RetNil => "RETNIL"u8,
            OpCode.RetTrue => "RETTRUE"u8,
            OpCode.RetFalse => "RETFALSE"u8,
            OpCode.Break => "BREAK"u8,
            OpCode.BlkPush => "BLKPUSH"u8,
            OpCode.Add => "ADD"u8,
            OpCode.AddI => "ADDI"u8,
            OpCode.Sub => "SUB"u8,
            OpCode.SubI => "SUBI"u8,
            OpCode.AddILV => "ADDILV"u8,
            OpCode.SubILV => "SUBILV"u8,
            OpCode.Mul => "MUL"u8,
            OpCode.Div => "DIV"u8,
            OpCode.EQ => "EQ"u8,
            OpCode.LT => "LT"u8,
            OpCode.LE => "LE"u8,
            OpCode.GT => "GT"u8,
            OpCode.GE => "GE"u8,
            OpCode.Array => "ARRAY"u8,
            OpCode.Array2 => "ARRAY2"u8,
            OpCode.AryCat => "ARYCAT"u8,
            OpCode.AryPush => "ARYPUSH"u8,
            OpCode.ArySplat => "ARYSPLAT"u8,
            OpCode.ARef => "AREF"u8,
            OpCode.ASet => "ASET"u8,
            OpCode.APost => "APOST"u8,
            OpCode.Intern => "INTERN"u8,
            OpCode.Symbol => "SYMBOL"u8,
            OpCode.String => "STRING"u8,
            OpCode.StrCat => "STRCAT"u8,
            OpCode.Hash => "HASH"u8,
            OpCode.HashAdd => "HASHADD"u8,
            OpCode.HashCat => "HASHCAT"u8,
            OpCode.Lambda => "LAMBDA"u8,
            OpCode.Block => "BLOCK"u8,
            OpCode.Method => "METHOD"u8,
            OpCode.RangeInc => "RANGE_INC"u8,
            OpCode.RangeExc => "RANGE_EXC"u8,
            OpCode.OClass => "OCLASS"u8,
            OpCode.Class => "CLASS"u8,
            OpCode.Module => "MODULE"u8,
            OpCode.Exec => "EXEC"u8,
            OpCode.Def => "DEF"u8,
            OpCode.TDef => "TDEF"u8,
            OpCode.SDef => "SDEF"u8,
            OpCode.Alias => "ALIAS"u8,
            OpCode.Undef => "UNDEF"u8,
            OpCode.SClass => "SCLASS"u8,
            OpCode.TClass => "TCLASS"u8,
            OpCode.Debug => "DEBUG"u8,
            OpCode.Err => "ERR"u8,
            OpCode.EXT1 => "EXT1"u8,
            OpCode.EXT2 => "EXT2"u8,
            OpCode.EXT3 => "EXT3"u8,
            OpCode.Stop => "STOP"u8,

            OpCode.SendInternal => "SEND_INTERNAL"u8,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        };

    public void CodeDump(Irep irep, IBufferWriter<byte> writer, int tabWidth = 4)
    {
        var longestOpCodeName = "RETURN_BLK"u8.Length;
        var maxTabCount = (longestOpCodeName) / tabWidth + 1;

        void WriteOpCodeWithTab(OpCode opcode)
        {
            var name = GetOpCodeName(opcode);
            ReadOnlySpan<byte> tabs = "\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t"u8;
            writer.Write(name);
            writer.Write(tabs.Slice(0, maxTabCount - (name.Length) / tabWidth));
        }

        void WriteRegister(int n)
        {
            if (n == 0) return;
            if (n >= irep.LocalVariables.Length) return;
            var local = irep.LocalVariables[n - 1];
            if (local.Value != 0)
            {
                Format(writer, $"R{n}:{symbolTable.NameOf(local)}\n");
            }
        }

        void WriteLocalVariableA(short a)
        {
            if (a == 0 || a >= irep.LocalVariables.Length)
            {
                writer.Write("\n"u8);
                return;
            }

            writer.Write("\t;"u8);
            WriteRegister(a);
            writer.Write("\n"u8);
        }

        void WriteLocalVariableAB(short a, short b)
        {
            if (a + b == 0 || (a >= irep.LocalVariables.Length && b >= irep.LocalVariables.Length))
            {
                writer.Write("\n"u8);
                return;
            }

            writer.Write("\t;"u8);
            if (a > 0) WriteRegister(a);
            if (b > 0) WriteRegister(b);
            writer.Write("\n"u8);
        }

        Format(writer, $"irep: {Unsafe.As<Irep, nuint>(ref irep)} nregs={irep.RegisterVariableCount} nlocals={irep.LocalVariables.Length} ");
        Format(writer, $"pools={irep.PoolValues.Length} syms={irep.Symbols.Length} reps={irep.Children.Length} iren={irep.Sequence.Length}\n");


        {
            var head = false;
            for (var index = 0; index < irep.LocalVariables.Length; index++)
            {
                var localSymbol = irep.LocalVariables[index];
                var name = symbolTable.NameOf(localSymbol);
                if (name.IsEmpty) continue;
                if (!head)
                {
                    writer.Write("local variable names:\n"u8);
                    head = true;
                }

                Format(writer, $"R{index}:{name}\n");
            }
        }

        {
            int e = 0;
            for (int i = irep.CatchHandlers.Length; i > 0; i--, e++)
            {
                var catchHandler = irep.CatchHandlers[e];

                var typeName = catchHandler.HandlerType switch
                {
                    CatchHandlerType.Rescue => "rescue",
                    CatchHandlerType.Ensure => "ensure",
                    CatchHandlerType.All => "all",
                    _ => "unknown"
                };
                Format(writer, $"catch type: {typeName} begin: {catchHandler.Begin} end: {catchHandler.End} target: {catchHandler.Target}\n");
            }
        }
        {
            var pc = 0;
            var endPc = irep.Sequence.Length;
            ref var sequence = ref GetArrayDataReference(irep.Sequence);
            while (pc < endPc)
            {
                //TODO: Irep debug info

                var opcode = (OpCode)irep.Sequence[pc];
                if (opcode is OpCode.Nop or OpCode.Call or OpCode.KeyEnd or OpCode.Stop
                    or OpCode.EXT1 or OpCode.EXT2 or OpCode.EXT3
                    or OpCode.RetSelf or OpCode.RetNil or OpCode.RetTrue or OpCode.RetFalse)
                {
                    writer.Write(GetOpCodeName(opcode));
                    writer.Write("\n"u8);
                    pc++;
                    continue;
                }

                {
                    WriteOpCodeWithTab(opcode);
                }

                switch (opcode)
                {
                    // ReSharper disable UnreachableSwitchCaseDueToIntegerAnalysis
                    case OpCode.Nop:

                        break;
                    case OpCode.Move:
                        var bb = OperandBB.Read(ref sequence, ref pc);
                        Format(writer, $"R{bb.A}\tR{bb.B}\n");
                        break;
                    case OpCode.LoadL:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}");
                            var value = irep.PoolValues[bb.B];
                            switch (value.VType)
                            {
                                case MRubyVType.Float:
                                    Format(writer, $"L[{value.FloatValue}]\t");
                                    break;
                                case MRubyVType.Integer:
                                    Format(writer, $"L[{value.IntegerValue}]\t");
                                    break;
                                default:
                                    Format(writer, $"L[{bb.B}]\t");
                                    break;
                            }

                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.LoadI8:
                        bb = OperandBB.Read(ref sequence, ref pc);
                        Format(writer, $"R{bb.A}\t{bb.B}\t");
                        WriteLocalVariableA(bb.A);
                        break;
                    case OpCode.LoadINeg:
                        bb = OperandBB.Read(ref sequence, ref pc);
                        Format(writer, $"R{bb.A}\t-{bb.B}\t");
                        WriteLocalVariableA(bb.A);
                        break;
                    case OpCode.LoadI16:
                        var bs = OperandBS.Read(ref sequence, ref pc);
                        Format(writer, $"R{bs.A}\t{unchecked((short)bs.B)}\t");
                        WriteLocalVariableA(bs.A);
                        break;
                    case OpCode.LoadI32:
                        var bss = OperandBSS.Read(ref sequence, ref pc);
                        Format(writer, $"R{bss.A}\t{((ushort)bss.B << 16) | (ushort)bss.C}\t");
                        WriteLocalVariableA(bss.A);
                        break;
                    case OpCode.LoadI__1:
                        var b = OperandB.Read(ref sequence, ref pc);
                        Format(writer, $"tR{b.A}\t(-1)\t");
                        WriteLocalVariableA(b.A);
                        break;
                    case OpCode.LoadI_0:
                    case OpCode.LoadI_1:
                    case OpCode.LoadI_2:
                    case OpCode.LoadI_3:
                    case OpCode.LoadI_4:
                    case OpCode.LoadI_5:
                    case OpCode.LoadI_6:
                    case OpCode.LoadI_7:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            var value = (int)opcode - (int)OpCode.LoadI_0;
                            Format(writer, $"R{b.A}\t{value}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadSym:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.LoadNil:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t(nil)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadSelf:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t(R0)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadT:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t(true)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadF:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t(false)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.GetGV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetGV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetSV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetSV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetConst:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetConst:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetMCnst:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tR{bb.A}::{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetMCnst:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}::{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetIV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetIV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetUpVar:
                        var bbb = OperandBBB.Read(ref sequence, ref pc);
                    {
                        Format(writer, $"R{bbb.A}\t{bbb.B}\t{bbb.C}\t");
                        WriteLocalVariableA(bbb.A);
                        break;
                    }
                    case OpCode.SetUpVar:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t{bbb.B}\t{bbb.C}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.GetCV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetCV:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetIdx:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.GetIdx0:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tR{bb.B}\n");
                            break;
                        }
                    case OpCode.SetIdx:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\tR{b.A + 1}\tR{b.A + 2}\n");
                            break;
                        }
                    case OpCode.Jmp:
                        {
                            var s = OperandS.Read(ref sequence, ref pc);
                            var i = pc;
                            Format(writer, $"{i + s.A}\n");
                            break;
                        }
                    case OpCode.JmpUw:
                        {
                            var s = OperandS.Read(ref sequence, ref pc);
                            var i = pc;
                            Format(writer, $"{i + s.A}\n");
                            break;
                        }
                    case OpCode.JmpIf:
                        {
                            bs = OperandBS.Read(ref sequence, ref pc);
                            var i = pc;
                            Format(writer, $"R{bs.A}\t{i + bs.B}\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.JmpNot:
                        {
                            bs = OperandBS.Read(ref sequence, ref pc);
                            var i = pc;
                            Format(writer, $"R{bs.A}\t{i + bs.B}\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.JmpNil:
                        {
                            bs = OperandBS.Read(ref sequence, ref pc);
                            var i = pc;
                            Format(writer, $"R{bs.A}\t{i + bs.B}\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.SSend:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.SSendB:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.Send:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.SendB:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.SSend0:
                    case OpCode.Send0:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.BlkCall:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{bb.B}\n");
                            break;
                        }
                    case OpCode.Call:
                        {
                            break;
                        }
                    case OpCode.Super:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.ArgAry:
                        {
                            bs = OperandBS.Read(ref sequence, ref pc);
                            Format(writer, $"R{bs.A}\t{(bs.B >> 11) & 0x3f}:{(bs.B >> 10) & 0x1}:{(bs.B >> 5) & 0x1f}:{(bs.B >> 4) & 0x1f} ({bs.B & 0xf})\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.Enter:
                        {
                            var a = OperandW.Read(ref sequence, ref pc).A;
                            Format(writer, $"{(a >> 18) & 0x1f}:{(a >> 13) & 0x1f}:{(a >> 12) & 0x1}:{(a >> 7) & 0x1f}:{(a >> 2) & 0x1f}:{(a >> 1) & 0x1}:{a & 1} (0x{a:x})\n");
                            break;
                        }
                    case OpCode.KeyP:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.KeyEnd:
                        {
                            OperandZ.Read(ref sequence, ref pc);
                            break;
                        }
                    case OpCode.KArg:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Return:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.ReturnBlk:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Break:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.BlkPush:
                        {
                            bs = OperandBS.Read(ref sequence, ref pc);
                            Format(writer, $"R{bs.A}\t{(bs.B >> 11) & 0x3f}:{(bs.B >> 10) & 0x1}:{(bs.B >> 5) & 0x1f}:{(bs.B >> 4) & 0x1} ({(bs.B >> 0) & 0xf})\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.Lambda:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tI[{bb.B}]\n");
                            break;
                        }
                    case OpCode.Block:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tI[{bb.B}]\n");
                            break;
                        }
                    case OpCode.Method:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tI[{bb.B}]\n");
                            break;
                        }
                    case OpCode.RangeInc:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\n");
                            break;
                        }
                    case OpCode.RangeExc:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\n");
                            break;
                        }
                    case OpCode.Def:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t(R{bb.A + 1})\n");
                            break;
                        }
                    case OpCode.TDef:
                    case OpCode.SDef:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\tI[{bbb.C}]\n");
                            break;
                        }
                    case OpCode.Undef:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $":{symbolTable.NameOf(irep.Symbols[b.A])}\n");
                            break;
                        }
                    case OpCode.Alias:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $":{symbolTable.NameOf(irep.Symbols[bb.A])}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\n");
                            break;
                        }
                    case OpCode.Add:
                        {
                            goto case OpCode.EQ;
                        }
                    case OpCode.AddI:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Sub:
                        {
                           goto case OpCode.EQ;
                        }
                    case OpCode.SubI:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.AddILV:
                    case OpCode.SubILV:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\tR{bbb.B}\t{bbb.C}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.Mul:
                    case OpCode.Div:
                    case OpCode.LT:
                    case OpCode.LE:
                    case OpCode.GT:
                    case OpCode.GE:
                    case OpCode.EQ:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.Array:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tR{bb.A}\t{bb.B}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Array2:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\tR{bbb.B}\t{bbb.C}");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.AryCat:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\tR{b.A + 1}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.AryPush:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.ArySplat:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.ARef:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\tR{bbb.B}\t{bbb.C}");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.ASet:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\tR{bbb.B}\t{bbb.C}");
                            WriteLocalVariableAB(bbb.A, bbb.B);
                            break;
                        }
                    case OpCode.APost:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bbb.A}\t{bbb.B}\t{bbb.C}");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.Intern:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Symbol:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tL[{bb.B}]\t; {irep.PoolValues[bb.B].As<RString>().AsSpan()}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.String:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tL[{bb.B}]\t; {irep.PoolValues[bb.B].As<RString>().AsSpan()}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.StrCat:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\tR{b.A + 1}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Hash:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.HashAdd:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.HashCat:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\tR{b.A + 1}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.OClass:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Class:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Module:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Exec:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tI[{bb.B}]");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SClass:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.TClass:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Err:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            var message = irep.PoolValues[b.A];
                            if (message.Object is RString)
                                Format(writer, $"{message.As<RString>().AsSpan()}\n");
                            else Format(writer, $"L[{b.A}]\n");
                            break;
                        }
                    case OpCode.Except:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Rescue:
                        {
                            bb = OperandBB.Read(ref sequence, ref pc);
                            Format(writer, $"R{bb.A}\tR{bb.B}");
                            WriteLocalVariableAB(bb.A, bb.B);
                            break;
                        }
                    case OpCode.RaiseIf:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.MatchErr:
                        {
                            b = OperandB.Read(ref sequence, ref pc);
                            Format(writer, $"R{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Debug:
                        {
                            bbb = OperandBBB.Read(ref sequence, ref pc);
                            Format(writer, $"{bbb.A}\t{bbb.B}\t{bbb.C}\n");
                            break;
                        }
                    case OpCode.Stop:
                        {
                            OperandZ.Read(ref sequence, ref pc);
                            break;
                        }
                    case OpCode.EXT1:
                        {
                            OperandZ.Read(ref sequence, ref pc);
                            break;
                        }
                    case OpCode.EXT2:
                        {
                            OperandZ.Read(ref sequence, ref pc);
                            break;
                        }
                    case OpCode.EXT3:
                        {
                            OperandZ.Read(ref sequence, ref pc);
                            break;
                        }
                    // ReSharper restore UnreachableSwitchCaseDueToIntegerAnalysis
                }
            }

            writer.Write("\n"u8);
        }
    }
}