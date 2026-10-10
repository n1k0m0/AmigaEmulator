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
    /// Decodes Motorola 68000 opcodes into executable instruction metadata.
    /// </summary>
    public static class M68kDecoder
    {
        /// <summary>
        /// Decodes value from Amiga-compatible data.
        /// </summary>
        public static M68kInstruction Decode(ushort opcode)
        {
            if ((opcode & 0xF100) == 0x7000)
            {
                return new M68kInstruction
                {
                    Kind = M68kInstructionKind.Moveq,
                    Name = "MOVEQ",
                    Register = (opcode >> 9) & 7,
                    SizeBytes = 2,
                    Cycles = 4
                };
            }

            if ((opcode & 0xF000) == 0x1000 || (opcode & 0xF000) == 0x2000 || (opcode & 0xF000) == 0x3000)
            {
                int destinationMode = (opcode >> 6) & 7;
                int size = ((opcode >> 12) & 3) == 2 ? 4 : (((opcode >> 12) & 3) == 1 ? 1 : 2);
                return Simple(destinationMode == 1 ? M68kInstructionKind.Movea : M68kInstructionKind.Move, destinationMode == 1 ? "MOVEA" : "MOVE", EstimateSizeBytes(opcode, size), 4);
            }

            if ((opcode & 0xFF00) == 0x0000)
            {
                return Immediate(M68kInstructionKind.Ori, "ORI", opcode);
            }

            if ((opcode & 0xFF00) == 0x0200)
            {
                return Immediate(M68kInstructionKind.Andi, "ANDI", opcode);
            }

            if ((opcode & 0xFF00) == 0x0A00)
            {
                return Immediate(M68kInstructionKind.Eori, "EORI", opcode);
            }

            if ((opcode & 0xFF00) == 0x0C00)
            {
                return Immediate(M68kInstructionKind.Cmpi, "CMPI", opcode);
            }

            if ((opcode & 0xF000) == 0x5000 && ((opcode >> 6) & 3) != 3)
            {
                return Simple((opcode & 0x0100) == 0 ? M68kInstructionKind.Addq : M68kInstructionKind.Subq, (opcode & 0x0100) == 0 ? "ADDQ" : "SUBQ", 2, 4);
            }

            if ((opcode & 0xF000) == 0x6000)
            {
                int condition = (opcode >> 8) & 0x0F;
                if (condition == 0)
                {
                    return Simple(M68kInstructionKind.Bra, "BRA", (opcode & 0xFF) == 0 ? 4 : 2, 10);
                }

                if (condition == 1)
                {
                    return Simple(M68kInstructionKind.Bsr, "BSR", (opcode & 0xFF) == 0 ? 4 : 2, 18);
                }

                return Simple(M68kInstructionKind.Bcc, "Bcc", (opcode & 0xFF) == 0 ? 4 : 2, 10);
            }

            if ((opcode & 0xF000) == 0x9000)
            {
                return Simple(M68kInstructionKind.Sub, "SUB", 2, 4);
            }

            if ((opcode & 0xF000) == 0xB000)
            {
                return Simple(M68kInstructionKind.Cmp, "CMP", 2, 4);
            }

            if ((opcode & 0xF000) == 0xD000)
            {
                return Simple(M68kInstructionKind.Add, "ADD", 2, 4);
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                return Simple(M68kInstructionKind.Lea, "LEA", EstimateSizeBytes(opcode, 4), 4);
            }

            if ((opcode & 0xFFC0) == 0x4840)
            {
                return Simple(M68kInstructionKind.Pea, "PEA", EstimateSizeBytes(opcode, 4), 12);
            }

            if ((opcode & 0xFB80) == 0x4880 || (opcode & 0xFB80) == 0x4C80)
            {
                return Simple(M68kInstructionKind.Movem, "MOVEM", EstimateSizeBytes(opcode, 4) + 2, 12);
            }

            if ((opcode & 0xFF00) == 0x4200)
            {
                return Simple(M68kInstructionKind.Clr, "CLR", 2, 4);
            }

            if ((opcode & 0xFFC0) == 0x4AC0)
            {
                return Simple(M68kInstructionKind.Tas, "TAS", EstimateSizeBytes(opcode, 1), 4);
            }

            if ((opcode & 0xFF00) == 0x4A00)
            {
                return Simple(M68kInstructionKind.Tst, "TST", 2, 4);
            }

            if ((opcode & 0xFFC0) == 0x40C0 || (opcode & 0xFFC0) == 0x46C0)
            {
                return Simple(M68kInstructionKind.MoveSr, "MOVE SR", 2, 4);
            }

            if ((opcode & 0xFFC0) == 0x44C0)
            {
                return Simple(M68kInstructionKind.MoveCcr, "MOVE CCR", 2, 4);
            }

            if ((opcode & 0xFFF0) == 0x4E60)
            {
                return Simple(M68kInstructionKind.MoveUsp, "MOVE USP", 2, 4);
            }

            if ((opcode & 0xFFF8) == 0x4E50)
            {
                return Simple(M68kInstructionKind.Link, "LINK", 4, 16);
            }

            if ((opcode & 0xFFF8) == 0x4E58)
            {
                return Simple(M68kInstructionKind.Unlk, "UNLK", 2, 12);
            }

            if ((opcode & 0xFFC0) == 0x4EC0)
            {
                return Simple(M68kInstructionKind.Jmp, "JMP", EstimateSizeBytes(opcode, 4), 12);
            }

            if ((opcode & 0xFFC0) == 0x4E80)
            {
                return Simple(M68kInstructionKind.Jsr, "JSR", EstimateSizeBytes(opcode, 4), 20);
            }

            if ((opcode & 0xFFF0) == 0x4E40)
            {
                return Simple(M68kInstructionKind.Trap, "TRAP", 2, 34);
            }

            switch (opcode)
            {
                case 0x4E70:
                    return Simple(M68kInstructionKind.Reset, "RESET", 2, 132);
                case 0x4E71:
                    return Simple(M68kInstructionKind.Nop, "NOP", 2, 4);
                case 0x4E72:
                    return Simple(M68kInstructionKind.Stop, "STOP", 4, 4);
                case 0x4E73:
                    return Simple(M68kInstructionKind.Rte, "RTE", 2, 20);
                case 0x4E75:
                    return Simple(M68kInstructionKind.Rts, "RTS", 2, 16);
                default:
                    return Simple(M68kInstructionKind.Illegal, "ILLEGAL", 2, 4);
            }
        }

        private static M68kInstruction Immediate(M68kInstructionKind kind, string name, ushort opcode)
        {
            int sizeCode = (opcode >> 6) & 3;
            int extensionBytes = sizeCode == 2 ? 4 : 2;
            return Simple(kind, name, 2 + extensionBytes, 4);
        }

        /// <summary>
        /// Runs Estimate Size Bytes.
        /// </summary>
        private static int EstimateSizeBytes(ushort opcode, int size)
        {
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (mode == 5 || mode == 6)
            {
                return 4;
            }

            if (mode == 7)
            {
                if (register == 1)
                {
                    return 6;
                }

                if (register == 0 || register == 2 || register == 3 || register == 4)
                {
                    return register == 4 && size == 4 ? 6 : 4;
                }
            }

            return 2;
        }

        private static M68kInstruction Simple(M68kInstructionKind kind, string name, int sizeBytes, int cycles)
        {
            return new M68kInstruction
            {
                Kind = kind,
                Name = name,
                Register = -1,
                SizeBytes = sizeBytes,
                Cycles = cycles
            };
        }
    }
}
