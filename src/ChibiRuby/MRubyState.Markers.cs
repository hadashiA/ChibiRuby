using System.Diagnostics;
using System.Runtime.CompilerServices;

// ReSharper disable InconsistentNaming
// ReSharper disable MemberCanBePrivate.Local
namespace ChibiRuby;

partial class MRubyState
{
    static class Markers
    {
        static Markers()
        {
            InitializeMarkers();
        }

        /// <summary>
        /// This method is used to call the marker method once for the JIT decompiler.
        /// ClrMd, used for the JIT decompiler, cannot identify a method unless that method is called at least once.
        /// </summary>
        [Conditional("CASE_MARKER")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InitializeMarkers()
        {
            Nop();
            Move();
            LoadL();
            LoadI8();
            LoadINeg();
            LoadI__1();
            LoadI_0();
            LoadI_1();
            LoadI_2();
            LoadI_3();
            LoadI_4();
            LoadI_5();
            LoadI_6();
            LoadI_7();
            LoadI16();
            LoadI32();
            LoadSym();
            LoadNil();
            LoadSelf();
            LoadT();
            LoadF();
            GetGV();
            SetGV();
            GetSV();
            SetSV();
            GetIV();
            SetIV();
            GetCV();
            SetCV();
            GetConst();
            SetConst();
            GetMCnst();
            SetMCnst();
            GetUpVar();
            SetUpVar();
            GetIdx();
            GetIdx0();
            SetIdx();
            Jmp();
            JmpIf();
            JmpNot();
            JmpNil();
            JmpUw();
            Except();
            Rescue();
            RaiseIf();
            MatchErr();
            SSend();
            SSend0();
            SSendB();
            Send();
            Send0();
            SendB();
            Call();
            BlkCall();
            Super();
            ArgAry();
            Enter();
            KeyP();
            KeyEnd();
            KArg();
            Return();
            ReturnBlk();
            RetSelf();
            RetNil();
            RetTrue();
            RetFalse();
            Break();
            BlkPush();
            Add();
            AddI();
            Sub();
            SubI();
            AddILV();
            SubILV();
            Mul();
            Div();
            EQ();
            LT();
            LE();
            GT();
            GE();
            Array();
            Array2();
            AryCat();
            AryPush();
            ArySplat();
            ARef();
            ASet();
            APost();
            Intern();
            Symbol();
            String();
            StrCat();
            Hash();
            HashAdd();
            HashCat();
            Lambda();
            Block();
            Method();
            RangeInc();
            RangeExc();
            OClass();
            Class();
            Module();
            Exec();
            Def();
            TDef();
            SDef();
            Alias();
            Undef();
            SClass();
            TClass();
            Debug();
            Err();
            EXT1();
            EXT2();
            EXT3();
            Stop();
            SendInternal();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Nop()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Move()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadL()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI8()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadINeg()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI__1()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_0()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_1()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_2()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_3()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_4()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_5()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_6()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI_7()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI16()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadI32()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadSym()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadNil()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadSelf()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadT()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadF()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetGV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetGV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetSV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetSV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetIV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetIV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetCV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetCV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetConst()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetConst()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetMCnst()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetMCnst()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetUpVar()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetUpVar()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetIdx()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetIdx()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Jmp()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void JmpIf()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void JmpNot()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void JmpNil()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void JmpUw()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Except()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Rescue()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RaiseIf()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SSend()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SSendB()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Send()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SendB()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Call()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Super()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ArgAry()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Enter()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void KeyP()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void KeyEnd()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void KArg()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Return()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ReturnBlk()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Break()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void BlkPush()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Add()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void AddI()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Sub()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SubI()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Mul()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Div()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void EQ()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LT()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LE()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GT()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GE()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Array()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Array2()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void AryCat()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void AryPush()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ArySplat()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ARef()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ASet()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void APost()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Intern()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Symbol()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void String()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void StrCat()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Hash()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void HashAdd()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void HashCat()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Lambda()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Block()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Method()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RangeInc()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RangeExc()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void OClass()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Class()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Module()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Exec()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Def()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Alias()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Undef()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SClass()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void TClass()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Debug()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Err()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void EXT1()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void EXT2()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void EXT3()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Stop()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SendInternal()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetIdx0()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void MatchErr()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SSend0()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Send0()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void BlkCall()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RetSelf()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RetNil()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RetTrue()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void RetFalse()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void AddILV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SubILV()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void TDef()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SDef()
        {
        }
    }
}