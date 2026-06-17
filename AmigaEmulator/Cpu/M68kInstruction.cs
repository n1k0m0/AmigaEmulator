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
        /// Represents the I ll eg al option.
        /// </summary>
        Illegal,
        /// <summary>
        /// Represents the N op option.
        /// </summary>
        Nop,
        /// <summary>
        /// Represents the R es et option.
        /// </summary>
        Reset,
        /// <summary>
        /// Represents the S to p option.
        /// </summary>
        Stop,
        /// <summary>
        /// Represents the R te option.
        /// </summary>
        Rte,
        /// <summary>
        /// Represents the R ts option.
        /// </summary>
        Rts,
        /// <summary>
        /// Represents the M ov eq option.
        /// </summary>
        Moveq,
        /// <summary>
        /// Represents the M ov e option.
        /// </summary>
        Move,
        /// <summary>
        /// Represents the M ov ea option.
        /// </summary>
        Movea,
        /// <summary>
        /// Represents the L ea option.
        /// </summary>
        Lea,
        /// <summary>
        /// Represents the P ea option.
        /// </summary>
        Pea,
        /// <summary>
        /// Represents the C lr option.
        /// </summary>
        Clr,
        /// <summary>
        /// Represents the T st option.
        /// </summary>
        Tst,
        /// <summary>
        /// Represents the O ri option.
        /// </summary>
        Ori,
        /// <summary>
        /// Represents the A nd i option.
        /// </summary>
        Andi,
        /// <summary>
        /// Represents the E or i option.
        /// </summary>
        Eori,
        /// <summary>
        /// Represents the C mp i option.
        /// </summary>
        Cmpi,
        /// <summary>
        /// Represents the C mp option.
        /// </summary>
        Cmp,
        /// <summary>
        /// Represents the A dd option.
        /// </summary>
        Add,
        /// <summary>
        /// Represents the S ub option.
        /// </summary>
        Sub,
        /// <summary>
        /// Represents the A dd q option.
        /// </summary>
        Addq,
        /// <summary>
        /// Represents the S ub q option.
        /// </summary>
        Subq,
        /// <summary>
        /// Represents the B ra option.
        /// </summary>
        Bra,
        /// <summary>
        /// Represents the B sr option.
        /// </summary>
        Bsr,
        /// <summary>
        /// Represents the B cc option.
        /// </summary>
        Bcc,
        /// <summary>
        /// Represents the J mp option.
        /// </summary>
        Jmp,
        /// <summary>
        /// Represents the J sr option.
        /// </summary>
        Jsr,
        /// <summary>
        /// Represents the L in k option.
        /// </summary>
        Link,
        /// <summary>
        /// Represents the U nl k option.
        /// </summary>
        Unlk,
        /// <summary>
        /// Represents the M ov em option.
        /// </summary>
        Movem,
        /// <summary>
        /// Represents the M ov eS r option.
        /// </summary>
        MoveSr,
        /// <summary>
        /// Represents the M ov eC cr option.
        /// </summary>
        MoveCcr,
        /// <summary>
        /// Represents the M ov eU sp option.
        /// </summary>
        MoveUsp,
        /// <summary>
        /// Represents the T ra p option.
        /// </summary>
        Trap
    }

    /// <summary>
    /// Stores 68000 Instruction data used by the emulator.
    /// </summary>
    public struct M68kInstruction
    {
        /// <summary>
        /// Stores the Kind.
        /// </summary>
        public M68kInstructionKind Kind;
        /// <summary>
        /// Stores the Name.
        /// </summary>
        public string Name;
        /// <summary>
        /// Stores the Register.
        /// </summary>
        public int Register;
        /// <summary>
        /// Stores the Size Bytes.
        /// </summary>
        public int SizeBytes;
        /// <summary>
        /// Stores the Cycles.
        /// </summary>
        public int Cycles;
    }
}
