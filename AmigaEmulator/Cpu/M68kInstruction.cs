/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
namespace AmigaEmulator.Core.Cpu
{
    /// <summary>
    /// Lists the supported 68000 Instruction Kind values.
    /// </summary>
    public enum M68kInstructionKind
    {
        /// <summary>
        /// Represents Illegal.
        /// </summary>
        Illegal,
        /// <summary>
        /// Represents Nop.
        /// </summary>
        Nop,
        /// <summary>
        /// Represents Reset.
        /// </summary>
        Reset,
        /// <summary>
        /// Represents Stop.
        /// </summary>
        Stop,
        /// <summary>
        /// Represents Rte.
        /// </summary>
        Rte,
        /// <summary>
        /// Represents Rts.
        /// </summary>
        Rts,
        /// <summary>
        /// Represents Moveq.
        /// </summary>
        Moveq,
        /// <summary>
        /// Represents Move.
        /// </summary>
        Move,
        /// <summary>
        /// Represents Movea.
        /// </summary>
        Movea,
        /// <summary>
        /// Represents Lea.
        /// </summary>
        Lea,
        /// <summary>
        /// Represents Pea.
        /// </summary>
        Pea,
        /// <summary>
        /// Represents Clr.
        /// </summary>
        Clr,
        /// <summary>
        /// Represents Tst.
        /// </summary>
        Tst,
        /// <summary>
        /// Represents Ori.
        /// </summary>
        Ori,
        /// <summary>
        /// Represents Andi.
        /// </summary>
        Andi,
        /// <summary>
        /// Represents Eori.
        /// </summary>
        Eori,
        /// <summary>
        /// Represents Cmpi.
        /// </summary>
        Cmpi,
        /// <summary>
        /// Represents Cmp.
        /// </summary>
        Cmp,
        /// <summary>
        /// Represents Add.
        /// </summary>
        Add,
        /// <summary>
        /// Represents Sub.
        /// </summary>
        Sub,
        /// <summary>
        /// Represents Addq.
        /// </summary>
        Addq,
        /// <summary>
        /// Represents Subq.
        /// </summary>
        Subq,
        /// <summary>
        /// Represents Bra.
        /// </summary>
        Bra,
        /// <summary>
        /// Represents Bsr.
        /// </summary>
        Bsr,
        /// <summary>
        /// Represents Bcc.
        /// </summary>
        Bcc,
        /// <summary>
        /// Represents Jmp.
        /// </summary>
        Jmp,
        /// <summary>
        /// Represents Jsr.
        /// </summary>
        Jsr,
        /// <summary>
        /// Represents Link.
        /// </summary>
        Link,
        /// <summary>
        /// Represents Unlk.
        /// </summary>
        Unlk,
        /// <summary>
        /// Represents Movem.
        /// </summary>
        Movem,
        /// <summary>
        /// Represents Move SR.
        /// </summary>
        MoveSr,
        /// <summary>
        /// Represents Move Ccr.
        /// </summary>
        MoveCcr,
        /// <summary>
        /// Represents Move Usp.
        /// </summary>
        MoveUsp,
        /// <summary>
        /// Represents Trap.
        /// </summary>
        Trap
    }

    /// <summary>
    /// Stores 68000 Instruction data used by the emulator.
    /// </summary>
    public struct M68kInstruction
    {
        /// <summary>
        /// Stores Kind.
        /// </summary>
        public M68kInstructionKind Kind;
        /// <summary>
        /// Stores Name.
        /// </summary>
        public string Name;
        /// <summary>
        /// Stores Register.
        /// </summary>
        public int Register;
        /// <summary>
        /// Stores Size Bytes.
        /// </summary>
        public int SizeBytes;
        /// <summary>
        /// Stores Cycles.
        /// </summary>
        public int Cycles;
    }
}
